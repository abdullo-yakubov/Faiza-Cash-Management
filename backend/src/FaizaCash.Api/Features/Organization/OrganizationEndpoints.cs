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

/// <summary>Модуль «Organization»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var currencies = app.MapGroup("/api/currencies").RequireAuthorization().WithTags("Organization");
        var branches = app.MapGroup("/api/branches").RequireAuthorization().WithTags("Organization");
        var cashboxes = app.MapGroup("/api/cashboxes").RequireAuthorization().WithTags("Organization");
        var settings = app.MapGroup("/api/settings").RequireAuthorization().WithTags("Organization");
currencies.MapGet("", [Authorize] async (CashDb db) => Results.Ok(await db.Currencies.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));
branches.MapGet("", [Authorize] async (HttpContext ctx, CashDb db, bool? active, int? page, int? pageSize) => await ListPage(ctx,db.Branches.AsNoTracking().Where(x=>!active.HasValue||x.Active==active).OrderBy(x=>x.Name).ThenBy(x=>x.Id),page,pageSize,ctx.RequestAborted));
branches.MapPost("", [Authorize(Roles = Admin)] async (BranchInput x, CashDb db, Audit audit, HttpContext ctx) => { var code=x.Code?.Trim().ToUpperInvariant(); var name=x.Name?.Trim(); var city=x.City?.Trim(); if(string.IsNullOrWhiteSpace(code)||code.Length>24||string.IsNullOrWhiteSpace(name)||name.Length>160||string.IsNullOrWhiteSpace(city)||city.Length>100||code.Contains('\0')||name.Contains('\0')||city.Contains('\0')) return Results.BadRequest(); if(await db.Branches.AnyAsync(b=>b.Code==code)) return Results.Conflict(); var branch=new Branch{Code=code,Name=name,City=city}; db.Branches.Add(branch); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"branch_created","branch",branch.Id,new{branch.Code,branch.City}); return Results.Created($"/api/branches/{branch.Id}",branch); });
branches.MapPatch("/{id:long}", [Authorize(Roles = Admin)] async (long id, BranchPatch x, CashDb db, Audit audit, HttpContext ctx) => { var branch=await db.Branches.FindAsync(id); if(branch is null)return Results.NotFound(); if(x.Name is not null){if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>160)return Results.BadRequest();branch.Name=x.Name.Trim();} if(x.City is not null){if(string.IsNullOrWhiteSpace(x.City)||x.City.Trim().Length>100)return Results.BadRequest();branch.City=x.City.Trim();} if(x.Active.HasValue)branch.Active=x.Active.Value; await db.SaveChangesAsync(); await audit.Add(ctx.UserId(),"branch_updated","branch",id,new{x.Name,x.City,x.Active}); return Results.NoContent(); });
cashboxes.MapGet("", [Authorize] async (HttpContext ctx,CashDb db,ICashboxAccess access,bool? active = null, long? branchId = null) => {var allowed=await access.AllowedCashboxes(ctx.UserId(),ctx.RequestAborted);return Results.Ok(await db.Cashboxes.Include(x => x.Currency).Include(x=>x.Branch).Where(x => allowed.Contains(x.Id)&&(!active.HasValue || x.Active == active)&&(!branchId.HasValue||x.BranchId==branchId)).Select(x => new { x.Id, x.Code, x.Name, branchId=x.BranchId, branch=x.Branch.Name, city=x.Branch.City, currency = x.Currency.Code, x.BalanceLimit, x.Active, openingDate=x.Opening==null?(DateOnly?)null:x.Opening.Date, openingAmount=x.Opening==null?(decimal?)null:x.Opening.Amount }).ToListAsync());});
cashboxes.MapPost("/{id:long}/opening", [Authorize(Roles = Admin)] async (long id, CashboxOpeningInput x, HttpContext ctx, CashDb db, Audit audit) =>
{
    if(x.Date==DateOnly.MinValue||x.Date>Local.Today||x.Amount<0||x.Amount>9999999999999999.99m||decimal.Round(x.Amount,2)!=x.Amount)
        return Results.Problem("Укажите дату не позже сегодняшней и неотрицательную сумму с точностью до дирама.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var box=await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(box is null)return Results.NotFound();
    if(!box.Active)return Results.Problem("Касса неактивна.",statusCode:409);
    if(await db.Openings.AnyAsync(o=>o.CashboxId==id,ctx.RequestAborted))return Results.Problem("Начальный остаток кассы уже задан и не изменяется.",statusCode:409);
    if(await db.Operations.AnyAsync(o=>o.CashboxId==id&&o.Status=="posted",ctx.RequestAborted)||await db.Shifts.AnyAsync(s=>s.CashboxId==id,ctx.RequestAborted))
        return Results.Problem("Начальный остаток задаётся до первой смены и первого проведённого документа.",statusCode:409);
    if(await db.Operations.AnyAsync(o=>o.CashboxId==id&&o.Date<x.Date,ctx.RequestAborted))
        return Results.Problem("Есть черновики раньше даты начального остатка. Измените их дату или удалите.",statusCode:409);
    if(box.BalanceLimit.HasValue&&x.Amount>box.BalanceLimit.Value)return Results.Problem("Начальный остаток превышает лимит кассы.",statusCode:409);
    var opening=new CashboxOpening{CashboxId=id,Date=x.Date,Amount=x.Amount,CreatedBy=ctx.UserId()};
    db.Openings.Add(opening);await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"cashbox_opening_set","cashbox",id,new{date=x.Date,amount=x.Amount});
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.Created($"/api/cashboxes/{id}/opening",new{cashboxId=id,date=opening.Date,amount=opening.Amount});
});
cashboxes.MapPost("", [Authorize(Roles = Admin)] async (CashboxInput x, HttpContext ctx, CashDb db, Audit audit) =>
{
    if (string.IsNullOrWhiteSpace(x.Code) || x.Code.Trim().Length>24 || !System.Text.RegularExpressions.Regex.IsMatch(x.Code.Trim(),"^[A-Za-z0-9-]+$") || string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length>160 || x.Name.Contains('\0') || x.BalanceLimit < 0 || !await db.Currencies.AnyAsync(c => c.Id == x.CurrencyId) || !await db.Branches.AnyAsync(b=>b.Id==x.BranchId&&b.Active)) return Results.Problem("Некорректные реквизиты кассы или филиала.", statusCode: 400);
    if(!await db.Currencies.AnyAsync(c=>c.Id==x.CurrencyId && c.Code==db.Organizations.Select(o=>o.Currency).First())) return Results.Problem("Касса должна использовать валюту организации.",statusCode:400);
    var code = x.Code.Trim().ToUpperInvariant(); if (await db.Cashboxes.AnyAsync(c => c.Code == code)) return Results.Problem("Код кассы уже существует.", statusCode: 409);
    var c = new Cashbox { Code = code, Name = x.Name.Trim(), BranchId=x.BranchId, CurrencyId = x.CurrencyId, BalanceLimit = x.BalanceLimit }; db.Cashboxes.Add(c); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "cashbox_created", "cashbox", c.Id, new { c.Code, c.BranchId }); return Results.Created($"/api/cashboxes/{c.Id}", new {c.Id,c.Code,c.Name,c.BranchId,c.Active});
});
cashboxes.MapPatch("/{id:long}", [Authorize(Roles = Admin)] async (long id, CashboxPatch x, HttpContext ctx, CashDb db, Audit audit) =>
{
    var c = await db.Cashboxes.FindAsync(id); if (c is null) return Results.NotFound();
    if(x.Name is not null && (string.IsNullOrWhiteSpace(x.Name)||x.Name.Trim().Length>160||x.Name.Contains('\0')))return Results.Problem("Укажите название кассы длиной до 160 символов.",statusCode:400);
    if(x.BalanceLimit<0)return Results.Problem("Лимит не может быть отрицательным.",statusCode:400);
    if(x.Active==false&&c.Active&&await db.Shifts.AnyAsync(s=>s.CashboxId==id&&s.Status=="open"))return Results.Problem("Перед деактивацией закройте открытую смену кассы.",statusCode:409);
    var before=new{c.Name,c.Active,c.BalanceLimit};
    if (x.Name is not null) c.Name = x.Name.Trim(); if (x.Active.HasValue) c.Active = x.Active.Value; if (x.BalanceLimit.HasValue) c.BalanceLimit = x.BalanceLimit;
    await db.SaveChangesAsync();
    await audit.Add(ctx.UserId(),"cashbox_updated","cashbox",id,new{before,after=new{c.Name,c.Active,c.BalanceLimit}});
    if(before.Active&&!c.Active)await audit.Add(ctx.UserId(),"cashbox_deactivated","cashbox",id,new{c.Code});
    return Results.NoContent();
});
cashboxes.MapGet("/{id:long}/balance", [Authorize] async (long id, DateOnly? date,HttpContext ctx,IBalanceService balances,ICashboxAccess access) => {await access.Require(ctx.UserId(),id,ctx.RequestAborted);var day=date ?? Local.Today;return Results.Ok(new BalanceDto(id,day,await balances.BalanceAt(id,day,ctx.RequestAborted)));});
settings.MapGet("", [Authorize] async (CashDb db)=>Results.Ok(await db.Organizations.SingleAsync()));
settings.MapPut("", [Authorize(Roles=Admin)] async (OrganizationInput x,CashDb db,Audit audit,HttpContext ctx)=> { if(OrganizationValidators.Settings(x) is {} invalid)return invalid;var org=await db.Organizations.SingleAsync();if(org.Currency!=x.Currency&&await db.Operations.AnyAsync())return Results.Problem("Валюта не меняется после начала учёта.",statusCode:409);var before=new{org.Name,org.Currency,org.ExpenseApprovalThreshold};org.Name=x.Name.Trim();org.Currency=x.Currency.ToUpperInvariant();org.ExpenseApprovalThreshold=x.ExpenseApprovalThreshold;var currency=await db.Currencies.FirstAsync();currency.Code=org.Currency;await db.SaveChangesAsync();await audit.Add(ctx.UserId(),"settings_updated","organization",org.Id,new{before,after=new{org.Name,org.Currency,org.ExpenseApprovalThreshold}});return Results.NoContent(); });
        return app;
    }
}
