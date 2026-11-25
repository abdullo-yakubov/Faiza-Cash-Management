using System.Security.Claims;
using FaizaGold.Api.Data;
using FaizaGold.Api.Infrastructure;
using FaizaGold.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Login, string Password);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public sealed record MeResponse(int Id, string Login, string FullName, UserRole Role, bool MustChangePassword, int[] RegisterIds);

    private const string StampClaim = "faiza:stamp";

    public static void MapAuth(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, PasswordService passwords, AuditLog audit,
            HttpContext http, CancellationToken ct) =>
        {
            var login = (req.Login ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.RegisterAccess).SingleOrDefaultAsync(u => u.Login == login, ct);
            if (user is null || !user.IsActive || !passwords.Verify(user, req.Password ?? ""))
            {
                audit.Add("login_failed", "user", null, new { login }, userId: null, login: login.Length > 50 ? login[..50] : login);
                await db.SaveChangesAsync(ct);
                return Results.Json(new { title = "Неверный логин или пароль.", status = 401 }, statusCode: 401);
            }

            await SignInAsync(http, user);
            audit.Add("login", "user", user.Id, userId: user.Id, login: user.Login);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToMe(user));
        }).AllowAnonymous().RequireRateLimiting("login");

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        // Без входа отвечает 204, чтобы интерфейс при загрузке не получал ошибку 401.
        g.MapGet("/me", async (HttpContext http, CurrentUser me, AppDbContext db, CancellationToken ct) =>
        {
            if (http.User.Identity?.IsAuthenticated != true) return Results.NoContent();
            var user = await db.Users.Include(u => u.RegisterAccess).SingleAsync(u => u.Id == me.Id, ct);
            return Results.Ok(ToMe(user));
        }).AllowAnonymous();

        g.MapPost("/change-password", async (ChangePasswordRequest req, CurrentUser me, AppDbContext db,
            PasswordService passwords, AuditLog audit, HttpContext http, CancellationToken ct) =>
        {
            var user = await db.Users.Include(u => u.RegisterAccess).SingleAsync(u => u.Id == me.Id, ct);
            if (!passwords.Verify(user, req.CurrentPassword ?? ""))
                throw new ValidationException("Текущий пароль указан неверно.");
            var newPassword = Check.Password(req.NewPassword);
            if (newPassword == req.CurrentPassword)
                throw new ValidationException("Новый пароль должен отличаться от текущего.");
            user.PasswordHash = passwords.Hash(user, newPassword);
            user.MustChangePassword = false;
            audit.Add("password_changed", "user", user.Id);
            await db.SaveChangesAsync(ct);
            await SignInAsync(http, user); // новая метка пароля в cookie
            return Results.Ok(ToMe(user));
        });
    }

    private static MeResponse ToMe(User u) =>
        new(u.Id, u.Login, u.FullName, u.Role, u.MustChangePassword, u.RegisterAccess.Select(a => a.CashRegisterId).Order().ToArray());

    private static Task SignInAsync(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(StampClaim, Stamp(user)),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Метка меняется при смене пароля или роли, после чего старые cookie перестают действовать.</summary>
    private static string Stamp(User user) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{user.PasswordHash}|{user.Role}")))[..16];

    /// <summary>При каждом запросе проверяет, что пользователь существует, активен и не сменил пароль или роль.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = ctx.Principal?.FindFirstValue(StampClaim);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = int.TryParse(id, out var userId) ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) : null;
        if (user is null || !user.IsActive || Stamp(user) != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
