using System.Reflection;
using Leaderboard.Application.Abstractions.Messaging;
using NetArchTest.Rules;
using static Leaderboard.ArchitectureTests.LayerDependencyTests;

namespace Leaderboard.ArchitectureTests;

public sealed class ConventionTests
{
    [Theory]
    [InlineData(typeof(ICommandHandler<,>))]
    [InlineData(typeof(IQueryHandler<,>))]
    public void Handlers_AreSealedInternalAndNamedHandler(Type handlerInterface)
    {
        var result = Types.InAssembly(ApplicationAssembly).That()
            .ImplementInterface(handlerInterface).And().DoNotHaveNameMatching("Decorator")
            .Should().BeSealed().And().NotBePublic().And().HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Validators_AreSealedAndNamedValidator()
    {
        var result = Types.InAssembly(ApplicationAssembly).That().Inherit(typeof(FluentValidation.AbstractValidator<>))
            .Should().BeSealed().And().HaveNameEndingWith("Validator")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void EveryCommandAndQuery_HasExactlyOneHandler()
    {
        var requests = ApplicationAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>))))
            .ToList();
        var handled = ApplicationAssembly.GetTypes()
            .Where(t => !t.Name.Contains("Decorator", StringComparison.Ordinal))
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        requests.ShouldNotBeEmpty();
        foreach (var request in requests)
        {
            handled.Count(h => h == request).ShouldBe(1, $"{request.Name} must have exactly one handler");
        }
    }

    [Fact]
    public void DomainEntities_DoNotExposePublicSetters()
    {
        var violations = DomainAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace?.StartsWith("Leaderboard.Domain", StringComparison.Ordinal) == true)
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .Where(t => !typeof(Leaderboard.Domain.Common.Error).IsAssignableFrom(t)) // records: init-only
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod?.IsPublic == true)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void ApiEndpointGroups_AreAllMapped()
    {
        var groups = ApiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Leaderboard.Api.Endpoints.IEndpointGroup).IsAssignableFrom(t))
            .ToList();
        var mapped = typeof(Leaderboard.Api.Endpoints.IEndpointGroup).Assembly
            .GetType("Leaderboard.Api.Endpoints.EndpointGroupExtensions")!
            .GetField("Groups", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null) as Leaderboard.Api.Endpoints.IEndpointGroup[];

        mapped.ShouldNotBeNull().Select(g => g.GetType()).ShouldBe(groups, ignoreOrder: true);
    }

    [Fact]
    public void ApiEndpointGroups_AreSealed()
    {
        var result = Types.InAssembly(ApiAssembly).That().ImplementInterface(typeof(Leaderboard.Api.Endpoints.IEndpointGroup))
            .Should().BeSealed().GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }
}
