using System.Globalization;
using System.Text;
using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class ReportEndpoints
{
    public sealed record DashboardRegister(int Id, string Branch, string Name, string Currency, decimal Balance, bool HasOpenShift,
        decimal TodayIncome, decimal TodayExpense, int TodayCount);

    public sealed record CashBook(int RegisterId, string Register, string Branch, string Currency, DateOnly From, DateOnly To,
        decimal OpeningBalance, decimal Income, decimal Expense, decimal ClosingBalance, List<OperationEndpoints.OperationDto> Operations);

    public sealed record SummaryRow(string Category, OperationKind Kind, string Currency, decimal Total, int Count);

    public static void MapReports(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (AppDbContext db, CurrentUser me, BusinessCalendar calendar, TimeProvider clock,
            CancellationToken ct) =>
        {
            var allowed = await me.AllowedRegisterIdsAsync(ct);
            var start = calendar.StartOfDay(calendar.Today(clock));
            var q = db.CashRegisters.Where(r => r.IsActive);
            if (allowed is not null) q = q.Where(r => allowed.Contains(r.Id));
            var registers = await q.OrderBy(r => r.BranchId).ThenBy(r => r.Id)
                .Select(r => new DashboardRegister(r.Id, r.Branch!.Name, r.Name, r.Currency, r.Balance,
                    db.Shifts.Any(s => s.CashRegisterId == r.Id && s.ClosedAt == null),
                    db.Operations.Where(o => o.CashRegisterId == r.Id && o.CreatedAt >= start && o.Delta > 0).Sum(o => (decimal?)o.Delta) ?? 0,
                    db.Operations.Where(o => o.CashRegisterId == r.Id && o.CreatedAt >= start && o.Delta < 0).Sum(o => (decimal?)-o.Delta) ?? 0,
                    db.Operations.Count(o => o.CashRegisterId == r.Id && o.CreatedAt >= start)))
                .ToListAsync(ct);
            var totals = registers.GroupBy(r => r.Currency)
                .Select(g => new { currency = g.Key, balance = g.Sum(r => r.Balance) }).ToList();
            return Results.Ok(new { date = calendar.Today(clock), registers, totals });
        });

        api.MapGet("/reports/cashbook", async (int registerId, DateOnly from, DateOnly? to, AppDbContext db, CurrentUser me,
            BusinessCalendar calendar, CancellationToken ct) =>
            Results.Ok(await BuildCashBookAsync(db, me, calendar, registerId, from, to ?? from, ct)));

        api.MapGet("/reports/cashbook.csv", async (int registerId, DateOnly from, DateOnly? to, AppDbContext db, CurrentUser me,
            BusinessCalendar calendar, CancellationToken ct) =>
        {
            var book = await BuildCashBookAsync(db, me, calendar, registerId, from, to ?? from, ct);
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.AppendLine($"Кассовая книга;{Csv(book.Branch)} / {Csv(book.Register)};{book.From:dd.MM.yyyy}–{book.To:dd.MM.yyyy};{book.Currency}");
            sb.AppendLine($"Остаток на начало;{book.OpeningBalance.ToString("0.00", inv)}");
            sb.AppendLine("Дата и время;Номер;Вид;Статья;Контрагент;Основание;Приход;Расход;Остаток;Кассир");
            foreach (var o in book.Operations)
            {
                sb.AppendLine(string.Join(';',
                    calendar.ToLocal(o.CreatedAt).ToString("dd.MM.yyyy HH:mm", inv), o.Number, KindName(o.Kind), Csv(o.Category),
                    Csv(o.Counterparty), Csv(o.Description),
                    o.Delta > 0 ? o.Delta.ToString("0.00", inv) : "", o.Delta < 0 ? (-o.Delta).ToString("0.00", inv) : "",
                    o.BalanceAfter.ToString("0.00", inv), Csv(o.CreatedBy)));
            }
            sb.AppendLine($"Итого приход;{book.Income.ToString("0.00", inv)}");
            sb.AppendLine($"Итого расход;{book.Expense.ToString("0.00", inv)}");
            sb.AppendLine($"Остаток на конец;{book.ClosingBalance.ToString("0.00", inv)}");
            // BOM нужен, чтобы Excel правильно открыл кириллицу.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", $"cashbook-{registerId}-{book.From:yyyyMMdd}-{book.To:yyyyMMdd}.csv");
        });

        api.MapGet("/reports/summary", async (DateOnly from, DateOnly to, int? registerId, AppDbContext db, CurrentUser me,
            BusinessCalendar calendar, CancellationToken ct) =>
        {
            if (to < from) throw new ValidationException("Дата окончания раньше даты начала.");
            var allowed = await me.AllowedRegisterIdsAsync(ct);
            if (registerId is { } rid) await ShiftEndpoints.EnsureCanViewAsync(me, rid, ct);
            var start = calendar.StartOfDay(from);
            var end = calendar.StartOfDay(to.AddDays(1));
            var q = db.Operations.Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.CategoryId != null);
            if (registerId is not null) q = q.Where(o => o.CashRegisterId == registerId);
            else if (allowed is not null) q = q.Where(o => allowed.Contains(o.CashRegisterId));

            // Сторно уменьшает итог по статье исходной операции.
            var raw = await q.Select(o => new
                {
                    Category = o.Category!.Name,
                    CategoryKind = o.Category.Kind,
                    Currency = db.CashRegisters.Where(r => r.Id == o.CashRegisterId).Select(r => r.Currency).Single(),
                    o.Delta,
                    o.Kind,
                })
                .ToListAsync(ct);
            var rows = raw.GroupBy(x => new { x.Category, x.CategoryKind, x.Currency })
                .Select(g => new SummaryRow(g.Key.Category,
                    g.Key.CategoryKind == CategoryKind.Income ? OperationKind.Income : OperationKind.Expense,
                    g.Key.Currency,
                    Math.Abs(g.Sum(x => x.Delta)),
                    g.Count(x => x.Kind != OperationKind.Reversal) - g.Count(x => x.Kind == OperationKind.Reversal)))
                .Where(r => r.Count > 0 || r.Total != 0)
                .OrderBy(r => r.Currency).ThenBy(r => r.Kind).ThenByDescending(r => r.Total)
                .ToList();
            return Results.Ok(rows);
        });
    }

    private static async Task<CashBook> BuildCashBookAsync(AppDbContext db, CurrentUser me, BusinessCalendar calendar,
        int registerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from) throw new ValidationException("Дата окончания раньше даты начала.");
        if (to.DayNumber - from.DayNumber > 366) throw new ValidationException("Период не может быть больше года.");
        await ShiftEndpoints.EnsureCanViewAsync(me, registerId, ct);
        var register = await db.CashRegisters.AsNoTracking().Include(r => r.Branch).SingleOrDefaultAsync(r => r.Id == registerId, ct)
                       ?? throw new NotFoundException("Касса не найдена.");
        var start = calendar.StartOfDay(from);
        var end = calendar.StartOfDay(to.AddDays(1));

        // Кассовая книга никогда не переписывается, поэтому остаток на любую дату = сумма движений до неё.
        var opening = await db.Operations.Where(o => o.CashRegisterId == registerId && o.CreatedAt < start)
            .SumAsync(o => (decimal?)o.Delta, ct) ?? 0;
        var ops = await OperationEndpoints.Project(db.Operations
            .Where(o => o.CashRegisterId == registerId && o.CreatedAt >= start && o.CreatedAt < end)
            .OrderBy(o => o.Id)).ToListAsync(ct);
        var income = ops.Where(o => o.Delta > 0).Sum(o => o.Delta);
        var expense = ops.Where(o => o.Delta < 0).Sum(o => -o.Delta);
        return new CashBook(register.Id, register.Name, register.Branch!.Name, register.Currency, from, to, opening, income, expense,
            opening + income - expense, ops);
    }

    private static string KindName(OperationKind kind) => kind switch
    {
        OperationKind.Income => "Приход",
        OperationKind.Expense => "Расход",
        OperationKind.TransferIn => "Перевод (получено)",
        OperationKind.TransferOut => "Перевод (отправлено)",
        OperationKind.Reversal => "Сторно",
        _ => kind.ToString(),
    };

    /// <summary>Экранирует значение для CSV и не даёт Excel выполнить формулу из текста.</summary>
    internal static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value;
        if (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
