using Microsoft.Extensions.Caching.Memory;
namespace FaizaCash.Api.Middleware;
/// <summary>30-second cache for directory reads; any successful directory write bumps the version.
/// The X-Total-Count header of bounded lists is cached together with the body.</summary>
public sealed class DirectoryCacheMiddleware(RequestDelegate next)
{
    private static long version;
    private sealed record Entry(byte[] Body, string? TotalCount);
    public async Task InvokeAsync(HttpContext c,IMemoryCache cache)
    {
        var path=c.Request.Path.Value;
        var directory=c.Request.Path.StartsWithSegments("/api/categories") || c.Request.Path.StartsWithSegments("/api/counterparties");
        if(c.User.Identity?.IsAuthenticated!=true || !directory){await next(c);return;}
        if(c.Request.Method!="GET"){await next(c);if(c.Response.StatusCode<300)Interlocked.Increment(ref version);return;}
        if(path is not ("/api/categories" or "/api/counterparties")){await next(c);return;}
        var key=$"directories:{Interlocked.Read(ref version)}:{path}:{c.Request.QueryString}";
        if(cache.TryGetValue<Entry>(key,out var hit)&&hit is not null)
        {
            c.Response.ContentType="application/json; charset=utf-8";
            if(hit.TotalCount is not null)c.Response.Headers["X-Total-Count"]=hit.TotalCount;
            await c.Response.Body.WriteAsync(hit.Body);return;
        }
        var output=c.Response.Body;await using var buffer=new MemoryStream();c.Response.Body=buffer;
        try
        {
            await next(c);var bytes=buffer.ToArray();
            if(c.Response.StatusCode==200)cache.Set(key,new Entry(bytes,c.Response.Headers["X-Total-Count"].FirstOrDefault()),TimeSpan.FromSeconds(30));
            await output.WriteAsync(bytes);
        }
        finally{c.Response.Body=output;}
    }
}
