using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Infrastructure;
using Infrastructure.Extensions;
using Infrastructure.Speed;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using VasaLiveFeeder.LiveScraper;


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
        string currentSpeedStr = null;
        var rawQuery = req.Url.Query; // starts with '?' when present
        try
        {
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
                    if (key == "currentspeed" || key == "speed") currentSpeedStr = val;
                }
            }
        }
        catch (Exception e)
        {
            return await CreateJsonResponse(req, $"Exception: {e}", HttpStatusCode.BadRequest);
        }

        // If not provided in query, read the request body
        try
        {
            if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(progressStr) || string.IsNullOrWhiteSpace(currentSpeedStr))
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
                            if (root.TryGetProperty("currentSpeed", out var jSpeed)) currentSpeedStr ??= jSpeed.GetRawText().Trim('"');
                            if (root.TryGetProperty("speed", out var jSpeed2)) currentSpeedStr ??= jSpeed2.GetRawText().Trim('"');
                        }
                        catch (JsonException) { /* ignore parse errors below */ }
                    }
                    else if (body.Contains(","))
                    {
                        var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            currentSpeedStr ??= parts[2].Trim();
                        }
                        else if (parts.Length >= 2)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                        }
                    }
                    else
                    {
                        var parts = body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            currentSpeedStr ??= parts[2].Trim();
                        }
                        else if (parts.Length >= 2)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            return await CreateJsonResponse(req, $"Exception 2: {e}", HttpStatusCode.BadRequest);
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

        double newSpeed;
        try
        {
             newSpeed = await DeriveTempoDelta(raceName, progressStr, currentSpeedStr);
        }
        catch (Exception e)
        {
            return await CreateJsonResponse(req, $"Exception deriving: {e}", HttpStatusCode.BadRequest);
        }

        // return parsed data as JSON
        var result = new { newSpeed = newSpeed };

        return await CreateJsonResponse(req, result, HttpStatusCode.OK);
    }

    public async Task<double> DeriveDistanceDelta(string raceName, double myProgress)
    {
        var scraper = ServiceLocator.Resolve<ILiveScraper>();
        var leaderDistance = await scraper.GetLeaderDistanceWithPlaywrightAsync("https://live.eqtiming.com/73152#result:297321-0-1308925-1-1-");
        
        var leaderKm = leaderDistance.Value;
        var ourPlus50Percent = myProgress * 1.5;
        var delta = leaderKm - ourPlus50Percent;

        return  delta;
    }

    public async Task<double> DeriveTempoDelta(string raceName, string myProgressStr, string meanSpeedStr)
    {
        var meanSpeed = Speed.FromKilometersPerHour(double.Parse(meanSpeedStr, CultureInfo.InvariantCulture));

        var myProgress = double.Parse(myProgressStr, CultureInfo.InvariantCulture);
        var totalDistance = GetTotalDistance(raceName);

        var behindInKm = await DeriveDistanceDelta(raceName, myProgress);

        var kmToGo = totalDistance - myProgress;
        var catchUpPerKm = behindInKm / kmToGo;
        var catchUpPerKmInMeters = catchUpPerKm * 1000.0;

        // Calculate required speed adjustment using simple ratio
        // If catchUpPerKmInMeters is positive: we're behind, need to speed up (cover MORE distance per km)
        //   - Speed ratio = (1000 + catchUpPerKmInMeters) / 1000 > 1.0
        //   - Required speed = current speed * ratio (faster)
        // If catchUpPerKmInMeters is negative: we're ahead, can slow down (cover LESS distance per km)
        //   - Speed ratio = (1000 + catchUpPerKmInMeters) / 1000 < 1.0
        //   - Required speed = current speed * ratio (slower)
        
        var speedRatio = (1000.0 + catchUpPerKmInMeters) / 1000.0;
        
        var currentSpeed = meanSpeed;
        var requiredSpeed = meanSpeed * speedRatio;
       
        // Time remaining at new pace
        var timeToGoSeconds = kmToGo * requiredSpeed.MinutesPerKilometer * 60.0;
        var tsToGo = TimeSpan.FromSeconds(timeToGoSeconds);
        
        var myTime = $"{tsToGo:hh\\:mm\\:ss}";
        
        _logger.LogInformation($"Old speed: {meanSpeed}. New speed: {requiredSpeed}. Estimated total time: {myTime}");

        return double.Round(requiredSpeed.KilometersPerHour, 2);
    }

    private double GetTotalDistance(string raceName)
    {
        return raceName.ToLower() switch
        {
            "vasaloppet" => 90.0,
            "vasaloppet 90" => 90.0,
            "vasaloppet 45" => 45,
            "vasaloppet 30" => 30,
            "vasaloppet 10" => 10.0,
            "halvvasan" => 45,
            "craft" => 42.0,
            "craft ski marathon" => 42.0,
            "sya" => 40.0,
            "k-byggslingan" => 40.0,
            _ => 40.0
        };
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