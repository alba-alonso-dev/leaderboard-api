using System.Net;
using System.Net.Http.Json;
using Leaderboard.Api.IntegrationTests.Infrastructure;
using Leaderboard.Application.Auth;
using Leaderboard.Application.Players;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Leaderboard.Api.IntegrationTests.Features;

public sealed class AuthTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Register_ReturnsCreatedWithLocationAndNoSecrets()
    {
        var response = await Api.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { username = "ana_dev", email = "ana@example.com", password = "Sup3rSecret1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("password", Case.Insensitive);
        var player = await response.ReadAsync<PlayerResponse>();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/players/{player.Id}");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        await Api.RegisterAsync("ana_dev");

        var response = await Api.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { username = "other", email = "ANA_DEV@example.com", password = "Sup3rSecret1" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "player.email_taken");
    }

    [Fact]
    public async Task Register_DuplicateUsernameIgnoringCase_ReturnsConflict()
    {
        await Api.RegisterAsync("ana_dev");

        var response = await Api.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { username = "ANA_DEV", email = "new@example.com", password = "Sup3rSecret1" });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "player.username_taken");
    }

    [Theory]
    [InlineData("ab", "ana@example.com", "Sup3rSecret1", "username")]
    [InlineData("ana_dev", "no-es-email", "Sup3rSecret1", "email")]
    [InlineData("ana_dev", "ana@example.com", "corta", "password")]
    public async Task Register_InvalidInput_ReturnsValidationProblem(string username, string email, string password, string field)
    {
        var response = await Api.Client.PostAsJsonAsync("/api/v1/auth/register", new { username, email, password });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Register_MalformedJson_ReturnsBadRequestProblem()
    {
        var response = await Api.Client.PostAsync("/api/v1/auth/register", "{ not json".AsJson());

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ReturnsJwtWithExpectedClaims()
    {
        var player = await Api.RegisterAsync();

        var tokens = await Api.LoginAsync(player.Email, ApiDriver.DefaultPassword);

        tokens.TokenType.ShouldBe("Bearer");
        tokens.ExpiresIn.ShouldBe(900);
        var jwt = new JsonWebToken(tokens.AccessToken);
        jwt.Subject.ShouldBe(player.Id.ToString());
        jwt.Claims.ShouldContain(c => c.Type == "role" && c.Value == "player");
        jwt.Issuer.ShouldBe("leaderboard-api");
    }

    [Fact]
    public async Task Login_InvalidCredentials_DoNotRevealWhetherEmailExists()
    {
        var player = await Api.RegisterAsync();

        var unknownEmail = await Api.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.com", password = "Whatever123" });
        var wrongPassword = await Api.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = player.Email, password = "Whatever123" });

        await unknownEmail.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await wrongPassword.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Me_WithToken_ReturnsProfile()
    {
        var player = await Api.RegisterAsync();

        var me = await Api.GetAsync<PlayerProfileResponse>("/api/v1/players/me", player.AccessToken);

        me.Id.ShouldBe(player.Id);
        me.Email.ShouldBe(player.Email);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorizedProblem() =>
        await (await Api.Client.GetAsync("/api/v1/players/me")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.unauthorized");

    [Fact]
    public async Task Me_WithTamperedToken_ReturnsUnauthorized()
    {
        var player = await Api.RegisterAsync();

        var response = await Api.SendAsync(HttpMethod.Get, "/api/v1/players/me", player.AccessToken[..^4] + "AAAA");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublicProfile_ExposesNoEmail()
    {
        var player = await Api.RegisterAsync();

        var body = await (await Api.Client.GetAsync($"/api/v1/players/{player.Id}")).Content.ReadAsStringAsync();

        body.ShouldContain(player.Username);
        body.ShouldNotContain("@");
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReuseRevokesTheFamily()
    {
        var player = await Api.RegisterAsync();

        var first = await Api.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = player.RefreshToken });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await first.ReadAsync<TokenResponse>();
        rotated.RefreshToken.ShouldNotBe(player.RefreshToken);

        // The old token is presented again (stolen): rejected, and the new one dies with it.
        var reuse = await Api.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = player.RefreshToken });
        await reuse.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");

        var afterTheft = await Api.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        await afterTheft.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var player = await Api.RegisterAsync();

        var logout = await Api.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", player.AccessToken, new { refreshToken = player.RefreshToken });
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await Api.Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = player.RefreshToken });
        await refresh.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.invalid_refresh_token");
    }
}
