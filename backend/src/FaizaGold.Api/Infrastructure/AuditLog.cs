using System.Text.Json;
using FaizaGold.Api.Data;
using FaizaGold.Domain;

namespace FaizaGold.Api.Infrastructure;

public sealed class AuditLog(AppDbContext db, TimeProvider clock, IHttpContextAccessor accessor)
{
    /// <summary>Добавляет запись в журнал. Сохраняется вместе с основным изменением в одной транзакции.</summary>
    public void Add(string action, string entity, object? entityId, object? details = null, int? userId = null, string? login = null)
    {
        var principal = accessor.HttpContext?.User;
        var idClaim = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        db.Audit.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            UserId = userId ?? (int.TryParse(idClaim, out var id) ? id : null),
            UserLogin = login ?? principal?.Identity?.Name,
            Action = action,
            Entity = entity,
            EntityId = entityId?.ToString(),
            Details = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
