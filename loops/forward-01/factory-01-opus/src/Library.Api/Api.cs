using System.Text.Json;
using System.Text.Json.Nodes;
using Library.Core;

namespace Library.Api;

/// <summary>
/// HTTP 表面のヘルパー: JSON 読み取り・型検証・エラー封筒・DTO 整形。
/// K-ERROR-SCHEMA-001 / K-JSON-001 / 仕様 §2 に従う。
/// </summary>
public static class Api
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        // 重複キー等は既定挙動。コメント・末尾カンマは許容しない(厳密)。
    };

    /// <summary>
    /// リクエストボディを JsonObject として読む。
    /// parseOk=false: ボディが JSON オブジェクトでない / 壊れている(→ invalid_request)。
    /// </summary>
    public static async Task<(JsonObject? json, bool parseOk)> ReadJson(HttpContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
                return (null, false);
            var node = JsonNode.Parse(body);
            if (node is JsonObject obj)
                return (obj, true);
            return (null, false);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>
    /// 文字列フィールドの取得。存在し JSON string であれば true(空文字も true)。
    /// 欠落・null・非文字列型は false(= invalid_request)。
    /// </summary>
    public static bool TryGetString(JsonObject json, string key, out string? value)
    {
        value = null;
        if (!json.TryGetPropertyValue(key, out var node) || node is null)
            return false;
        if (node is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            value = s;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 整数フィールドの取得。JSON number かつ整数値であれば true。
    /// 小数・真偽・文字列・欠落・null は false。
    /// </summary>
    public static bool TryGetInt(JsonObject json, string key, out int value)
    {
        value = 0;
        if (!json.TryGetPropertyValue(key, out var node) || node is null)
            return false;
        if (node is not JsonValue jv)
            return false;

        // 真偽値が number に化けないよう、まず bool/string を弾く。
        if (jv.TryGetValue<bool>(out _)) return false;
        if (jv.TryGetValue<string>(out _)) return false;

        // 整数として取れること(小数は弾く)。
        if (jv.TryGetValue<long>(out var l))
        {
            if (l < int.MinValue || l > int.MaxValue) return false;
            value = (int)l;
            return true;
        }
        return false;
    }

    // ---- エラー封筒(K-ERROR-SCHEMA-001) ---------------------------------------

    public static IResult Error(int status, string code, string message)
        => Results.Json(new JsonObject
        {
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }, statusCode: status);

    public static string ErrorJson(string code, string message)
        => new JsonObject
        {
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();

    public static string ErrorCode(LendingError e) => e switch
    {
        LendingError.NoCopiesAvailable => "no_copies_available",
        LendingError.LoanLimitExceeded => "loan_limit_exceeded",
        LendingError.MemberOverdueBlocked => "member_overdue_blocked",
        _ => "invalid_request"
    };

    public static string ErrorMessage(LendingError e) => e switch
    {
        LendingError.NoCopiesAvailable => "no copies available",
        LendingError.LoanLimitExceeded => "loan limit exceeded",
        LendingError.MemberOverdueBlocked => "member is overdue blocked",
        _ => "invalid request"
    };

    // ---- DTO 整形(K-JSON-001) -------------------------------------------------

    public static JsonObject BookObject(Book book, int activeLoanCount)
        => new()
        {
            ["id"] = book.Id,
            ["title"] = book.Title,
            ["copies"] = book.Copies,
            ["availableCopies"] = LendingRules.AvailableCopies(book.Copies, activeLoanCount)
        };

    /// <summary>
    /// 貸出オブジェクト。active は returnedAtUtc/fineAmount を含めない(省略)。
    /// returned は両フィールドを含む。
    /// </summary>
    public static JsonObject LoanObject(Loan loan)
    {
        var obj = new JsonObject
        {
            ["id"] = loan.Id,
            ["bookId"] = loan.BookId,
            ["memberId"] = loan.MemberId,
            ["loanedAtUtc"] = Iso.Format(loan.LoanedAtUtc),
            ["dueDateUtc"] = Iso.FormatDate(loan.DueDateUtc),
            ["status"] = loan.Status == LoanStatus.Returned ? "returned" : "active"
        };
        if (loan.Status == LoanStatus.Returned)
        {
            obj["returnedAtUtc"] = Iso.Format(loan.ReturnedAtUtc!.Value);
            obj["fineAmount"] = loan.FineAmount!.Value;
        }
        return obj;
    }
}
