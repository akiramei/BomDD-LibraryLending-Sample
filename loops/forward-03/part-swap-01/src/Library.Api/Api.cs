using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Library.Api;

/// <summary>
/// HTTP-surface helpers: strict JSON body reading (raw type inspection, no coercion),
/// validation primitives, error/response builders, and loan response shaping (K-JSON-001).
/// </summary>
public static class Api
{
    private static readonly JsonSerializerOptions ErrorJson = new();

    // ---- request body reading ---------------------------------------------

    /// <summary>
    /// Read the request body as a JSON object. Returns (false, _, errorResult) with a 400
    /// invalid_request for missing/malformed body or a non-object top-level value.
    /// </summary>
    public static async Task<(bool ok, JsonElement root, IResult? error)> ReadJsonObject(HttpContext http)
    {
        JsonDocument doc;
        try
        {
            using var reader = new StreamReader(http.Request.Body);
            var text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text))
                return (false, default, Invalid("request body must be a JSON object"));
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return (false, default, Invalid("request body must be valid JSON"));
        }

        var root = doc.RootElement.Clone();
        doc.Dispose();
        if (root.ValueKind != JsonValueKind.Object)
            return (false, default, Invalid("request body must be a JSON object"));
        return (true, root, null);
    }

    /// <summary>True when property exists and is a JSON string (any string, including empty).</summary>
    public static bool TryGetString(JsonElement obj, string name, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return false;
        value = p.GetString();
        return value is not null;
    }

    /// <summary>True when property exists, is a JSON string, and is non-empty / non-whitespace.</summary>
    public static bool TryGetNonEmptyString(JsonElement obj, string name, out string? value)
    {
        if (!TryGetString(obj, name, out value)) return false;
        if (string.IsNullOrWhiteSpace(value)) { value = null; return false; }
        return true;
    }

    /// <summary>True when property exists and is a JSON integer (no fractional part).</summary>
    public static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number)
            return false;
        return p.TryGetInt32(out value);
    }

    // ---- response builders -------------------------------------------------

    public static IResult Invalid(string message) =>
        Results.Json(ErrorEnvelope.Of(ErrorCodes.InvalidRequest, message), statusCode: StatusCodes.Status400BadRequest);

    public static IResult NotFound() =>
        Results.Json(ErrorEnvelope.Of(ErrorCodes.NotFound, "resource not found"), statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string code, string message) =>
        Results.Json(ErrorEnvelope.Of(code, message), statusCode: StatusCodes.Status409Conflict);

    public static IResult Internal() =>
        Results.Json(ErrorEnvelope.Of(ErrorCodes.InternalError, "internal error"), statusCode: StatusCodes.Status500InternalServerError);

    public static string SerializeError(string code, string message) =>
        JsonSerializer.Serialize(ErrorEnvelope.Of(code, message), ErrorJson);

    // ---- loan response shaping --------------------------------------------

    /// <summary>
    /// Shape a loan for JSON. active loans omit returnedAtUtc/fineAmount entirely; returned
    /// loans include both (K-JSON-001 / spec §2.6).
    /// </summary>
    public static object LoanJson(LoanRow l)
    {
        if (l.Status == "returned")
        {
            return new Dictionary<string, object?>
            {
                ["id"] = l.Id,
                ["bookId"] = l.BookId,
                ["memberId"] = l.MemberId,
                ["loanedAtUtc"] = l.LoanedAtUtc,
                ["dueDateUtc"] = l.DueDateUtc,
                ["status"] = "returned",
                ["returnedAtUtc"] = l.ReturnedAtUtc,
                ["fineAmount"] = l.FineAmount
            };
        }

        return new Dictionary<string, object?>
        {
            ["id"] = l.Id,
            ["bookId"] = l.BookId,
            ["memberId"] = l.MemberId,
            ["loanedAtUtc"] = l.LoanedAtUtc,
            ["dueDateUtc"] = l.DueDateUtc,
            ["status"] = "active"
        };
    }
}
