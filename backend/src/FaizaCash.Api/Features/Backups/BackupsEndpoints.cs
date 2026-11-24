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

/// <summary>Модуль «Backups»: маршруты перенесены из ApiEndpoints.cs без изменения путей, кодов и форм JSON.</summary>
public static class BackupsEndpoints
{
    public static IEndpointRouteBuilder MapBackupsEndpoints(this IEndpointRouteBuilder app)
    {
        var backups = app.MapGroup("/api/backups").RequireAuthorization().WithTags("Backups");
backups.MapGet("/status", [Authorize(Roles = Admin)] async (BackupService backups, CancellationToken ct) => Results.Ok(await backups.StatusAsync(ct)));
backups.MapGet("", [Authorize(Roles = Admin)] async (BackupService backups, CancellationToken ct) => Results.Ok(await backups.ListAsync(ct)));
backups.MapPost("", [Authorize(Roles = Admin)] async (BackupService backups, HttpContext ctx, Audit audit, CancellationToken ct) =>
    await BackupAction(async()=>{var result=await backups.CreateAsync(ct);await audit.Add(ctx.UserId(),"backup_created","backup",null,new{id=result.Id,result.SizeBytes,result.Sha256});return result;},
        async error=>await audit.Add(ctx.UserId(),"backup_failed","backup",null,new{error}))).RequireRateLimiting("sensitive");
backups.MapPost("/{id}/verify", [Authorize(Roles = Admin)] async (string id, BackupService backups, HttpContext ctx, Audit audit, CancellationToken ct) =>
    await BackupAction(async()=>{var result=await backups.VerifyAsync(id, ct);await audit.Add(ctx.UserId(),"backup_verified","backup",null,new{id,result.Sha256});return result;},
        async error=>await audit.Add(ctx.UserId(),"backup_verify_failed","backup",null,new{id,error}))).RequireRateLimiting("sensitive");
backups.MapPost("/{id}/restore-test", [Authorize(Roles = Admin)] async (string id, BackupService backups, HttpContext ctx, Audit audit, CancellationToken ct) =>
{
    if (!backups.RestoreTestConfigured)
        return Results.Problem("Изолированная тестовая база восстановления не сконфигурирована.", statusCode: 503);
    return await BackupAction(async()=>{var result=await backups.RestoreTestAsync(id, ct);await audit.Add(ctx.UserId(),"backup_restore_tested","backup",null,new{id,destination="isolated-test-database",status=result.Status,result.DurationMs,matched=result.Reconciliation?.Matched});return result;},
        async error=>await audit.Add(ctx.UserId(),"backup_restore_failed","backup",null,new{id,destination="isolated-test-database",error}));
}).RequireRateLimiting("sensitive");
        return app;
    }
}
