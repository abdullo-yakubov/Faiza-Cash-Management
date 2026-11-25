using FaizaGold.Api.Data;
using FaizaGold.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaizaGold.Api.Features;

public static class AuditEndpoints
{
    public static void MapAudit(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (string? action, int? take, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Audit.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
            return await q.OrderByDescending(a => a.Id).Take(Math.Clamp(take ?? 200, 1, 1000)).ToListAsync(ct);
        }).RequireAuthorization(p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Manager), nameof(UserRole.Accountant)));
    }
}
