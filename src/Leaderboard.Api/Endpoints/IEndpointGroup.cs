namespace Leaderboard.Api.Endpoints;

/// <summary>A cohesive set of routes for one feature. Discovered and mapped at startup.</summary>
public interface IEndpointGroup
{
    void Map(IEndpointRouteBuilder app);
}

internal static class EndpointGroupExtensions
{
    /// <summary>Mapping order is also the order of sections in Swagger UI (it follows the user journey).</summary>
    private static readonly IEndpointGroup[] Groups =
    [
        new AuthEndpoints(),
        new PlayersEndpoints(),
        new GamesEndpoints(),
        new ScoresEndpoints(),
        new LeaderboardEndpoints(),
        new AdminEndpoints(),
    ];

    public static IEndpointRouteBuilder MapEndpointGroups(this IEndpointRouteBuilder app)
    {
        foreach (var group in Groups)
        {
            group.Map(app);
        }

        return app;
    }
}
