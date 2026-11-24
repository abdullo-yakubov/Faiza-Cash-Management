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

/// <summary>Модуль «Auth»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();
        var auth = app.MapGroup("/api/auth").WithTags("Auth");
auth.MapPost("/login", async (LoginDto input, HttpContext ctx, CashDb db, Audit audit) =>
{
    var login = (input.Login ?? "").Trim().ToLowerInvariant();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var count = await db.LoginAttempts.CountAsync(x => x.Login == login && x.Ip == ip && !x.Succeeded && x.CreatedAt > DateTime.UtcNow.AddMinutes(-15));
    if (count >= 5) return Results.Problem("Вход временно ограничен. Повторите попытку позже.", statusCode: 429);
    var user = await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Login == login);
    // D-07: always run the PBKDF2 verification so an unknown login costs the same time as a wrong password.
    var passwordMatches = input.Password is { Length: <=256 } && Password.Verify(input.Password, user?.PasswordHash ?? Password.DummyHash);
    var valid = user is not null && user.Active && passwordMatches;
    db.LoginAttempts.Add(new() { Login = login, Ip = ip, Succeeded = valid }); await db.SaveChangesAsync();
    if (!valid)
    {
        await audit.Add(null, "login_failed", "user", null, new { login, ip });
        await Task.Delay(Math.Min(1000, 150 * (count + 1)), ctx.RequestAborted);
        return Results.Problem("Неверный логин или пароль.", statusCode: 401);
    }
    var result = await Tokens.Issue(user!, ctx, db, configuration);
    await audit.Add(user!.Id, "login", "user", null, new { ip });
    return Results.Ok(result);
}).RequireRateLimiting("login");
auth.MapPost("/refresh", async (HttpContext ctx, CashDb db) =>
{
    if (!ctx.Request.Cookies.TryGetValue(Tokens.Cookie, out var raw)) return Results.Problem("Сеанс не найден.", statusCode: 401);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var old = await db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.UserRoles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Hash == Tokens.Hash(raw));
    if (old is not null && old.RevokedAt != null) { await db.RefreshTokens.Where(x=>x.UserId==old.UserId && x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTime.UtcNow)); await tx.CommitAsync(); return Results.Problem("Повторное использование сеанса. Выполните вход.",statusCode:401); }
    if (old is null || !old.User.Active || old.ExpiresAt <= DateTime.UtcNow) return Results.Problem("Сеанс недействителен.", statusCode: 401);
    old.RevokedAt = DateTime.UtcNow;
    var response = await Tokens.Issue(old.User, ctx, db, configuration, old);
    await db.SaveChangesAsync();
    await tx.CommitAsync(); return Results.Ok(response);
}).RequireRateLimiting("sensitive");
auth.MapPost("/logout", [Authorize] async (HttpContext ctx, CashDb db, Audit audit,IConfiguration configuration) =>
{
    if (ctx.Request.Cookies.TryGetValue(Tokens.Cookie, out var raw))
    {
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Hash == Tokens.Hash(raw) && x.RevokedAt == null);
        if (token is not null) { token.RevokedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
    }
    ctx.Response.Cookies.Delete(Tokens.Cookie, Tokens.Options(ctx,configuration));
    await audit.Add(ctx.UserId(), "logout", "user", null, new { });
    return Results.NoContent();
});
auth.MapGet("/me", [Authorize] async (HttpContext ctx, CashDb db) => Results.Ok((await db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).SingleAsync(x => x.Id == ctx.UserId())).Public()));
auth.MapPost("/change-password", [Authorize] async (PasswordDto input, HttpContext ctx, CashDb db, Audit audit) =>
{
    var u = await db.Users.SingleAsync(x => x.Id == ctx.UserId());
    if (!Password.Verify(input.OldPassword ?? "", u.PasswordHash)) return Results.Problem("Неверный текущий пароль.", statusCode: 400);
    if (!Password.Valid(input.NewPassword)||input.NewPassword!.Length>256) return Results.Problem("Пароль должен содержать от 10 до 256 символов, буквы и цифры.", statusCode: 400);
    await db.RefreshTokens.Where(x=>x.UserId==u.Id && x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTime.UtcNow));
    u.PasswordHash = Password.Hash(input.NewPassword!); u.MustChangePassword = false; await db.SaveChangesAsync();
    await audit.Add(u.Id, "password_changed", "user", null, new { }); return Results.NoContent();
}).RequireRateLimiting("sensitive");
        return app;
    }
}
