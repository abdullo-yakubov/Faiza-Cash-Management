using FaizaCash.Domain;
using FaizaCash.Application;
using FaizaCash.Infrastructure;
using FaizaCash.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;
using static FaizaCash.Api.OperationsValidators;
using static FaizaCash.Api.BackupResults;

namespace FaizaCash.Api;

/// <summary>Модуль «Shifts»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class ShiftsEndpoints
{
    public static IEndpointRouteBuilder MapShiftsEndpoints(this IEndpointRouteBuilder app)
    {
        var shifts = app.MapGroup("/api/shifts").RequireAuthorization().WithTags("Shifts");
shifts.MapGet("", [Authorize] async (long cashboxId, DateOnly? date, HttpContext ctx, CashDb db, ICashboxAccess access) =>
{
    await access.Require(ctx.UserId(), cashboxId, ctx.RequestAborted);
    var query=db.Shifts.AsNoTracking().Where(x=>x.CashboxId==cashboxId);
    if(date.HasValue) query=query.Where(x=>x.Date==date.Value);
    return Results.Ok(await query.OrderByDescending(x=>x.Date).Take(30).ToListAsync(ctx.RequestAborted));
});
shifts.MapPost("/open", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (ShiftOpenInput x, HttpContext ctx, CashDb db, ICashboxAccess access, IBalanceService balances, Audit audit) =>
{
    await access.Require(ctx.UserId(),x.CashboxId,ctx.RequestAborted);
    if(x.OpeningBalance<0 || x.Date==DateOnly.MinValue || x.Date>Local.Today || decimal.Round(x.OpeningBalance,2)!=x.OpeningBalance) return Results.Problem("Укажите неотрицательный остаток и дату не позже сегодняшней.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var box=await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={x.CashboxId} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(box is null || !box.Active) return Results.NotFound();
    if(await db.Shifts.AnyAsync(s=>s.CashboxId==x.CashboxId&&s.Status=="open",ctx.RequestAborted)) return Results.Problem("У кассы уже есть открытая смена.",statusCode:409);
    if(await db.Openings.AnyAsync(o=>o.CashboxId==x.CashboxId&&o.Date>x.Date,ctx.RequestAborted)) return Results.Problem("Касса начинает учёт позже выбранной даты.",statusCode:400);
    // D-03: the declared opening balance must equal the ledger balance; a mismatch is reported, not silently stored.
    var expected=await balances.BalanceAt(x.CashboxId,x.Date,ctx.RequestAborted);
    if(x.OpeningBalance!=expected)
        return Results.Problem($"Начальный остаток смены должен совпадать с расчётным остатком кассы: {expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)}. Пересчитайте наличные; расхождение фиксируется при закрытии смены.",statusCode:409,extensions:new Dictionary<string,object?>{["expectedBalance"]=expected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)});
    var shift=new CashShift{CashboxId=x.CashboxId,Date=x.Date,OpeningBalance=x.OpeningBalance,OpenedBy=ctx.UserId()};
    db.Shifts.Add(shift); await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"shift_opened","shift",shift.Id,new{shift.CashboxId,shift.Date,shift.OpeningBalance});
    await tx.CommitAsync(ctx.RequestAborted); return Results.Created($"/api/shifts/{shift.Id}",shift);
});
shifts.MapPost("/{id:long}/close", [Authorize(Roles = Admin + "," + Accountant + ",cashier")] async (long id, ShiftCloseInput x, HttpContext ctx, CashDb db, ICashboxAccess access, IBalanceService balances, Audit audit) =>
{
    if(x.ClosingBalance<0||decimal.Round(x.ClosingBalance,2)!=x.ClosingBalance)return Results.Problem("Фактический остаток должен быть неотрицательным, с точностью до дирама.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var shift=await db.Shifts.FromSqlInterpolated($"SELECT * FROM \"Shifts\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(shift is null) return Results.NotFound();
    await access.Require(ctx.UserId(),shift.CashboxId,ctx.RequestAborted);
    if(!ShiftRules.CanClose(shift.Status,shift.OpeningBalance,x.ClosingBalance)) return Results.Problem("Смена уже закрыта.",statusCode:409);
    var expected=await balances.BalanceAt(shift.CashboxId,shift.Date,ctx.RequestAborted);
    shift.ClosingBalance=x.ClosingBalance; shift.ClosingDifference=ShiftRules.ClosingDifference(expected,x.ClosingBalance); shift.Status="closed"; shift.ClosedBy=ctx.UserId(); shift.ClosedAt=DateTime.UtcNow;
    await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"shift_closed","shift",shift.Id,new{shift.CashboxId,shift.Date,expected,shift.ClosingBalance,shift.ClosingDifference});
    await tx.CommitAsync(ctx.RequestAborted); return Results.Ok(shift);
});
        return app;
    }
}
