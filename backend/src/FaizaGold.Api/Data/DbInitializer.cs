using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Data;

/// <summary>Применяет миграции и создаёт первого администратора и, по желанию, учебные данные.</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var login = config["Seed:AdminLogin"] ?? "admin";
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Задайте пароль первого администратора: Seed:AdminPassword.");
            var admin = new User
            {
                Login = login, FullName = "Администратор", Role = UserRole.Admin,
                CreatedAt = clock.GetUtcNow(), MustChangePassword = true,
            };
            admin.PasswordHash = passwords.Hash(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            log.LogInformation("Created first administrator {Login}", login);
        }

        if (config.GetValue("Seed:Demo", false) && !await db.Branches.AnyAsync())
            await SeedDemoAsync(db, passwords, clock, config["Seed:DemoPassword"] ?? "Demo12345");
    }

    /// <summary>Учебные данные для демонстрации: филиал, две кассы, статьи, контрагенты, сотрудники.</summary>
    private static async Task SeedDemoAsync(AppDbContext db, PasswordService passwords, TimeProvider clock, string password)
    {
        var main = new Branch { Name = "Головной офис", Address = "г. Душанбе" };
        var second = new Branch { Name = "Филиал Худжанд", Address = "г. Худжанд" };
        db.Branches.AddRange(main, second);
        await db.SaveChangesAsync();

        var r1 = new CashRegister { BranchId = main.Id, Name = "Основная касса", Currency = "TJS" };
        var r2 = new CashRegister { BranchId = main.Id, Name = "Валютная касса", Currency = "USD" };
        var r3 = new CashRegister { BranchId = second.Id, Name = "Касса филиала", Currency = "TJS" };
        db.CashRegisters.AddRange(r1, r2, r3);

        db.Categories.AddRange(
            new Category { Name = "Выручка от продаж", Kind = CategoryKind.Income },
            new Category { Name = "Возврат подотчётных сумм", Kind = CategoryKind.Income },
            new Category { Name = "Прочие поступления", Kind = CategoryKind.Income },
            new Category { Name = "Оплата поставщику", Kind = CategoryKind.Expense },
            new Category { Name = "Выдача под отчёт", Kind = CategoryKind.Expense },
            new Category { Name = "Заработная плата", Kind = CategoryKind.Expense },
            new Category { Name = "Хозяйственные расходы", Kind = CategoryKind.Expense });

        db.Counterparties.AddRange(
            new Counterparty { Name = "ООО «Сомон Трейд»", Kind = CounterpartyKind.Supplier },
            new Counterparty { Name = "ИП Каримов", Kind = CounterpartyKind.Customer },
            new Counterparty { Name = "Рахимов А. (сотрудник)", Kind = CounterpartyKind.Employee });
        await db.SaveChangesAsync();

        User Make(string login, string name, UserRole role)
        {
            var u = new User { Login = login, FullName = name, Role = role, CreatedAt = clock.GetUtcNow() };
            u.PasswordHash = passwords.Hash(u, password);
            return u;
        }

        var cashier = Make("cashier", "Кассир Назарова М.", UserRole.Cashier);
        cashier.RegisterAccess.Add(new RegisterAccess { CashRegisterId = r1.Id });
        cashier.RegisterAccess.Add(new RegisterAccess { CashRegisterId = r2.Id });
        db.Users.AddRange(cashier,
            Make("accountant", "Бухгалтер Саидова Н.", UserRole.Accountant),
            Make("manager", "Руководитель Юсупов Ф.", UserRole.Manager));
        await db.SaveChangesAsync();
    }
}
