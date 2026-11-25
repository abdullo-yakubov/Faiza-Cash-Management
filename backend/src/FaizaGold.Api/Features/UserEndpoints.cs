using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class UserEndpoints
{
    public sealed record UserDto(int Id, string Login, string FullName, UserRole Role, bool IsActive, bool MustChangePassword, int[] RegisterIds);
    public sealed record CreateUserRequest(string Login, string FullName, UserRole Role, string Password, int[]? RegisterIds);
    public sealed record UpdateUserRequest(string FullName, UserRole Role, bool IsActive, int[]? RegisterIds);
    public sealed record ResetPasswordRequest(string Password);

    public static void MapUsers(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/users").RequireAuthorization(Policies.Admin);

        g.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            (await db.Users.Include(u => u.RegisterAccess).OrderBy(u => u.Login).ToListAsync(ct)).Select(ToDto));

        g.MapPost("/", async (CreateUserRequest req, AppDbContext db, PasswordService passwords, AuditLog audit,
            TimeProvider clock, CancellationToken ct) =>
        {
            var login = Check.Text(req.Login, "Логин", 50).ToLowerInvariant();
            if (!login.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new ValidationException("Логин может содержать только латинские буквы, цифры и знаки . _ -");
            var user = new User
            {
                Login = login,
                FullName = Check.Text(req.FullName, "ФИО", 150),
                Role = Check.Enum(req.Role, "Роль"),
                CreatedAt = clock.GetUtcNow(),
                MustChangePassword = true,
            };
            user.PasswordHash = passwords.Hash(user, Check.Password(req.Password));
            await SetAccessAsync(db, user, req.RegisterIds, ct);
            db.Users.Add(user);
            audit.Add("user_created", "user", login, new { user.Role });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}", ToDto(user));
        });

        g.MapPut("/{id:int}", async (int id, UpdateUserRequest req, AppDbContext db, CurrentUser me, AuditLog audit,
            CancellationToken ct) =>
        {
            var user = await db.Users.Include(u => u.RegisterAccess).SingleOrDefaultAsync(u => u.Id == id, ct)
                       ?? throw new NotFoundException("Пользователь не найден.");
            var role = Check.Enum(req.Role, "Роль");
            if (user.Id == me.Id && (role != UserRole.Admin || !req.IsActive))
                throw new ValidationException("Нельзя снять с себя роль администратора или заблокировать себя.");
            user.FullName = Check.Text(req.FullName, "ФИО", 150);
            user.Role = role;
            user.IsActive = req.IsActive;
            await SetAccessAsync(db, user, req.RegisterIds, ct);
            audit.Add("user_updated", "user", user.Id, new { user.Role, user.IsActive, registers = req.RegisterIds });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(user));
        });

        g.MapPost("/{id:int}/reset-password", async (int id, ResetPasswordRequest req, AppDbContext db,
            PasswordService passwords, AuditLog audit, CancellationToken ct) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Пользователь не найден.");
            user.PasswordHash = passwords.Hash(user, Check.Password(req.Password));
            user.MustChangePassword = true;
            audit.Add("password_reset", "user", user.Id);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task SetAccessAsync(AppDbContext db, User user, int[]? registerIds, CancellationToken ct)
    {
        var ids = (registerIds ?? []).Distinct().ToArray();
        if (user.Role != UserRole.Cashier) ids = [];
        var existing = await db.CashRegisters.Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (existing.Count != ids.Length) throw new ValidationException("Одна из выбранных касс не найдена.");
        user.RegisterAccess.RemoveAll(a => !ids.Contains(a.CashRegisterId));
        foreach (var rid in ids.Where(rid => user.RegisterAccess.All(a => a.CashRegisterId != rid)))
            user.RegisterAccess.Add(new RegisterAccess { CashRegisterId = rid });
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Login, u.FullName, u.Role, u.IsActive, u.MustChangePassword,
        u.RegisterAccess.Select(a => a.CashRegisterId).Order().ToArray());
}
