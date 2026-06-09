using System.Text.Json.Serialization;

namespace Library.Api;

/// <summary>Error envelope (K-ERROR-SCHEMA-001 / spec §2.8): { "error": { "code", "message" } }.</summary>
public sealed record ErrorEnvelope([property: JsonPropertyName("error")] ErrorBody Error)
{
    public static ErrorEnvelope Of(string code, string message) => new(new ErrorBody(code, message));
}

public sealed record ErrorBody(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

/// <summary>The closed set of error codes (spec §2.8). No other code is ever returned.</summary>
public static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string NoCopiesAvailable = "no_copies_available";
    public const string LoanLimitExceeded = "loan_limit_exceeded";
    public const string MemberOverdueBlocked = "member_overdue_blocked";
    public const string AlreadyReturned = "already_returned";
    public const string InternalError = "internal_error";
}
