using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Infrastructure;
using Infrastructure.Extensions;
using Infrastructure.Logger;
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
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        // Try to get raceName/progressInKm from query string first (manual parse)
        string raceName = null;
        string progressStr = null;
        string elapsedTimeStr = null;
        string currentSpeedStr = null;
        bool dryRun = false;
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
                    if (key == "elapsedtime" || key == "elapsed" || key == "time") elapsedTimeStr = val;
                    if (key == "currentspeed" || key == "speed") currentSpeedStr = val;
                    if (key == "dryrun") dryRun = val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
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
                            if (root.TryGetProperty("elapsedTime", out var jTime)) elapsedTimeStr ??= jTime.GetRawText().Trim('"');
                            if (root.TryGetProperty("elapsed", out var jTime2)) elapsedTimeStr ??= jTime2.GetRawText().Trim('"');
                            if (root.TryGetProperty("time", out var jTime3)) elapsedTimeStr ??= jTime3.GetRawText().Trim('"');
                            if (root.TryGetProperty("currentSpeed", out var jSpeed)) currentSpeedStr ??= jSpeed.GetRawText().Trim('"');
                            if (root.TryGetProperty("speed", out var jSpeed2)) currentSpeedStr ??= jSpeed2.GetRawText().Trim('"');
                            if (root.TryGetProperty("dryRun", out var jDry)) dryRun = jDry.GetBoolean();
                        }
                        catch (JsonException) { /* ignore parse errors below */ }
                    }
                    else if (body.Contains(","))
                    {
                        var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            elapsedTimeStr ??= parts[2].Trim();
                            currentSpeedStr ??= parts[3].Trim();
                        }
                        else if (parts.Length >= 3)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            elapsedTimeStr ??= parts[2].Trim();
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
                        if (parts.Length >= 4)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            elapsedTimeStr ??= parts[2].Trim();
                            currentSpeedStr ??= parts[3].Trim();
                        }
                        else if (parts.Length >= 3)
                        {
                            raceName ??= parts[0].Trim();
                            progressStr ??= parts[1].Trim();
                            elapsedTimeStr ??= parts[2].Trim();
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
        double leaderDistanceKm;
        try
        {
            var (pace, leaderDistance) = await DeriveTempoDelta(raceName, progressStr, elapsedTimeStr, currentSpeedStr, dryRun);
            newSpeed = pace; // pace in min/km
            leaderDistanceKm = leaderDistance;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error deriving tempo delta for race {RaceName} at {Progress} km", raceName, progressInKm);
            return await CreateJsonResponse(req, new { error = e.Message, details = e.ToString() }, HttpStatusCode.InternalServerError);
        }

        // return parsed data as JSON
        if (double.IsInfinity(newSpeed) || double.IsNaN(newSpeed))
        {
            Log.Error(GetType(), "Derived new speed is infinity or NaN");
            newSpeed = 0.1;
        }
        var result = new { newSpeed = newSpeed, leaderDistanceKm = leaderDistanceKm };

        return await CreateJsonResponse(req, result, HttpStatusCode.OK);
    }

    public async Task<(double leaderDistanceKm, TimeSpan? leaderElapsedTime)> GetLeaderDataAsync(string raceName, bool dryRun = false)
    {
        if (dryRun)
        {
            // Dry run mode: Simulate a race starting every 30 minutes (resets at :00 and :30)
            // Leader pace: 3:00 min/km (3 minutes per km) = 20 km/h
            const double leaderPaceMinPerKm = 3.0;

            var now = DateTime.UtcNow;
            var minutesSinceLast30 = now.Minute % 30 + (now.Second / 60.0);
            var raceTimeMinutes = minutesSinceLast30;
            var leaderDistanceKm = raceTimeMinutes / leaderPaceMinPerKm;
            var leaderElapsedTime = TimeSpan.FromMinutes(raceTimeMinutes);

            _logger.LogInformation("DRY RUN: Current time {Time}, minutes since last 30-min mark: {Minutes}, leader at {Distance} km (pace: 3:00 min/km)", 
                now.ToString("HH:mm:ss"), raceTimeMinutes, leaderDistanceKm);

            return (leaderDistanceKm, leaderElapsedTime);
        }

        var scraper = ServiceLocator.Resolve<ILiveScraper>();
        var url = GetRaceUrl(raceName);

        _logger.LogInformation("Attempting to scrape race URL: {Url}", url);

        // Log and validate configuration
        var scraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

        _logger.LogInformation("Environment - SCRAPER_SERVICE_URL: {ScraperServiceUrl}", scraperServiceUrl ?? "(not set)");
        _logger.LogInformation("Environment - GROQ_API_KEY: {HasKey}", !string.IsNullOrWhiteSpace(groqApiKey) ? "present" : "(not set)");

        var missingConfig = new List<string>();
        if (string.IsNullOrWhiteSpace(scraperServiceUrl))
            missingConfig.Add("SCRAPER_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(groqApiKey))
            missingConfig.Add("GROQ_API_KEY");

        if (missingConfig.Any())
        {
            var errorMsg = $"Missing required configuration: {string.Join(", ", missingConfig)}. " +
                          "Configure these in Azure Function App Settings or local.settings.json.";
            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        var leaderData = await scraper.GetLeaderDataWithScraperAsync(url);

        if (leaderData == null)
        {
            var errorMsg = $"Failed to extract leader data from race page: {url}. " +
                          "Possible reasons:\n" +
                          $"1. Scraper service ({scraperServiceUrl}) may be unreachable\n" +
                          "2. The race may not have started yet\n" +
                          "3. The race page format may have changed\n" +
                          "4. AI extraction failed\n" +
                          "Check Azure Function logs for details.";
            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        _logger.LogInformation("Successfully scraped leader data: {Distance} km, Time: {Time}", 
            leaderData.DistanceKm, leaderData.ElapsedTime);

        return (leaderData.DistanceKm, leaderData.ElapsedTime);
    }

    public async Task<(double requiredPaceMinPerKm, double leaderDistanceKm)> DeriveTempoDelta(string raceName, string myProgressStr, string elapsedTimeStr, string currentSpeedStr = null, bool dryRun = false)
    {
        var myProgress = double.Parse(myProgressStr, CultureInfo.InvariantCulture);
        var totalDistance = GetTotalDistance(raceName);

        // Get leader's current data
        var (leaderDistanceKm, leaderElapsedTime) = await GetLeaderDataAsync(raceName, dryRun);

        // Calculate target finishing time (leader's time + 50%)
        TimeSpan targetFinishTime;

        if (leaderDistanceKm >= totalDistance)
        {
            // Leader has finished - we have exact target time
            if (leaderElapsedTime.HasValue)
            {
                targetFinishTime = TimeSpan.FromSeconds(leaderElapsedTime.Value.TotalSeconds * 1.5);
                _logger.LogInformation("Leader finished in {LeaderTime}. Target time: {TargetTime}", 
                    leaderElapsedTime.Value.ToString(@"hh\:mm\:ss"), 
                    targetFinishTime.ToString(@"hh\:mm\:ss"));
            }
            else
            {
                // No time data, estimate based on 3:00 min/km pace
                targetFinishTime = TimeSpan.FromMinutes(totalDistance * 3.0 * 1.5);
                _logger.LogWarning("Leader finished but no time available, using estimated target: {TargetTime}", 
                    targetFinishTime.ToString(@"hh\:mm\:ss"));
            }
        }
        else
        {
            // Leader still racing - extrapolate their finishing time
            if (leaderElapsedTime.HasValue && leaderDistanceKm > 0)
            {
                var leaderMeanPaceMinPerKm = leaderElapsedTime.Value.TotalMinutes / leaderDistanceKm;
                var leaderEstimatedFinishTime = TimeSpan.FromMinutes(totalDistance * leaderMeanPaceMinPerKm);
                targetFinishTime = TimeSpan.FromSeconds(leaderEstimatedFinishTime.TotalSeconds * 1.5);

                _logger.LogInformation("Leader at {LeaderDist} km in {LeaderTime} (pace: {LeaderPace:F2} min/km). Estimated finish: {EstFinish}. Target time: {TargetTime}",
                    leaderDistanceKm,
                    leaderElapsedTime.Value.ToString(@"hh\:mm\:ss"),
                    leaderMeanPaceMinPerKm,
                    leaderEstimatedFinishTime.ToString(@"hh\:mm\:ss"),
                    targetFinishTime.ToString(@"hh\:mm\:ss"));
            }
            else
            {
                // No time data, estimate
                targetFinishTime = TimeSpan.FromMinutes(totalDistance * 3.0 * 1.5);
                _logger.LogWarning("No leader time data, using estimated target: {TargetTime}", 
                    targetFinishTime.ToString(@"hh\:mm\:ss"));
            }
        }

        // Calculate required pace for remaining distance
        var distanceRemaining = totalDistance - myProgress;

        if (distanceRemaining <= 0)
        {
            _logger.LogWarning("Already at or past finish line");
            return (0, leaderDistanceKm);
        }

        // Determine my elapsed time - prefer direct value, fallback to estimating from speed
        double myElapsedTimeMinutes;

        if (!string.IsNullOrWhiteSpace(elapsedTimeStr) && 
            double.TryParse(elapsedTimeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsedMinutes))
        {
            // Use provided elapsed time (in minutes)
            myElapsedTimeMinutes = elapsedMinutes;
            var currentPace = myProgress > 0 ? myElapsedTimeMinutes / myProgress : 0;
            _logger.LogInformation("Using provided elapsed time: {ElapsedTime} ({Pace:F2} min/km pace)",
                TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"), currentPace);
        }
        else if (!string.IsNullOrWhiteSpace(currentSpeedStr) && 
                 double.TryParse(currentSpeedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var speedMps))
        {
            // Fallback: estimate from current speed (m/s to min/km)
            var currentPaceMinPerKm = 1000.0 / (speedMps * 60.0);
            myElapsedTimeMinutes = myProgress * currentPaceMinPerKm;
            _logger.LogInformation("Estimated elapsed time from speed: {ElapsedTime} ({Pace:F2} min/km pace)",
                TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"), currentPaceMinPerKm);
        }
        else
        {
            // No time data available, assume average 5:00 min/km pace
            myElapsedTimeMinutes = myProgress * 5.0;
            _logger.LogWarning("No elapsed time or speed provided, assuming 5:00 min/km pace: {ElapsedTime}",
                TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"));
        }

        var actualTimeRemaining = Math.Max(0, targetFinishTime.TotalMinutes - myElapsedTimeMinutes);
        var requiredPaceMinPerKm = actualTimeRemaining / distanceRemaining;

        if (double.IsInfinity(requiredPaceMinPerKm) || double.IsNaN(requiredPaceMinPerKm) || requiredPaceMinPerKm <= 0)
        {
            requiredPaceMinPerKm = myProgress > 0 ? myElapsedTimeMinutes / myProgress : 5.0;
        }

        _logger.LogInformation("Progress: {Progress} km / {Total} km. Elapsed: {Elapsed}. Required pace: {RequiredPace:F2} min/km. Time remaining: {TimeRemaining}",
            myProgress, totalDistance, TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"),
            requiredPaceMinPerKm, TimeSpan.FromMinutes(actualTimeRemaining).ToString(@"hh\:mm\:ss"));

        return (Math.Round(requiredPaceMinPerKm, 2), Math.Round(leaderDistanceKm, 2));
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
            "ladiagonela" => 47.0,
            "craft" => 42.0,
            "test10k" => 10.0,
            "test20k" => 20.0,
            "craft ski marathon" => 42.0,
            "sya" => 40.0,
            "k-byggslingan" => 40.0,
            _ => 40.0
        };
    }

    private string GetRaceUrl(string raceName)
    {
        var raceBaseUrl =  raceName.ToLower() switch
        {
            "vasaloppet" => "https://live.eqtiming.com/76514",
            "vasaloppet 90" => "https://live.eqtiming.com/76514",
            "moraloppet" => "https://live.eqtiming.com/76514",
            "mora" => "https://live.eqtiming.com/76514",
            "mora25" => "https://live.eqtiming.com/73153",
            "craft" => "https://live.eqtiming.com/73152",
            "craft ski marathon" => "https://live.eqtiming.com/73152",
            "ladiagonela" => "https://skiclassics.com/live-center/?event=9620&season=2026&gender=men",
            "test10k" => "_",
            "test20k" => "_",
            _ => throw new Exception($"No race url defined for {raceName}")
        };

        if (raceBaseUrl.Contains("eqtiming"))
        {
            return $"{raceBaseUrl}#result";
        }

        return  raceBaseUrl;
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