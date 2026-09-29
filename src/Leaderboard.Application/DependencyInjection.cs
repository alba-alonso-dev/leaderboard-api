using FluentValidation;
using Leaderboard.Application.Abstractions;
using Leaderboard.Application.Abstractions.Messaging;
using Leaderboard.Application.ApiKeys;
using Leaderboard.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Leaderboard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime());

        // Decorators wrap in registration order: Logging(Validation(handler)).
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationCommandDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationQueryDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryDecorator<,>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<TokenIssuer>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ApiKeyOptions>().Bind(configuration.GetSection(ApiKeyOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ScoreSubmissionOptions>()
            .Bind(configuration.GetSection(ScoreSubmissionOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        return services;
    }
}
