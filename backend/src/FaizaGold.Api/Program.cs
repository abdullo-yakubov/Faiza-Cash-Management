using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FaizaGold.Api.Data;
using FaizaGold.Api.Features;
using FaizaGold.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BusinessCalendar>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddExceptionHandler<ErrorHandler>();
// Ключи шифрования cookie храним в папке, чтобы после перезапуска контейнера не разлогинивать всех.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("FaizaGold");
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "faiza.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        // API отвечает кодами 401/403 вместо перенаправления на страницу входа.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = AuthEndpoints.ValidatePrincipalAsync;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(FaizaGold.Domain.UserRole.Admin)))
    .AddPolicy(Policies.ManageDirectories, p => p.RequireRole(
        nameof(FaizaGold.Domain.UserRole.Admin), nameof(FaizaGold.Domain.UserRole.Accountant)))
    .AddPolicy(Policies.Operate, p => p.RequireRole(
        nameof(FaizaGold.Domain.UserRole.Admin), nameof(FaizaGold.Domain.UserRole.Accountant),
        nameof(FaizaGold.Domain.UserRole.Cashier)))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var loginAttempts = builder.Configuration.GetValue("Security:LoginAttemptsPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttempts, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

var api = app.MapGroup("/api");
api.MapAuth();
api.MapUsers();
api.MapDirectories();
api.MapShifts();
api.MapOperations();
api.MapTransfers();
api.MapReports();
api.MapAudit();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await DbInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();

public static class Policies
{
    public const string Admin = "Admin";
    public const string ManageDirectories = "ManageDirectories";
    public const string Operate = "Operate";
}

public partial class Program;
