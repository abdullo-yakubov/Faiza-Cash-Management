using FaizaCash.Domain;
using FaizaCash.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static FaizaCash.Api.RoleNames;

namespace FaizaCash.Api;

/// <summary>
/// Точка сборки API: каждый функциональный модуль (vertical slice) лежит в Features/&lt;Модуль&gt;
/// и регистрирует свои маршруты сам. Здесь — только порядок подключения, /health и первичный сид.
/// </summary>
public static class ApiEndpoints
{
    public static void MapCashApi(this WebApplication app, IConfiguration configuration)
    {
        app.MapReportsEndpoints();
        app.MapAuditEndpoints();
        app.MapCatalogWriteEndpoints();
        app.MapBackupsEndpoints();
        app.MapAuthEndpoints();
        app.MapUsersEndpoints();
        app.MapOrganizationEndpoints();
        app.MapShiftsEndpoints();
        app.MapTransfersEndpoints();
        app.MapCatalogsEndpoints();
        app.MapSettlementsEndpoints();
        app.MapRepresentativesEndpoints();
        app.MapOperationsEndpoints();
        app.MapAnalyticsEndpoints();
        app.MapApprovalsEndpoints();
        app.MapAttachmentsEndpoints();
        app.MapAdvancesEndpoints();
        app.MapInventoryEndpoints();
        app.MapGet("/health", async (CashDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok", utc = DateTime.UtcNow }) : Results.Problem("База данных недоступна.", statusCode: 503));
    }

    public static async Task Seed(CashDb db, IConfiguration cfg, ILogger log)
    {
        if (!await db.Organizations.AnyAsync()) { db.Organizations.Add(new()); await db.SaveChangesAsync(); }
        if (!await db.Roles.AnyAsync()) { db.Roles.AddRange(new Role { Code = Admin, Name = "Администратор" }, new Role { Code = Accountant, Name = "Бухгалтер" }, new Role { Code = Manager, Name = "Руководитель" }, new Role { Code = Auditor, Name = "Аудитор" }, new Role { Code = Cashier, Name = "Кассир" }); db.Currencies.Add(new Currency { Code = "TJS", Name = "Таджикский сомони" }); await db.SaveChangesAsync(); }
        if (!await db.Users.AnyAsync())
        {
            var pass = cfg["Seed:AdminPassword"];
            if (Password.Valid(pass)) { var u = new User { Login = cfg["Seed:AdminLogin"] ?? "admin", FullName = "Системный администратор", PasswordHash = Password.Hash(pass!), MustChangePassword = true }; u.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(x => x.Code == Admin) }); db.Users.Add(u); await db.SaveChangesAsync(); }
            else log.LogWarning("Первый администратор не создан. Задайте Seed__AdminPassword.");
        }
    }
}
