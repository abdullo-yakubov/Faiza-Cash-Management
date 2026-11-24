using FaizaCash.Domain;
using FaizaCash.Application;
using Microsoft.EntityFrameworkCore;

namespace FaizaCash.Api;

/// <summary>Общие помощники endpoint-модулей: постраничные списки и представления.</summary>
internal static class EndpointHelpers
{
internal const int MaxListSize = 500;
/// <summary>Bounded list: keeps the array contract, caps pageSize at 500 and reports the total in X-Total-Count.</summary>
internal static async Task<IResult> ListPage<T>(HttpContext ctx, IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
{
    var size=Math.Clamp(pageSize??MaxListSize,1,MaxListSize);var current=Math.Clamp(page??1,1,1000000);
    ctx.Response.Headers["X-Total-Count"]=(await query.CountAsync(ct)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    return Results.Ok(await query.Skip((current-1)*size).Take(size).ToListAsync(ct));
}
internal static object TransferView(CashTransfer x) => new { x.Id,x.SourceCashboxId,x.TargetCashboxId,x.Date,x.Amount,x.Description,x.SourceOperationId,x.TargetOperationId,x.CreatedAt,status="posted" };
internal static async Task<IResult> Paged<T, R>(IQueryable<T> q, int page, int size, Func<T, R> map) { page = Math.Max(1, page); size = Math.Clamp(size, 1, 100); var count = await q.CountAsync(); var rows = await q.Skip((page - 1) * size).Take(size).ToListAsync(); return Results.Ok(new { page, pageSize = size, total = count, items = rows.Select(map) }); }
}
