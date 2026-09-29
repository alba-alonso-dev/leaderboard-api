using Leaderboard.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace Leaderboard.Api.OpenApi;

internal static class OpenApiSetup
{
    public const string BearerScheme = "Bearer";
    public const string ApiKeyScheme = "ApiKeyHmac";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Leaderboard API",
                    Version = "v1",
                    Description =
                        "Game leaderboards: score submission from game servers (API key + HMAC) and rankings " +
                        "(Top N, pages, absolute and relative player position). Errors follow RFC 9457 ProblemDetails.",
                    License = new OpenApiLicense { Name = "MIT" },
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from POST /api/v1/auth/login.",
                };
                document.Components.SecuritySchemes[ApiKeyScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Description =
                        "Game-server credential. Real clients send the key id plus X-Timestamp, X-Nonce and X-Signature (HMAC). " +
                        "In Swagger UI (Development only) enter `keyId:secret` and the page signs each request in the browser.",
                };
                return Task.CompletedTask;
            });

            // Marks secured operations so Swagger UI sends the right credential.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any())
                {
                    return Task.CompletedTask;
                }

                var authorize = metadata.OfType<IAuthorizeData>().ToList();
                if (authorize.Count == 0)
                {
                    return Task.CompletedTask;
                }

                var scheme = authorize.Any(a => a.Policy == AuthPolicies.GameServer) ? ApiKeyScheme : BearerScheme;
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
                });
                return Task.CompletedTask;
            });
        });
}
