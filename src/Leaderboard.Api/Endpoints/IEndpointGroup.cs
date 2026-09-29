using System.Reflection;

namespace Leaderboard.Api.Endpoints;

/// <summary>A cohesive set of routes for one feature. Discovered and mapped at startup.</summary>
public interface IEndpointGroup
{
    void Map(IEndpointRouteBuilder app);
}

internal static class EndpointGroupExtensions
{
    public static IEndpointRouteBuilder MapEndpointGroups(this IEndpointRouteBuilder app)
    {
        var groups = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpointGroup).IsAssignableFrom(t))
            .Select(t => (IEndpointGroup)Activator.CreateInstance(t)!);

        foreach (var group in groups)
        {
            group.Map(app);
        }

        return app;
    }
}
