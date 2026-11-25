using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OpenApi;

namespace FaizaCash.Api;

/// <summary>
/// P2: OpenAPI document with the effective access rule of every operation (x-faiza-access):
/// anonymous flag, the roles that pass all authorization metadata (null = any authenticated user)
/// and the data scope enforced by AccessMiddleware/ICashboxAccess. tests/integration/p2.mjs checks it against real HTTP calls.
/// </summary>
public static class OpenApiSetup
{
    public static readonly string[] AllRoles = ["admin", "accountant", "cashier", "manager", "auditor"];

    public static IServiceCollection AddFaizaOpenApi(this IServiceCollection services) => services.AddOpenApi("v1", options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo { Title = "FAIZA GOLD API", Version = "v1", Description = "Кассовый учёт ООО «Фаиза». Расширение x-faiza-access описывает роли и скоуп каждой операции." };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var anonymous = metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<AuthorizationPolicy>().Any();
            IEnumerable<string>? roles = null;
            void Restrict(IEnumerable<string> allowed) => roles = (roles ?? AllRoles).Intersect(allowed, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var data in metadata.OfType<IAuthorizeData>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)))
                Restrict(data.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            foreach (var requirement in metadata.OfType<AuthorizationPolicy>().SelectMany(x => x.Requirements).OfType<RolesAuthorizationRequirement>())
                Restrict(requirement.AllowedRoles);
            var path = "/" + (context.Description.RelativePath ?? string.Empty).Split('?')[0];
            var parameters = context.Description.ParameterDescriptions.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scope = path.StartsWith("/api/cashboxes/{", StringComparison.Ordinal) || parameters.Contains("cashboxId") ? "cashbox"
                : parameters.Contains("branchId") ? "branch"
                : path.Contains("{id", StringComparison.Ordinal) ? "record" : "list";
            var access = new JsonObject
            {
                ["anonymous"] = anonymous,
                ["roles"] = anonymous || roles is null ? null : new JsonArray(roles.Order().Select(x => (JsonNode)x).ToArray()),
                ["scope"] = anonymous ? "none" : scope
            };
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions["x-faiza-access"] = new JsonNodeExtension(access);
            return Task.CompletedTask;
        });
    });
}
