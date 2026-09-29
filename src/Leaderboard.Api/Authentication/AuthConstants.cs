namespace Leaderboard.Api.Authentication;

public static class AuthSchemes
{
    public const string ApiKeyHmac = "ApiKeyHmac";
}

public static class AuthPolicies
{
    public const string GameServer = "GameServer";
    public const string Admin = "Admin";
}

public static class LeaderboardClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string UniqueName = "unique_name";
    public const string AuthType = "auth_type";
    public const string GameServer = "game_server";
    public const string GameId = "game_id";
    public const string ApiKeyId = "api_key_id";
    public const string KeyId = "key_id";
    public const string Nonce = "nonce";
}
