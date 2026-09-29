using System.Text.Json.Serialization;
using Leaderboard.Api.Authentication;
using Leaderboard.Api.Endpoints;
using Leaderboard.Api.ErrorHandling;
using Leaderboard.Api.Infrastructure;
using Leaderboard.Api.OpenApi;
using Leaderboard.Application;
using Leaderboard.Infrastructure;
using Leaderboard.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;

// `dotnet Leaderboard.Api.dll healthcheck` probes the running API. Used by Docker HEALTHCHECK: chiseled images have no curl.
if (args is ["healthcheck", ..])
{
    return await ContainerHealthProbe.RunAsync(args);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Leaderboard.Api")
        .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName));

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = 32 * 1024;
    });

    builder.Services.ConfigureHttpJsonOptions(json =>
        json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services
        .AddApplication(builder.Configuration)
        .AddInfrastructure(builder.Configuration)
        .AddApiProblemDetails()
        .AddApiAuthentication()
        .AddApiRateLimiting(builder.Configuration)
        .AddApiHealthChecks()
        .AddApiOpenApi()
        .AddRequestTimeouts(timeouts => timeouts.DefaultPolicy = new() { Timeout = TimeSpan.FromSeconds(30) });

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseSecurityHeaders();
    app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        diagnostics.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
        if (http.User.FindFirst(LeaderboardClaims.Subject)?.Value is { } playerId)
        {
            diagnostics.Set("PlayerId", playerId);
        }

        if (http.User.FindFirst(LeaderboardClaims.ApiKeyId)?.Value is { } apiKeyId)
        {
            diagnostics.Set("ApiKeyId", apiKeyId);
        }
    });

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseStaticFiles();
        app.MapOpenApi();
        app.UseSwaggerUI(ui =>
        {
            ui.SwaggerEndpoint("/openapi/v1.json", "Leaderboard API v1");
            ui.DocumentTitle = "Leaderboard API";
            ui.InjectJavascript("/swagger-hmac.js");
            ui.UseRequestInterceptor("function (request) { return window.leaderboardSignRequest(request); }");
            ui.EnablePersistAuthorization();
            ui.DisplayRequestDuration();
        });
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter(); // after authorization: the score-submit policy partitions by API key
    app.UseRequestTimeouts();

    app.MapApiHealthChecks();
    app.MapEndpointGroups();

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        await DatabaseMigrator.MigrateAsync(app.Services);
    }

    await AdminSeeder.SeedAsync(app.Services, app.Configuration);

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Leaderboard API terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
