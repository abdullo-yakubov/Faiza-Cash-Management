using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;

namespace FaizaCash.Api;

/// <summary>
/// Справочник номиналов и акты инвентаризации наличных (контракт волны 1, п.4).
/// Расчётный остаток берётся из единого IBalanceService под блокировкой кассы (как при проведении).
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var denominations = app.MapGroup("/api/denominations").RequireAuthorization().WithTags("Inventory");
        denominations.MapGet("", [Authorize] async (string? currency, CashDb db, CancellationToken ct) =>
        {
            var code = (currency ?? await db.Organizations.Select(o => o.Currency).SingleAsync(ct)).Trim().ToUpperInvariant();
            if (code.Length != 3 || !code.All(char.IsAsciiLetter)) return Results.Problem("Код валюты — три латинские буквы.", statusCode: 400);
            var rows = await db.Denominations.AsNoTracking().Where(d => d.Currency == code && d.Active).OrderByDescending(d => d.Value).ThenBy(d => d.Kind).Select(d => new { d.Value, d.Kind }).ToListAsync(ct);
            return Results.Ok(rows);
        });
        denominations.MapPut("", [Authorize(Roles = Admin)] async (DenominationsInput x, CashDb db, HttpContext ctx, Audit audit) =>
        {
            var code = x.Currency?.Trim().ToUpperInvariant();
            if (code is not { Length: 3 } || !code.All(char.IsAsciiLetter)) return Results.Problem("Код валюты — три латинские буквы.", statusCode: 400);
            if (DenominationRules.CatalogError(x.Items) is { } error) return Results.Problem(error, statusCode: 400);
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var existing = await db.Denominations.Where(d => d.Currency == code).ToListAsync(ctx.RequestAborted);
            foreach (var row in existing) row.Active = x.Items!.Any(i => i.Value == row.Value && i.Kind == row.Kind);
            foreach (var item in x.Items!.Where(i => !existing.Any(e => e.Value == i.Value && e.Kind == i.Kind)))
                db.Denominations.Add(new Denomination { Currency = code, Value = item.Value, Kind = item.Kind! });
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "denominations_updated", "denomination", null, new { currency = code, items = x.Items!.Count });
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.NoContent();
        });

        var inventories = app.MapGroup("/api/cash-inventories").RequireAuthorization().WithTags("Inventory");
        inventories.MapPost("", [Authorize(Roles = InventoryOperators)] async (CashInventoryInput x, HttpContext ctx, CashDb db, ICashboxAccess access, IBalanceService balances, Audit audit) =>
        {
            if (x.CashboxId <= 0) return Results.Problem("Укажите кассу.", statusCode: 400);
            await access.Require(ctx.UserId(), x.CashboxId, ctx.RequestAborted);
            var (members, commissionError) = DenominationRules.Commission(x.Commission);
            if (commissionError is not null) return Results.Problem(commissionError, statusCode: 400);
            var note = x.Note?.Trim();
            if (note is { Length: > 1000 } || note?.Contains('\0') == true) return Results.Problem("Примечание — не длиннее 1000 символов.", statusCode: 400);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ctx.RequestAborted);
            // Блокировка кассы сериализует акт с проведением документов: расчётный остаток не «уплывёт» во время пересчёта.
            var box = await db.Cashboxes.FromSqlInterpolated($"SELECT * FROM \"Cashboxes\" WHERE \"Id\"={x.CashboxId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ctx.RequestAborted);
            if (box is null || !box.Active) return Results.Problem("Касса не найдена или неактивна.", statusCode: 404);
            var (lines, error) = DenominationRules.Normalize(x.Denominations, await ShiftsValidators.Allowed(db, box.Id, ctx.RequestAborted));
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var today = Local.Today;
            var expected = await balances.BalanceAt(box.Id, today, ctx.RequestAborted);
            var actual = DenominationRules.Total(lines!);
            var act = new CashInventory { CashboxId = box.Id, Date = today, ExpectedBalance = expected, ActualBalance = actual, Difference = actual - expected, Denominations = lines!, Commission = members!, Note = string.IsNullOrEmpty(note) ? null : note, CreatedBy = ctx.UserId() };
            db.CashInventories.Add(act);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "cash_inventory_created", "cash_inventory", act.Id, new { act.CashboxId, act.ExpectedBalance, act.ActualBalance, act.Difference });
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Created($"/api/cash-inventories/{act.Id}", View(act, box));
        });
        inventories.MapGet("", [Authorize] async (long? cashboxId, HttpContext ctx, CashDb db, ICashboxAccess access, int page = 1, int pageSize = 25) =>
        {
            if (cashboxId.HasValue) await access.Require(ctx.UserId(), cashboxId.Value, ctx.RequestAborted);
            var allowed = await access.AllowedCashboxes(ctx.UserId(), ctx.RequestAborted);
            var boxes = await db.Cashboxes.AsNoTracking().Where(c => allowed.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ctx.RequestAborted);
            var query = db.CashInventories.AsNoTracking().Where(i => allowed.Contains(i.CashboxId) && (!cashboxId.HasValue || i.CashboxId == cashboxId)).OrderByDescending(i => i.CountedAt).ThenByDescending(i => i.Id);
            return await Paged(query, page, pageSize, i => View(i, boxes[i.CashboxId]));
        });
        inventories.MapGet("/{id:long}", [Authorize] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access) =>
        {
            var act = await db.CashInventories.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ctx.RequestAborted);
            if (act is null) return Results.NotFound();
            await access.Require(ctx.UserId(), act.CashboxId, ctx.RequestAborted);
            return Results.Ok(View(act, await db.Cashboxes.AsNoTracking().SingleAsync(c => c.Id == act.CashboxId, ctx.RequestAborted)));
        });
        inventories.MapGet("/{id:long}/act.pdf", [Authorize] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access, IPdfReport pdf, Audit audit) =>
        {
            var act = await db.CashInventories.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ctx.RequestAborted);
            if (act is null) return Results.NotFound();
            await access.Require(ctx.UserId(), act.CashboxId, ctx.RequestAborted);
            var box = await db.Cashboxes.AsNoTracking().Include(c => c.Currency).SingleAsync(c => c.Id == act.CashboxId, ctx.RequestAborted);
            var org = await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.SpecifyKind(act.CountedAt, DateTimeKind.Utc), "Asia/Dushanbe");
            var bytes = pdf.GenerateInventoryAct(new InventoryActData(org.Name, box.Currency.Code, act.Id, local, box.Name, box.Code, act.Denominations, act.ExpectedBalance, act.ActualBalance, act.Difference, act.Commission, act.Note));
            await audit.Add(ctx.UserId(), "report_exported", "cash_inventory", act.Id, new { report = "inventory_act", format = "pdf" });
            return Results.File(bytes, "application/pdf", $"inventory-act-{act.Id}.pdf");
        });
        return app;
    }

    private static object View(CashInventory x, Cashbox box) => new
    {
        x.Id, x.CashboxId, cashbox = box.Name, x.Date, x.CountedAt, x.ExpectedBalance, x.ActualBalance, x.Difference,
        result = x.Difference == 0 ? "match" : x.Difference > 0 ? "surplus" : "shortage",
        x.Denominations, x.Commission, x.Note, x.CreatedBy
    };
}
