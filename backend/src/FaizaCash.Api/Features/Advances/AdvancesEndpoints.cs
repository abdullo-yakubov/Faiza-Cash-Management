using System.Globalization;
using System.Text;
using FaizaCash.Application;
using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using static FaizaCash.Api.RoleNames;
using static FaizaCash.Api.EndpointHelpers;

namespace FaizaCash.Api;

/// <summary>
/// Авансы подотчётным лицам (контракт волны 1, п.3): выдача из проведённого РКО → авансовый отчёт →
/// закрытие возвратом остатка (ПКО) или возмещением перерасхода (РКО). Правила — <see cref="AdvanceRules"/>.
/// </summary>
public static class AdvancesEndpoints
{
    private static string Money2(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static IEndpointRouteBuilder MapAdvancesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/advances").RequireAuthorization().WithTags("Advances");
        group.MapGet("", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, string? status, long? counterpartyId, long? cashboxId, bool? overdue, int page = 1, int pageSize = 25) =>
        {
            if (status is not (null or AdvanceRules.Issued or AdvanceRules.Reported or AdvanceRules.Closed)) return Results.Problem("Статус аванса: issued, reported или closed.", statusCode: 400);
            var query = await Scoped(db, ctx, access, cashboxId);
            var today = Local.Today;
            query = query.Where(x => (status == null || x.Status == status) && (!counterpartyId.HasValue || x.CounterpartyId == counterpartyId) && (overdue != true || x.Status == AdvanceRules.Issued && x.DueDate < today));
            return await Paged(query.OrderBy(x => x.Status == AdvanceRules.Closed).ThenBy(x => x.DueDate).ThenByDescending(x => x.Id), page, pageSize, x => Summary(x, today));
        });
        group.MapGet("/{id:long}", [Authorize] async (long id, HttpContext ctx, CashDb db, ICashboxAccess access) =>
        {
            var advance = await db.Advances.AsNoTracking().Include(x => x.Counterparty).Include(x => x.Lines).ThenInclude(x => x.Category).SingleOrDefaultAsync(x => x.Id == id, ctx.RequestAborted);
            if (advance is null) return Results.NotFound();
            await access.Require(ctx.UserId(), advance.CashboxId, ctx.RequestAborted);
            return Results.Ok(await Details(db, advance, ctx.RequestAborted));
        });
        group.MapPost("", [Authorize(Roles = Accountant)] async (AdvanceInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var op = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={x.OperationId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ctx.RequestAborted);
            if (op is null) return Results.Problem("Расходный ордер не найден.", statusCode: 404);
            await access.Require(ctx.UserId(), op.CashboxId, ctx.RequestAborted);
            var kind = op.CounterpartyId.HasValue ? await db.Counterparties.Where(c => c.Id == op.CounterpartyId).Select(c => c.Kind).SingleAsync(ctx.RequestAborted) : null;
            if (AdvanceRules.IssueError(op.Status, op.Type, op.IsReversal, op.ReversalId.HasValue, op.TransferId.HasValue, kind) is { } issue) return Results.Problem(issue, statusCode: 409);
            if (AdvanceRules.PurposeError(x.Purpose, x.DueDate, op.Date) is { } purpose) return Results.Problem(purpose, statusCode: 400);
            if (await db.Advances.AnyAsync(a => a.OperationId == op.Id || a.SettlementOperationId == op.Id, ctx.RequestAborted))
                return Results.Problem("Этот расходный ордер уже связан с авансом.", statusCode: 409);
            var advance = new Advance { OperationId = op.Id, CashboxId = op.CashboxId, CounterpartyId = op.CounterpartyId!.Value, Amount = op.Amount, Purpose = x.Purpose!.Trim(), IssueDate = op.Date, DueDate = x.DueDate, CreatedBy = ctx.UserId() };
            db.Advances.Add(advance);
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "advance_issued", "advance", advance.Id, new { advance.OperationId, advance.CounterpartyId, advance.Amount, advance.DueDate });
            await tx.CommitAsync(ctx.RequestAborted);
            return Results.Created($"/api/advances/{advance.Id}", Summary(advance, Local.Today));
        });
        group.MapPost("/{id:long}/report", [Authorize(Roles = Accountant)] async (long id, AdvanceReportInput x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var advance = await db.Advances.FromSqlInterpolated($"SELECT * FROM \"Advances\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
            if (advance is null) return Results.NotFound();
            await access.Require(ctx.UserId(), advance.CashboxId, ctx.RequestAborted);
            if (advance.Status != AdvanceRules.Issued) return Results.Problem("Авансовый отчёт уже принят.", statusCode: 409);
            if (AdvanceRules.LinesError(x.Lines, advance.IssueDate, Local.Today) is { } error) return Results.Problem(error, statusCode: 400);
            var categoryIds = x.Lines!.Select(l => l.CategoryId).Distinct().ToArray();
            var valid = await db.Categories.CountAsync(c => categoryIds.Contains(c.Id) && c.Type == "expense" && c.Active && !c.IsSystem, ctx.RequestAborted);
            if (valid != categoryIds.Length) return Results.Problem("Каждая строка отчёта должна ссылаться на активную категорию расхода.", statusCode: 400);
            var spent = AdvanceRules.Spent(x.Lines!);
            db.AdvanceReportLines.AddRange(x.Lines!.Select(l => new AdvanceReportLine { AdvanceId = id, Date = l.Date, Description = l.Description!.Trim(), Amount = l.Amount, CategoryId = l.CategoryId }));
            advance.Spent = spent; advance.Status = AdvanceRules.Reported; advance.ReportedBy = ctx.UserId(); advance.ReportedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "advance_reported", "advance", id, new { lines = x.Lines!.Count, spent, remainder = AdvanceRules.Remainder(advance.Amount, spent) });
            await tx.CommitAsync(ctx.RequestAborted);
            var full = await db.Advances.AsNoTracking().Include(a => a.Counterparty).Include(a => a.Lines).ThenInclude(l => l.Category).SingleAsync(a => a.Id == id, ctx.RequestAborted);
            return Results.Ok(await Details(db, full, ctx.RequestAborted));
        });
        group.MapPost("/{id:long}/settle", [Authorize(Roles = Accountant)] async (long id, AdvanceSettleInput? x, HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            var advance = await db.Advances.FromSqlInterpolated($"SELECT * FROM \"Advances\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
            if (advance is null) return Results.NotFound();
            await access.Require(ctx.UserId(), advance.CashboxId, ctx.RequestAborted);
            CashOperation? op = null;
            if (x?.OperationId is long operationId)
            {
                op = await db.Operations.FromSqlInterpolated($"SELECT * FROM \"Operations\" WHERE \"Id\"={operationId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ctx.RequestAborted);
                if (op is null) return Results.Problem("Операция закрытия не найдена.", statusCode: 404);
                await access.Require(ctx.UserId(), op.CashboxId, ctx.RequestAborted);
                var currencies = await db.Cashboxes.Where(c => c.Id == op.CashboxId || c.Id == advance.CashboxId).Select(c => c.CurrencyId).Distinct().CountAsync(ctx.RequestAborted);
                if (currencies != 1) return Results.Problem("Операция закрытия должна быть в валюте кассы аванса.", statusCode: 409);
                if (await db.Advances.AnyAsync(a => a.OperationId == op.Id || a.SettlementOperationId == op.Id, ctx.RequestAborted))
                    return Results.Problem("Эта операция уже связана с авансом.", statusCode: 409);
            }
            var remainder = AdvanceRules.Remainder(advance.Amount, advance.Spent ?? 0m);
            if (AdvanceRules.SettleError(advance.Status, remainder, advance.CounterpartyId, op is null ? null : (op.Status, op.Type, op.Amount, op.CounterpartyId, op.IsReversal, op.TransferId.HasValue)) is { } error)
                return Results.Problem(error, statusCode: 409);
            advance.Status = AdvanceRules.Closed; advance.SettlementOperationId = op?.Id; advance.ClosedBy = ctx.UserId(); advance.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ctx.RequestAborted);
            await audit.Add(ctx.UserId(), "advance_closed", "advance", id, new { remainder, operationId = op?.Id });
            await tx.CommitAsync(ctx.RequestAborted);
            var full = await db.Advances.AsNoTracking().Include(a => a.Counterparty).Include(a => a.Lines).ThenInclude(l => l.Category).SingleAsync(a => a.Id == id, ctx.RequestAborted);
            return Results.Ok(await Details(db, full, ctx.RequestAborted));
        });

        var reports = app.MapGroup("/api/reports").RequireAuthorization().WithTags("Advances");
        reports.MapGet("/advances", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, string? status, long? cashboxId, bool? overdue) =>
        {
            var (rows, totals) = await Report(db, ctx, access, status, cashboxId, overdue);
            return Results.Ok(new { total = rows.Count, totals, items = rows });
        });
        reports.MapGet("/advances.csv", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit, string? status, long? cashboxId, bool? overdue) =>
        {
            var (rows, totals) = await Report(db, ctx, access, status, cashboxId, overdue);
            var csv = new StringBuilder("Аванс;Сотрудник;Выдан;Срок отчёта;Статус;Просрочен;Сумма;Израсходовано;Остаток;Назначение\n");
            foreach (var r in rows) csv.AppendLine(string.Join(';', r.Id, ReportEndpoints.Cell(r.Counterparty), r.IssueDate.ToString("dd.MM.yyyy"), r.DueDate.ToString("dd.MM.yyyy"), StatusName(r.Status), r.Overdue ? "да" : "нет", Money2(r.Amount), r.Spent is null ? "" : Money2(r.Spent.Value), r.Remainder is null ? "" : Money2(r.Remainder.Value), ReportEndpoints.Cell(r.Purpose)));
            csv.AppendLine($"Итого;;;;;;{Money2(totals.Amount)};{Money2(totals.Spent)};{Money2(totals.Remainder)};");
            await audit.Add(ctx.UserId(), "report_exported", "report", null, new { report = "advances", format = "csv", rows = rows.Count });
            return Results.File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "advances.csv");
        });
        reports.MapGet("/advances.pdf", [Authorize] async (HttpContext ctx, CashDb db, ICashboxAccess access, Audit audit, IPdfReport pdf, string? status, long? cashboxId, bool? overdue) =>
        {
            var (rows, totals) = await Report(db, ctx, access, status, cashboxId, overdue);
            var org = await db.Organizations.AsNoTracking().SingleAsync(ctx.RequestAborted);
            var table = rows.Select(r => (IReadOnlyList<string>)[r.Id.ToString(CultureInfo.InvariantCulture), r.Counterparty, r.IssueDate.ToString("dd.MM.yyyy"), r.DueDate.ToString("dd.MM.yyyy") + (r.Overdue ? " (просрочен)" : ""), StatusName(r.Status), Money2(r.Amount), r.Spent is null ? "—" : Money2(r.Spent.Value), r.Remainder is null ? "—" : Money2(r.Remainder.Value)]).ToList();
            var bytes = pdf.GenerateTable(org.Name, "Авансы подотчётным лицам", $"Сформировано {Local.Today:dd.MM.yyyy}. Остаток < 0 — перерасход, подлежит возмещению.",
                ["№", "Сотрудник", "Выдан", "Срок отчёта", "Статус", "Сумма", "Израсходовано", "Остаток"], table,
                [$"Выдано: {Money2(totals.Amount)} {org.Currency}", $"Израсходовано по отчётам: {Money2(totals.Spent)} {org.Currency}", $"Остаток (к возврату/возмещению): {Money2(totals.Remainder)} {org.Currency}", "Сумма выданных авансов прописью: " + FaizaCash.Infrastructure.RussianMoneyWords.Format(totals.Amount, org.Currency)]);
            await audit.Add(ctx.UserId(), "report_exported", "report", null, new { report = "advances", format = "pdf", rows = rows.Count });
            return Results.File(bytes, "application/pdf", "advances.pdf");
        });
        return app;
    }

    internal sealed record AdvanceRow(long Id, string Counterparty, DateOnly IssueDate, DateOnly DueDate, string Status, bool Overdue, decimal Amount, decimal? Spent, decimal? Remainder, string Purpose);
    internal sealed record AdvanceTotals(decimal Amount, decimal Spent, decimal Remainder);

    private static string StatusName(string status) => status switch { AdvanceRules.Issued => "выдан", AdvanceRules.Reported => "отчёт принят", AdvanceRules.Closed => "закрыт", _ => status };

    private static async Task<IQueryable<Advance>> Scoped(CashDb db, HttpContext ctx, ICashboxAccess access, long? cashboxId)
    {
        if (cashboxId.HasValue) await access.Require(ctx.UserId(), cashboxId.Value, ctx.RequestAborted);
        var allowed = await access.AllowedCashboxes(ctx.UserId(), ctx.RequestAborted);
        return db.Advances.AsNoTracking().Include(x => x.Counterparty).Where(x => allowed.Contains(x.CashboxId) && (!cashboxId.HasValue || x.CashboxId == cashboxId));
    }

    private static async Task<(List<AdvanceRow> Rows, AdvanceTotals Totals)> Report(CashDb db, HttpContext ctx, ICashboxAccess access, string? status, long? cashboxId, bool? overdue)
    {
        if (status is not (null or AdvanceRules.Issued or AdvanceRules.Reported or AdvanceRules.Closed)) throw new BusinessException("Статус аванса: issued, reported или closed.", 400);
        var today = Local.Today;
        var query = (await Scoped(db, ctx, access, cashboxId)).Where(x => (status == null || x.Status == status) && (overdue != true || x.Status == AdvanceRules.Issued && x.DueDate < today));
        var rows = (await query.OrderBy(x => x.IssueDate).ThenBy(x => x.Id).Take(5000).ToListAsync(ctx.RequestAborted))
            .Select(x => new AdvanceRow(x.Id, x.Counterparty.Name, x.IssueDate, x.DueDate, x.Status, AdvanceRules.IsOverdue(x.Status, x.DueDate, today), x.Amount, x.Spent, x.Spent.HasValue ? AdvanceRules.Remainder(x.Amount, x.Spent.Value) : null, x.Purpose)).ToList();
        return (rows, new AdvanceTotals(rows.Sum(x => x.Amount), rows.Sum(x => x.Spent ?? 0m), rows.Where(x => x.Status != AdvanceRules.Closed).Sum(x => x.Remainder ?? x.Amount)));
    }

    /// <summary>Плоское представление аванса (camelCase-ключи), к которому детальная карточка добавляет lines и attachments.</summary>
    private static Dictionary<string, object?> Summary(Advance x, DateOnly today) => new()
    {
        ["id"] = x.Id, ["operationId"] = x.OperationId, ["cashboxId"] = x.CashboxId, ["counterpartyId"] = x.CounterpartyId, ["counterparty"] = x.Counterparty?.Name,
        ["amount"] = x.Amount, ["purpose"] = x.Purpose, ["issueDate"] = x.IssueDate, ["dueDate"] = x.DueDate, ["status"] = x.Status,
        ["spent"] = x.Spent, ["remainder"] = x.Spent.HasValue ? AdvanceRules.Remainder(x.Amount, x.Spent.Value) : null,
        ["overdue"] = AdvanceRules.IsOverdue(x.Status, x.DueDate, today), ["settlementOperationId"] = x.SettlementOperationId,
        ["createdBy"] = x.CreatedBy, ["createdAt"] = x.CreatedAt, ["reportedAt"] = x.ReportedAt, ["closedAt"] = x.ClosedAt
    };

    private static async Task<Dictionary<string, object?>> Details(CashDb db, Advance x, CancellationToken ct)
    {
        var view = Summary(x, Local.Today);
        view["lines"] = x.Lines.OrderBy(l => l.Date).ThenBy(l => l.Id).Select(l => new { l.Id, l.Date, l.Description, l.Amount, l.CategoryId, category = l.Category?.Name }).ToList();
        view["attachments"] = await AttachmentsEndpoints.List(db, "advance", x.Id, ct);
        return view;
    }
}
