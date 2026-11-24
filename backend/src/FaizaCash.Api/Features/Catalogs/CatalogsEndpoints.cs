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

/// <summary>Модуль «Catalogs»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class CatalogsEndpoints
{
    public static IEndpointRouteBuilder MapCatalogsEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Catalogs");
        var counterparties = app.MapGroup("/api/counterparties").RequireAuthorization().WithTags("Catalogs");
        var documentTypes = app.MapGroup("/api/document-types").RequireAuthorization().WithTags("Catalogs");
categories.MapGet("", [Authorize] async (HttpContext ctx, CashDb db, string? type, string? q, int? page, int? pageSize) =>
{
    if(type is not null&&type is not ("income" or "expense"))return Results.Problem("Недопустимое направление категории.",statusCode:400);
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return await ListPage(ctx,db.Categories.AsNoTracking().Where(x=>(type==null||x.Type==type)&&(q==null||x.Name.Contains(q))).OrderBy(x=>x.Name).ThenBy(x=>x.Id),page,pageSize,ctx.RequestAborted);
});
counterparties.MapGet("", [Authorize] async (HttpContext ctx, CashDb db, string? q, int? page, int? pageSize) =>
{
    if(q?.Length>160||q?.Contains('\0')==true)return Results.Problem("Некорректный поисковый запрос.",statusCode:400);
    return await ListPage(ctx,db.Counterparties.AsNoTracking().Where(x=>q==null||x.Name.Contains(q)).OrderBy(x=>x.Name).ThenBy(x=>x.Id),page,pageSize,ctx.RequestAborted);
});
documentTypes.MapGet("", [Authorize] async (CashDb db) => Results.Ok(await db.DocumentTypes.OrderBy(x => x.Name).ToListAsync()));
documentTypes.MapPost("", [Authorize(Roles = Admin + "," + Accountant)] async (DocumentTypeInput x, CashDb db) =>
{
    var code=x.Code?.Trim().ToUpperInvariant();var name=x.Name?.Trim();
    if(string.IsNullOrEmpty(code)||code.Length>24||code.Contains('\0')||string.IsNullOrEmpty(name)||name.Length>160||name.Contains('\0'))
        return Results.Problem("Укажите код длиной до 24 символов и название длиной до 160 символов.",statusCode:400);
    db.DocumentTypes.Add(new(){Code=code,Name=name});
    try { await db.SaveChangesAsync(); }
    catch(DbUpdateException error) when(error.InnerException is PostgresException { SqlState:PostgresErrorCodes.UniqueViolation,ConstraintName:"IX_DocumentTypes_NormalizedCode" })
    {
        return Results.Problem("Код вида документа уже существует.",statusCode:409);
    }
    return Results.NoContent();
});
        return app;
    }
}
