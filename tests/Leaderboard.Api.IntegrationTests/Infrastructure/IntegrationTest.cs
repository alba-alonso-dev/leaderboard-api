namespace Leaderboard.Api.IntegrationTests.Infrastructure;

/// <summary>Base class: clean database before each test and a typed driver over the HTTP API.</summary>
[Collection(ApiTestGroup.Name)]
public abstract class IntegrationTest(ApiFactory factory) : IAsyncLifetime
{
    protected ApiFactory Factory { get; } = factory;

    protected ApiDriver Api { get; } = new(factory.CreateClient());

    public virtual async ValueTask InitializeAsync() => await Factory.ResetDatabaseAsync();

    public ValueTask DisposeAsync()
    {
        Api.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
