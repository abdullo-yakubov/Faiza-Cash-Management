using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

/// <summary>Справочники: филиалы, кассы, статьи, контрагенты.</summary>
public static class DirectoryEndpoints
{
    public sealed record BranchRequest(string Name, string? Address, bool IsActive = true);
    public sealed record RegisterRequest(int BranchId, string Name, string Currency, bool IsActive = true);
    public sealed record CategoryRequest(string Name, CategoryKind Kind, bool IsActive = true);
    public sealed record CounterpartyRequest(string Name, CounterpartyKind Kind, string? Phone, string? Note, bool IsActive = true);

    public sealed record RegisterDto(int Id, int BranchId, string BranchName, string Name, string Currency, decimal Balance,
        bool IsActive, bool HasOpenShift);

    public static void MapDirectories(this RouteGroupBuilder api)
    {
        // ---------- Филиалы ----------
        api.MapGet("/branches", async (AppDbContext db, CancellationToken ct) =>
            await db.Branches.OrderBy(b => b.Name).ToListAsync(ct));

        api.MapPost("/branches", async (BranchRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var branch = new Branch { Name = Check.Text(req.Name, "Название", 150), Address = Check.Optional(req.Address, "Адрес", 250) };
            db.Branches.Add(branch);
            audit.Add("branch_created", "branch", branch.Name);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/branches/{branch.Id}", branch);
        }).RequireAuthorization(Policies.Admin);

        api.MapPut("/branches/{id:int}", async (int id, BranchRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var branch = await db.Branches.FindAsync([id], ct) ?? throw new NotFoundException("Филиал не найден.");
            branch.Name = Check.Text(req.Name, "Название", 150);
            branch.Address = Check.Optional(req.Address, "Адрес", 250);
            branch.IsActive = req.IsActive;
            audit.Add("branch_updated", "branch", id, req);
            await db.SaveChangesAsync(ct);
            return Results.Ok(branch);
        }).RequireAuthorization(Policies.Admin);

        // ---------- Кассы ----------
        api.MapGet("/registers", async (AppDbContext db, CurrentUser me, CancellationToken ct) =>
        {
            var allowed = await me.AllowedRegisterIdsAsync(ct);
            var query = db.CashRegisters.AsQueryable();
            if (allowed is not null) query = query.Where(r => allowed.Contains(r.Id));
            return await query.OrderBy(r => r.BranchId).ThenBy(r => r.Id)
                .Select(r => new RegisterDto(r.Id, r.BranchId, r.Branch!.Name, r.Name, r.Currency, r.Balance, r.IsActive,
                    db.Shifts.Any(s => s.CashRegisterId == r.Id && s.ClosedAt == null)))
                .ToListAsync(ct);
        });

        api.MapPost("/registers", async (RegisterRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            await EnsureBranchAsync(db, req.BranchId, ct);
            var register = new CashRegister
            {
                BranchId = req.BranchId,
                Name = Check.Text(req.Name, "Название", 100),
                Currency = CheckCurrency(req.Currency),
            };
            db.CashRegisters.Add(register);
            audit.Add("register_created", "register", register.Name, new { register.Currency });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/registers/{register.Id}", new { register.Id });
        }).RequireAuthorization(Policies.Admin);

        api.MapPut("/registers/{id:int}", async (int id, RegisterRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var register = await db.CashRegisters.FindAsync([id], ct) ?? throw new NotFoundException("Касса не найдена.");
            await EnsureBranchAsync(db, req.BranchId, ct);
            var currency = CheckCurrency(req.Currency);
            if (currency != register.Currency && await db.Operations.AnyAsync(o => o.CashRegisterId == id, ct))
                throw new ValidationException("Валюту нельзя менять после первой операции.");
            if (!req.IsActive && await db.Shifts.AnyAsync(s => s.CashRegisterId == id && s.ClosedAt == null, ct))
                throw new ValidationException("Сначала закройте смену в этой кассе.");
            register.BranchId = req.BranchId;
            register.Name = Check.Text(req.Name, "Название", 100);
            register.Currency = currency;
            register.IsActive = req.IsActive;
            audit.Add("register_updated", "register", id, req);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);

        // ---------- Статьи ----------
        api.MapGet("/categories", async (AppDbContext db, CancellationToken ct) =>
            await db.Categories.OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(ct));

        api.MapPost("/categories", async (CategoryRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var c = new Category { Name = Check.Text(req.Name, "Название", 100), Kind = Check.Enum(req.Kind, "Вид") };
            db.Categories.Add(c);
            audit.Add("category_created", "category", c.Name, new { c.Kind });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/categories/{c.Id}", c);
        }).RequireAuthorization(Policies.ManageDirectories);

        api.MapPut("/categories/{id:int}", async (int id, CategoryRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var c = await db.Categories.FindAsync([id], ct) ?? throw new NotFoundException("Статья не найдена.");
            var kind = Check.Enum(req.Kind, "Вид");
            if (kind != c.Kind && await db.Operations.AnyAsync(o => o.CategoryId == id, ct))
                throw new ValidationException("Вид статьи нельзя менять, если по ней уже есть операции.");
            c.Name = Check.Text(req.Name, "Название", 100);
            c.Kind = kind;
            c.IsActive = req.IsActive;
            audit.Add("category_updated", "category", id, req);
            await db.SaveChangesAsync(ct);
            return Results.Ok(c);
        }).RequireAuthorization(Policies.ManageDirectories);

        // ---------- Контрагенты ----------
        api.MapGet("/counterparties", async (AppDbContext db, CancellationToken ct) =>
            await db.Counterparties.OrderBy(c => c.Name).ToListAsync(ct));

        api.MapPost("/counterparties", async (CounterpartyRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var c = new Counterparty
            {
                Name = Check.Text(req.Name, "Название", 150),
                Kind = Check.Enum(req.Kind, "Вид"),
                Phone = Check.Optional(req.Phone, "Телефон", 30),
                Note = Check.Optional(req.Note, "Примечание", 500),
            };
            db.Counterparties.Add(c);
            audit.Add("counterparty_created", "counterparty", c.Name);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/counterparties/{c.Id}", c);
        }).RequireAuthorization(Policies.ManageDirectories);

        api.MapPut("/counterparties/{id:int}", async (int id, CounterpartyRequest req, AppDbContext db, AuditLog audit, CancellationToken ct) =>
        {
            var c = await db.Counterparties.FindAsync([id], ct) ?? throw new NotFoundException("Контрагент не найден.");
            c.Name = Check.Text(req.Name, "Название", 150);
            c.Kind = Check.Enum(req.Kind, "Вид");
            c.Phone = Check.Optional(req.Phone, "Телефон", 30);
            c.Note = Check.Optional(req.Note, "Примечание", 500);
            c.IsActive = req.IsActive;
            audit.Add("counterparty_updated", "counterparty", id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(c);
        }).RequireAuthorization(Policies.ManageDirectories);
    }

    private static string CheckCurrency(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return Currencies.IsSupported(c) ? c : throw new ValidationException("Неподдерживаемая валюта.");
    }

    private static async Task EnsureBranchAsync(AppDbContext db, int branchId, CancellationToken ct)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
            throw new ValidationException("Филиал не найден.");
    }
}
