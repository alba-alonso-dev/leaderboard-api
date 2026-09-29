using System.Reflection;
using NetArchTest.Rules;

namespace Leaderboard.ArchitectureTests;

/// <summary>The dependency rule of Clean Architecture, enforced on every build.</summary>
public sealed class LayerDependencyTests
{
    internal static readonly Assembly DomainAssembly = typeof(Leaderboard.Domain.Common.Result).Assembly;
    internal static readonly Assembly ApplicationAssembly = typeof(Leaderboard.Application.DependencyInjection).Assembly;
    internal static readonly Assembly InfrastructureAssembly = typeof(Leaderboard.Infrastructure.DependencyInjection).Assembly;
    internal static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_DependsOnNothing()
    {
        var result = Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOnAny(
            "Leaderboard.Application", "Leaderboard.Infrastructure", "Leaderboard.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "FluentValidation", "Npgsql").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrWeb()
    {
        var result = Types.InAssembly(ApplicationAssembly).ShouldNot().HaveDependencyOnAny(
            "Leaderboard.Infrastructure", "Leaderboard.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(InfrastructureAssembly).ShouldNot().HaveDependencyOn("Leaderboard.Api").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Endpoints_DoNotTouchPersistenceDirectly()
    {
        var result = Types.InAssembly(ApiAssembly).That().ResideInNamespace("Leaderboard.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("Leaderboard.Infrastructure", "Microsoft.EntityFrameworkCore").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    internal static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
