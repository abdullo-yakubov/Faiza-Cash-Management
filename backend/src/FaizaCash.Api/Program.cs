using FaizaCash.Application;
using FaizaCash.Infrastructure;
using FaizaCash.Api;
using FaizaCash.Api.Middleware;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using FaizaCash.Infrastructure.Services;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(x => x.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
if (builder.Environment.IsProduction())
{
    if(!builder.Configuration.GetValue<bool>("RefreshCookie:Secure"))throw new InvalidOperationException("Production requires RefreshCookie:Secure=true.");
    // D-09: production backups must be encrypted at rest.
    if(FaizaCash.Infrastructure.Services.BackupCrypto.ParseKey(builder.Configuration["Backups:EncryptionKey"]) is null)throw new InvalidOperationException("Production requires Backups:EncryptionKey (base64, 32 bytes).");
    var configuredProxies=builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()??[];
    if(configuredProxies.Length==0)throw new InvalidOperationException("Production requires at least one trusted ForwardedHeaders:KnownProxies entry.");
    var trustedProxies=configuredProxies.Select(value=>IPAddress.TryParse(value,out var address)?address:throw new InvalidOperationException($"Invalid trusted proxy address: {value}")).ToArray();
    builder.Services.Configure<ForwardedHeadersOptions>(options=>
    {
        options.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;
        options.ForwardLimit=1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach(var proxy in trustedProxies)options.KnownProxies.Add(proxy);
    });
}
var cs = builder.Configuration.GetConnectionString("CashDb") ?? throw new InvalidOperationException("CashDb is not configured.");
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
if (jwtKey.Length < 32) throw new InvalidOperationException("Jwt:Key must have at least 32 characters.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "FaizaCash";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "FaizaCash";
builder.Services.AddDbContext<CashDb>(x => x.UseNpgsql(cs));
builder.Services.AddScoped<Audit>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor,CurrentActor>();
builder.Services.AddScoped<ICashboxAccess,CashboxAccessService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddScoped<ILedgerService,LedgerService>();
builder.Services.AddScoped<IBalanceService,BalanceService>();
builder.Services.AddSingleton<IPdfReport,PdfReport>();
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddProvider(new JsonFileLoggerProvider(builder.Configuration["Logs:Directory"] ?? ".runtime/logs"));
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.Converters.Add(new DecimalStringConverter()));
builder.Services.AddMemoryCache();
var frontendOrigins = (builder.Configuration["Frontend:Origins"] ?? builder.Configuration["Frontend:Origin"] ?? "http://localhost:8085;http://127.0.0.1:8085")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (frontendOrigins.Length == 0)
{
    frontendOrigins = ["http://localhost:8085", "http://127.0.0.1:8085"];
}
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.AddPolicy("login", c=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=> new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit=15,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
    // D-06: authenticated callers are partitioned by user id, anonymous refresh calls by client IP.
    o.AddPolicy("sensitive", c=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(c.User.Identity?.IsAuthenticated==true ? "user:"+c.User.FindFirst(ClaimTypes.NameIdentifier)?.Value : "ip:"+(c.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _=> new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x => x.TokenValidationParameters = new()
{
    ValidateIssuer = true, ValidIssuer = jwtIssuer,
    ValidateAudience = true, ValidAudience = jwtAudience,
    ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(20),
    NameClaimType = ClaimTypes.NameIdentifier, RoleClaimType = ClaimTypes.Role
});
builder.Services.AddAuthorization();
builder.Services.AddFaizaOpenApi();
var app = builder.Build();
app.UseForwardedHeaders();
if(app.Environment.IsProduction()||app.Environment.IsStaging()) { app.UseHttpsRedirection(); app.UseHsts(); }
app.UseExceptionHandler(e => e.Run(async c =>
{
    app.Logger.LogError(c.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error, "Unhandled exception");
    await Results.Problem("Внутренняя ошибка сервера.", statusCode: 500).ExecuteAsync(c);
}));
app.Use(async (c, next) =>
{
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["X-Frame-Options"] = "DENY";
    c.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; base-uri 'self'; frame-ancestors 'none'";
    await next();
});
app.UseCors(); app.UseMiddleware<RequestMiddleware>(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.UseMiddleware<AccessMiddleware>(); app.UseMiddleware<DirectoryCacheMiddleware>();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CashDb>();
    if(app.Environment.IsDevelopment()||app.Environment.IsEnvironment("Test"))await db.Database.MigrateAsync();
    else if((await db.Database.GetPendingMigrationsAsync()).Any())
        throw new InvalidOperationException("Database migrations are pending. Apply them using the deployment migration job before starting the API.");
    await ApiEndpoints.Seed(db, builder.Configuration, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed"));
}

app.MapCashApi(builder.Configuration);
// The API contract lists every route and its role rule, so it is published to administrators only.
app.MapOpenApi("/api/openapi/{documentName}.json").RequireAuthorization(p => p.RequireRole("admin")).ExcludeFromDescription();
app.Run();
