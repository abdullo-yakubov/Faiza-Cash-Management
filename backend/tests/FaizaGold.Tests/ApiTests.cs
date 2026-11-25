using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace FaizaGold.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Номера учебных данных: кассы 1 (TJS, кассир), 2 (USD, кассир), 3 (TJS, филиал). Статьи 1 — выручка, 4 — оплата поставщику.
    private const int MainTjs = 1, MainUsd = 2, BranchTjs = 3;

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static async Task<int> CategoryAsync(HttpClient c, string kind)
    {
        var all = await Json(await c.GetAsync("/api/categories"));
        return all.EnumerateArray().First(x => x.GetProperty("kind").GetString() == kind).GetProperty("id").GetInt32();
    }

    private static async Task EnsureShiftAsync(HttpClient c, int registerId)
    {
        var res = await c.GetAsync($"/api/registers/{registerId}/shift");
        if (res.StatusCode == HttpStatusCode.NoContent)
            await Json(await c.PostAsync($"/api/registers/{registerId}/shift/open", null));
    }

    private static Task<HttpResponseMessage> PostOperation(HttpClient c, int register, string kind, decimal amount, int category) =>
        c.PostAsJsonAsync("/api/operations", new { cashRegisterId = register, kind, amount, categoryId = category });

    [PostgresFact]
    public async Task Anonymous_requests_are_rejected()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/registers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [PostgresFact]
    public async Task Wrong_password_is_rejected_and_logged()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { login = "cashier", password = "wrong-pass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var admin = await factory.LoginAsync("admin", ApiFactory.AdminPassword);
        var audit = await Json(await admin.GetAsync("/api/audit?action=login_failed"));
        Assert.Contains(audit.EnumerateArray(), a => a.GetProperty("userLogin").GetString() == "cashier");
    }

    [PostgresFact]
    public async Task Defense_scenario_income_expense_reversal_and_cash_book()
    {
        var c = await factory.LoginAsync("accountant");
        await EnsureShiftAsync(c, BranchTjs);
        var income = await CategoryAsync(c, "Income");
        var expense = await CategoryAsync(c, "Expense");
        var before = (await Json(await c.GetAsync("/api/registers"))).EnumerateArray()
            .First(r => r.GetProperty("id").GetInt32() == BranchTjs).GetProperty("balance").GetDecimal();

        await Json(await PostOperation(c, BranchTjs, "Income", 1000m, income));
        var exp = await Json(await PostOperation(c, BranchTjs, "Expense", 250m, expense));
        Assert.Equal(before + 750m, exp.GetProperty("balanceAfter").GetDecimal());

        var rev = await Json(await c.PostAsJsonAsync($"/api/operations/{exp.GetProperty("id").GetInt64()}/reverse", new { reason = "ошибка" }));
        Assert.StartsWith("СТР-", rev.GetProperty("number").GetString());
        Assert.Equal(before + 1000m, rev.GetProperty("balanceAfter").GetDecimal());

        var again = await c.PostAsJsonAsync($"/api/operations/{exp.GetProperty("id").GetInt64()}/reverse", new { reason = "ещё" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var today = (await Json(await c.GetAsync("/api/dashboard"))).GetProperty("date").GetString();
        var book = await Json(await c.GetAsync($"/api/reports/cashbook?registerId={BranchTjs}&from={today}"));
        Assert.Equal(book.GetProperty("openingBalance").GetDecimal() + book.GetProperty("income").GetDecimal()
                     - book.GetProperty("expense").GetDecimal(), book.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(before + 1000m, book.GetProperty("closingBalance").GetDecimal());

        var csv = await c.GetAsync($"/api/reports/cashbook.csv?registerId={BranchTjs}&from={today}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Кассовая книга", await csv.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Overdraft_is_rejected()
    {
        var c = await factory.LoginAsync("accountant");
        await EnsureShiftAsync(c, MainUsd);
        var res = await PostOperation(c, MainUsd, "Expense", 1_000_000m, await CategoryAsync(c, "Expense"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Недостаточно денег", await res.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Operation_requires_open_shift()
    {
        var c = await factory.LoginAsync("accountant");
        await EnsureShiftAsync(c, MainUsd);
        await Json(await c.PostAsJsonAsync($"/api/registers/{MainUsd}/shift/close", new { countedAmount = (decimal?)null }));
        var res = await PostOperation(c, MainUsd, "Income", 10m, await CategoryAsync(c, "Income"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("смены", await res.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Cashier_cannot_touch_foreign_register_and_manager_is_read_only()
    {
        var cashier = await factory.LoginAsync("cashier");
        var registers = await Json(await cashier.GetAsync("/api/registers"));
        Assert.DoesNotContain(registers.EnumerateArray(), r => r.GetProperty("id").GetInt32() == BranchTjs);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync($"/api/registers/{BranchTjs}/shift/open", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/reports/cashbook?registerId={BranchTjs}&from=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/users")).StatusCode);

        var manager = await factory.LoginAsync("manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/registers/{MainTjs}/shift/open", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostOperation(manager, MainTjs, "Income", 1m, 1)).StatusCode);
    }

    [PostgresFact]
    public async Task Transfer_moves_money_between_registers_of_same_currency_only()
    {
        var c = await factory.LoginAsync("accountant");
        await EnsureShiftAsync(c, MainTjs);
        await EnsureShiftAsync(c, BranchTjs);
        await EnsureShiftAsync(c, MainUsd);
        await Json(await PostOperation(c, MainTjs, "Income", 300m, await CategoryAsync(c, "Income")));

        async Task<decimal> Balance(int id) => (await Json(await c.GetAsync("/api/registers"))).EnumerateArray()
            .First(r => r.GetProperty("id").GetInt32() == id).GetProperty("balance").GetDecimal();
        var (a, b) = (await Balance(MainTjs), await Balance(BranchTjs));

        await Json(await c.PostAsJsonAsync("/api/transfers", new { fromRegisterId = MainTjs, toRegisterId = BranchTjs, amount = 120m }));
        Assert.Equal(a - 120m, await Balance(MainTjs));
        Assert.Equal(b + 120m, await Balance(BranchTjs));

        var mixed = await c.PostAsJsonAsync("/api/transfers", new { fromRegisterId = MainTjs, toRegisterId = MainUsd, amount = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
    }

    [PostgresFact]
    public async Task Parallel_expenses_never_overdraw_and_balance_matches_ledger()
    {
        var c = await factory.LoginAsync("accountant");
        await EnsureShiftAsync(c, MainTjs);
        var income = await CategoryAsync(c, "Income");
        var expense = await CategoryAsync(c, "Expense");
        var start = (await Json(await c.GetAsync("/api/registers"))).EnumerateArray()
            .First(r => r.GetProperty("id").GetInt32() == MainTjs).GetProperty("balance").GetDecimal();
        await Json(await PostOperation(c, MainTjs, "Income", 1000m - start % 1000m + 1000m, income)); // остаток кратен 1000

        var balance = (await Json(await c.GetAsync("/api/registers"))).EnumerateArray()
            .First(r => r.GetProperty("id").GetInt32() == MainTjs).GetProperty("balance").GetDecimal();
        var attempts = (int)(balance / 100m) + 10; // на 10 попыток больше, чем хватает денег
        var results = await Task.WhenAll(Enumerable.Range(0, attempts).Select(_ => PostOperation(c, MainTjs, "Expense", 100m, expense)));

        Assert.Equal((int)(balance / 100m), results.Count(r => r.IsSuccessStatusCode));
        await using var conn = new NpgsqlConnection(factory.DbConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM \"CashRegisters\" r WHERE r.\"Balance\" <> COALESCE((SELECT SUM(\"Delta\") FROM \"Operations\" o WHERE o.\"CashRegisterId\" = r.\"Id\"), 0) OR r.\"Balance\" < 0", conn);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task Admin_creates_user_who_must_change_password_and_deactivation_ends_session()
    {
        var admin = await factory.LoginAsync("admin", ApiFactory.AdminPassword);
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            login = "Kassir2", fullName = "Новый кассир", role = "Cashier", password = "Start1234", registerIds = new[] { MainTjs },
        });
        var user = await Json(created);
        Assert.Equal("kassir2", user.GetProperty("login").GetString());
        Assert.True(user.GetProperty("mustChangePassword").GetBoolean());

        var kassir = await factory.LoginAsync("kassir2", "Start1234");
        await Json(await kassir.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Start1234", newPassword = "Better5678" }));
        Assert.Equal(HttpStatusCode.OK, (await kassir.GetAsync("/api/auth/me")).StatusCode);

        var id = user.GetProperty("id").GetInt32();
        await Json(await admin.PutAsJsonAsync($"/api/users/{id}", new { fullName = "Новый кассир", role = "Cashier", isActive = false, registerIds = new[] { MainTjs } }));
        Assert.Equal(HttpStatusCode.NoContent, (await kassir.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kassir.GetAsync("/api/registers")).StatusCode);
    }

    [PostgresFact]
    public async Task Weak_password_and_duplicate_login_are_rejected()
    {
        var admin = await factory.LoginAsync("admin", ApiFactory.AdminPassword);
        var weak = await admin.PostAsJsonAsync("/api/users", new { login = "weak", fullName = "Тест", role = "Manager", password = "123" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var dup = await admin.PostAsJsonAsync("/api/users", new { login = "cashier", fullName = "Тест", role = "Manager", password = "Start1234" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }
}
