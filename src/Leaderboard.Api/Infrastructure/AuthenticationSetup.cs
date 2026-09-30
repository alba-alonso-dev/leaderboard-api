using Leaderboard.Api.Authentication;
using Leaderboard.Domain.Players;
using Leaderboard.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Leaderboard.Api.Infrastructure;

internal static class AuthenticationSetup
{
    /// <summary>
    /// Two independent schemes: JWT Bearer (default, people) and API key + HMAC (game servers, opt-in per endpoint).
    /// A JWT can never submit scores and an API key can never manage games.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyHmacAuthenticationHandler>(AuthSchemes.ApiKeyHmac, _ => { });

        // Configured from the validated JwtOptions so tests and environments override a single source of truth.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = LeaderboardClaims.UniqueName,
                    RoleClaimType = LeaderboardClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .AddPolicy(AuthPolicies.GameServer, policy => policy
                .AddAuthenticationSchemes(AuthSchemes.ApiKeyHmac)
                .RequireAuthenticatedUser()
                .RequireClaim(LeaderboardClaims.AuthType, LeaderboardClaims.GameServer))
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(PlayerRoles.Admin));

        return services;
    }
}
