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

/// <summary>Модуль «Users»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/users").RequireAuthorization().WithTags("Users");
        var roles = app.MapGroup("/api/roles").RequireAuthorization().WithTags("Users");
        var access = app.MapGroup("/api/access").RequireAuthorization().WithTags("Users");
users.MapGet("", [Authorize(Roles = Admin)] async (CashDb db, int page = 1, int pageSize = 25, string? q = null) =>
    // D-11: the same search bounds as the other lists.
    q?.Length > 160 || q?.Contains('\0') == true ? Results.Problem("Некорректный поисковый запрос.", statusCode: 400) :
    await Paged(db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role).Where(x => q == null || x.Login.Contains(q) || x.FullName.Contains(q)).OrderBy(x => x.Login), page, pageSize, x => x.Public()));
users.MapPost("", [Authorize(Roles = Admin)] async (UserInput input, HttpContext ctx, CashDb db, Audit audit) =>
{
    if (input.Roles is null || string.IsNullOrWhiteSpace(input.Login) || input.Login.Trim().Length>80 || input.Login.Contains('\0') || string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Trim().Length>160 || input.FullName.Contains('\0') || !Password.Valid(input.Password) || input.Password!.Length>256) return Results.Problem("Проверьте логин, имя и пароль.", statusCode: 400);
    var login = input.Login.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(x => x.Login == login)) return Results.Problem("Логин уже занят.", statusCode: 409);
    var roles = await db.Roles.Where(x => input.Roles.Contains(x.Code)).ToListAsync();
    if (input.Roles.Count==0 || roles.Count != input.Roles.Count) return Results.Problem("Неизвестная роль.", statusCode: 400);
    var u = new User { Login = login, FullName = input.FullName.Trim(), PasswordHash = Password.Hash(input.Password!), MustChangePassword = input.MustChangePassword };
    u.UserRoles = roles.Select(x => new UserRole { Role = x }).ToList(); db.Users.Add(u); await db.SaveChangesAsync(); await audit.Add(ctx.UserId(), "user_created", "user", null, new { u.Login }); return Results.Created($"/api/users/{u.Id}", u.Public());
});
users.MapPatch("/{id:guid}", [Authorize(Roles = Admin)] async (Guid id, UserPatch input, CashDb db, HttpContext ctx, Audit audit) =>
{
    if(id==ctx.UserId() && (input.Active==false || input.Roles!=null&&!input.Roles.Contains(Admin))) return Results.Problem("Нельзя отключить собственную административную учётную запись.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync();
    var u = await db.Users.Include(x => x.UserRoles).SingleOrDefaultAsync(x => x.Id == id); if (u is null) return Results.NotFound();
    var beforeName=u.FullName;var beforeActive=u.Active;var beforeRoles=await db.UserRoles.Where(x=>x.UserId==id).Select(x=>x.Role.Code).OrderBy(x=>x).ToArrayAsync();
    if(input.FullName is not null && (string.IsNullOrWhiteSpace(input.FullName)||input.FullName.Trim().Length>160||input.FullName.Contains('\0'))) return Results.Problem("Укажите имя пользователя длиной до 160 символов.",statusCode:400);
    if (input.FullName is not null) u.FullName = input.FullName.Trim(); if (input.Active.HasValue) u.Active = input.Active.Value;
    if (input.Roles is not null) { var rs = await db.Roles.Where(x => input.Roles.Contains(x.Code)).ToListAsync(); if (input.Roles.Count==0 || rs.Count != input.Roles.Count) return Results.Problem("Неизвестная роль.", statusCode: 400); u.UserRoles.RemoveAll(x=>!rs.Any(r=>r.Id==x.RoleId)); foreach(var role in rs) if(!u.UserRoles.Any(x=>x.RoleId==role.Id)) u.UserRoles.Add(new UserRole {UserId=id,RoleId=role.Id}); }
    await db.SaveChangesAsync();
    var changes=new Dictionary<string,object>();
    if(beforeName!=u.FullName)changes["FullName"]=new {before=beforeName,after=u.FullName};
    if(beforeActive!=u.Active)changes["Active"]=new {before=beforeActive,after=u.Active};
    var afterRoles=input.Roles?.OrderBy(x=>x).ToArray()??beforeRoles;
    if(!beforeRoles.SequenceEqual(afterRoles))changes["Roles"]=new {before=beforeRoles,after=afterRoles};
    await audit.Add(ctx.UserId(),"user_updated","user",null,new{id,changes});await tx.CommitAsync();return Results.NoContent();
});
users.MapPost("/{id:guid}/password-reset", [Authorize(Roles = Admin)] async (Guid id, PasswordResetInput input, CashDb db, HttpContext ctx, Audit audit) =>
{
    if(!Password.Valid(input.NewPassword)||input.NewPassword!.Length>256)return Results.Problem("Пароль должен содержать от 10 до 256 символов, буквы и цифры.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync(ctx.RequestAborted);
    var user=await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ctx.RequestAborted);
    if(user is null)return Results.NotFound();
    user.PasswordHash=Password.Hash(input.NewPassword!);
    user.MustChangePassword=true;
    await db.RefreshTokens.Where(x=>x.UserId==id&&x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTime.UtcNow),ctx.RequestAborted);
    await db.SaveChangesAsync(ctx.RequestAborted);
    await audit.Add(ctx.UserId(),"user_password_reset","user",null,new{userId=id});
    await tx.CommitAsync(ctx.RequestAborted);
    return Results.NoContent();
}).RequireRateLimiting("sensitive");
roles.MapGet("", [Authorize] async (CashDb db) => Results.Ok(await db.Roles.AsNoTracking().OrderBy(x => x.Code).ToListAsync()));
access.MapGet("", [Authorize(Roles=Admin)] async(HttpContext ctx,CashDb db,int? page,int? pageSize)=>await ListPage(ctx,db.CashAccess.AsNoTracking().OrderBy(x=>x.UserId).ThenBy(x=>x.CashboxId),page,pageSize,ctx.RequestAborted));
access.MapPost("", [Authorize(Roles=Admin)] async(AccessInput x,HttpContext ctx,CashDb db,Audit audit)=>
{
    if(!await db.Users.AnyAsync(u=>u.Id==x.UserId)||!await db.Cashboxes.AnyAsync(c=>c.Id==x.CashboxId))return Results.Problem("Пользователь или касса не найдены.",statusCode:400);
    await using var tx=await db.Database.BeginTransactionAsync();
    if(!await db.CashAccess.AnyAsync(a=>a.UserId==x.UserId&&a.CashboxId==x.CashboxId))
    {
        db.CashAccess.Add(new(){UserId=x.UserId,CashboxId=x.CashboxId});
        await db.SaveChangesAsync();
        await audit.Add(ctx.UserId(),"cash_access_granted","CashAccess",x.CashboxId,new{ x.UserId,x.CashboxId });
    }
    await tx.CommitAsync();
    return Results.NoContent();
});
access.MapDelete("/{userId:guid}/{cashboxId:long}", [Authorize(Roles=Admin)] async(Guid userId,long cashboxId,HttpContext ctx,CashDb db,Audit audit)=>
{
    await using var tx=await db.Database.BeginTransactionAsync();
    var access=await db.CashAccess.SingleOrDefaultAsync(a=>a.UserId==userId&&a.CashboxId==cashboxId);
    if(access is not null)
    {
        db.CashAccess.Remove(access);
        await db.SaveChangesAsync();
        await audit.Add(ctx.UserId(),"cash_access_revoked","CashAccess",cashboxId,new{UserId=userId,CashboxId=cashboxId});
    }
    await tx.CommitAsync();
    return Results.NoContent();
});
        return app;
    }
}
