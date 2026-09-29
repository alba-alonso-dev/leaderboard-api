namespace Leaderboard.Api.Infrastructure;

/// <summary>Minimal HTTP probe so container HEALTHCHECK works in distroless/chiseled images.</summary>
internal static class ContainerHealthProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var url = args.Length > 1 ? args[1] : "http://localhost:8080/health/live";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(new Uri(url));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
