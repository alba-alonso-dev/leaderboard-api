namespace Leaderboard.Api.ErrorHandling;

/// <summary>Codes for errors produced by the HTTP pipeline itself (not by use cases).</summary>
public static class ErrorCodes
{
    public const string Unauthorized = "auth.unauthorized";
    public const string Forbidden = "auth.forbidden";
    public const string NotFound = "resource.not_found";
    public const string MethodNotAllowed = "http.method_not_allowed";
    public const string UnsupportedMediaType = "http.unsupported_media_type";
    public const string BadRequest = "http.bad_request";
    public const string PayloadTooLarge = "http.payload_too_large";
    public const string RateLimited = "rate_limit.exceeded";
    public const string Unexpected = "server.unexpected";

    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => RateLimited,
        _ => status >= 500 ? Unexpected : "http." + status,
    };
}
