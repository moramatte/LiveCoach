using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace VasaLiveFeeder;

public class Function1
{
    private readonly ILogger<Function1> _logger;

    public Function1(ILogger<Function1> logger)
    {
        _logger = logger;
    }

    [Function("TempoDelta")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        // Try to get raceName/progressInKm from query string first (manual parse)
        string raceName = null;
        string progressStr = null;
        var rawQuery = req.Url.Query; // starts with '?' when present
        if (!string.IsNullOrEmpty(rawQuery))
        {
            var q = rawQuery.TrimStart('?');
            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
                var val = Uri.UnescapeDataString(kv[1]);
                if (key == "racename" || key == "race") raceName = val;
                if (key == "progressinkm" || key == "progress" || key == "km") progressStr = val;
            }
        }

        // If not provided in query, read the request body
        if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(progressStr))
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                body = body.Trim();
                if (body.StartsWith("{"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("raceName", out var jRace)) raceName ??= jRace.GetString();
                        if (root.TryGetProperty("race", out var jRace2)) raceName ??= jRace2.GetString();
                        if (root.TryGetProperty("progressInKm", out var jProg)) progressStr ??= jProg.GetRawText().Trim('"');
                        if (root.TryGetProperty("progress", out var jProg2)) progressStr ??= jProg2.GetRawText().Trim('"');
                        if (root.TryGetProperty("km", out var jProg3)) progressStr ??= jProg3.GetRawText().Trim('"');
                    }
                    catch (JsonException) { /* ignore parse errors below */ }
                }
                else if (body.Contains(","))
                {
                    var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        raceName ??= parts[0].Trim();
                        progressStr ??= parts[1].Trim();
                    }
                }
                else
                {
                    var parts = body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        raceName ??= parts[0].Trim();
                        progressStr ??= parts[1].Trim();
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(progressStr))
        {
            _logger.LogWarning("Race name or progress not provided. Race='{Race}', Progress='{Progress}'", raceName, progressStr);
            var message = "Provide both race name and progress in km (query: ?raceName=..&progressInKm=.. or JSON body { \"raceName\":.., \"progressInKm\":.. }).";
            return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
        }

        if (!double.TryParse(progressStr.Trim().Trim('"'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var progressInKm))
        {
            _logger.LogWarning("Failed to parse progress. Progress='{ProgressStr}'", progressStr);
            var message = "Invalid progress value. Provide a numeric value (e.g. 42.5).";
            return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
        }

        // return parsed data as JSON
        var result = new { raceName = raceName, progressInKm = progressInKm };

        return await CreateJsonResponse(req, result, HttpStatusCode.OK);
    }

    private static async Task<HttpResponseData> CreateJsonResponse(HttpRequestData req, object value, HttpStatusCode status)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(value, options);
        var bytes = Encoding.UTF8.GetBytes(json);
        await resp.Body.WriteAsync(bytes, 0, bytes.Length);
        return resp;
    }
}