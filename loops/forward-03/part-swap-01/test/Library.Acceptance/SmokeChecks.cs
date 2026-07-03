using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Library.Acceptance;

/// <summary>
/// L1 API smoke (M-BOM scope ②): launch Library.Api as a subprocess on a fresh DB, send one
/// happy-path request to each of the 6 endpoints, assert expected status, then stop the process.
/// </summary>
public static class SmokeChecks
{
    public static async Task Run(Harness h)
    {
        var apiProjectDir = LocateApiProjectDir();
        if (apiProjectDir is null)
        {
            h.Check("L1-SMOKE/locate Library.Api project", false);
            return;
        }

        var dbPath = Path.Combine(Path.GetTempPath(), $"library-smoke-{Guid.NewGuid():N}.db");
        const string url = "http://127.0.0.1:5099";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = apiProjectDir
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(apiProjectDir);
        psi.ArgumentList.Add("--no-build"); // built by the solution before the harness runs
        psi.Environment["ASPNETCORE_URLS"] = url;
        psi.Environment["LIBRARY_DB_PATH"] = dbPath;
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";

        Process? proc = null;
        try
        {
            proc = Process.Start(psi)!;
            // Drain output so the child does not block on a full pipe.
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();

            using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) };
            if (!await WaitForReady(client, proc))
            {
                h.Check("L1-SMOKE/API process became ready", false);
                return;
            }

            // 1. POST /v1/books
            var bookResp = await client.PostAsync("/v1/books",
                JsonContent("{\"title\":\"Smoke Title\",\"copies\":2}"));
            h.Check("L1-SMOKE/POST /v1/books -> 201", bookResp.StatusCode == HttpStatusCode.Created);
            var bookId = await ReadId(bookResp);

            // 2. GET /v1/books/{id}
            var getBook = await client.GetAsync($"/v1/books/{bookId}");
            h.Check("L1-SMOKE/GET /v1/books/{id} -> 200", getBook.StatusCode == HttpStatusCode.OK);

            // 3. POST /v1/members
            var memberResp = await client.PostAsync("/v1/members",
                JsonContent("{\"name\":\"Smoke Member\"}"));
            h.Check("L1-SMOKE/POST /v1/members -> 201", memberResp.StatusCode == HttpStatusCode.Created);
            var memberId = await ReadId(memberResp);

            // 4. POST /v1/loans
            var loanResp = await client.PostAsync("/v1/loans",
                JsonContent($"{{\"bookId\":\"{bookId}\",\"memberId\":\"{memberId}\",\"loanedAtUtc\":\"2026-06-10T10:00:00Z\"}}"));
            h.Check("L1-SMOKE/POST /v1/loans -> 201", loanResp.StatusCode == HttpStatusCode.Created);
            var loanId = await ReadId(loanResp);

            // 5. POST /v1/loans/{id}/return
            var returnResp = await client.PostAsync($"/v1/loans/{loanId}/return",
                JsonContent("{\"returnedAtUtc\":\"2026-06-12T10:00:00Z\"}"));
            h.Check("L1-SMOKE/POST /v1/loans/{id}/return -> 200", returnResp.StatusCode == HttpStatusCode.OK);

            // 6. GET /v1/loans?memberId=
            var listResp = await client.GetAsync($"/v1/loans?memberId={memberId}");
            h.Check("L1-SMOKE/GET /v1/loans?memberId= -> 200", listResp.StatusCode == HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            h.Check($"L1-SMOKE/no exception ({ex.GetType().Name}: {ex.Message})", false);
        }
        finally
        {
            TryKill(proc);
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* best effort */ }
        }
    }

    private static async Task<bool> WaitForReady(HttpClient client, Process proc)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited) return false;
            try
            {
                // any HTTP response (even 404) means the listener is up.
                var ping = await client.GetAsync("/v1/books/__ping__");
                if (ping is not null) return true;
            }
            catch
            {
                await Task.Delay(250);
            }
        }
        return false;
    }

    private static HttpContent JsonContent(string json) =>
        new StringContent(json, Encoding.UTF8, "application/json");

    private static async Task<string> ReadId(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static void TryKill(Process? proc)
    {
        if (proc is null) return;
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        try { proc.WaitForExit(5000); } catch { }
        proc.Dispose();
    }

    /// <summary>Walk up from the test assembly to find src/Library.Api.</summary>
    private static string? LocateApiProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Library.Api");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "Library.Api.csproj")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
