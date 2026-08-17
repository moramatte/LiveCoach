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
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Function1(ILogger<Function1> logger)
    {
        _logger = logger;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
    {
        // Lightweight health check to keep function warm
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync("OK");
        return response;
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
        string medalTimePctStr = null;
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
                    if (key == "medaltimepct" || key == "medalpct" || key == "medal") medalTimePctStr = val;
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
                            if (root.TryGetProperty("medalTimePct", out var jMedal)) medalTimePctStr ??= jMedal.GetRawText().Trim('"');
                            if (root.TryGetProperty("medalPct", out var jMedal2)) medalTimePctStr ??= jMedal2.GetRawText().Trim('"');
                            if (root.TryGetProperty("medal", out var jMedal3)) medalTimePctStr ??= jMedal3.GetRawText().Trim('"');
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

        if (string.IsNullOrWhiteSpace(raceName) || string.IsNullOrWhiteSpace(progressStr) || 
            (string.IsNullOrWhiteSpace(elapsedTimeStr) && !dryRun))
        {
            _logger.LogWarning("Required parameters missing. Race='{Race}', Progress='{Progress}', Elapsed='{Elapsed}', DryRun={DryRun}", 
                raceName, progressStr, elapsedTimeStr, dryRun);
            var message = "Provide race name, progress in km, and elapsed time in minutes (query: ?raceName=..&progressInKm=..&elapsed=.. or JSON body { \\\"raceName\\\":.., \\\"progressInKm\\\":.., \\\"elapsed\\\":.. }).";
            return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
        }

        if (!double.TryParse(progressStr.Trim().Trim('"'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var progressInKm))
        {
            _logger.LogWarning("Failed to parse progress. Progress='{ProgressStr}'", progressStr);
            var message = "Invalid progress value. Provide a numeric value (e.g. 42.5).";
            return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
        }

        // Parse medalTimePct (default to 50% if not provided)
        double medalTimePct = 50.0;
        if (!string.IsNullOrWhiteSpace(medalTimePctStr))
        {
            if (!double.TryParse(medalTimePctStr.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out medalTimePct))
            {
                _logger.LogWarning("Failed to parse medalTimePct. MedalTimePct='{MedalTimePctStr}'", medalTimePctStr);
                var message = "Invalid medalTimePct value. Provide a numeric percentage value (e.g. 50 for 50%).";
                return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
            }
            if (medalTimePct < 0 || medalTimePct > 500)
            {
                _logger.LogWarning("medalTimePct out of range. MedalTimePct={MedalTimePct}", medalTimePct);
                var message = "medalTimePct must be between 0 and 500 (percent).";
                return await CreateJsonResponse(req, message, HttpStatusCode.BadRequest);
            }
        }

        double newSpeed;
        double leaderDistanceKm;
        string? leaderName;
        bool live;
        try
        {
            var (pace, leaderDistance, scrapedLeaderName, isLive) = await DeriveTempoDelta(raceName, progressStr, elapsedTimeStr, medalTimePct, dryRun);
            newSpeed = pace;
            leaderDistanceKm = leaderDistance;
            leaderName = scrapedLeaderName;
            live = isLive;
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("No race data available"))
        {
            return await CreateJsonResponse(req, new { error = e.Message }, HttpStatusCode.NotFound);
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

        // Convert pace from decimal minutes to M:SS format
        var paceFormatted = FormatPaceAsMinutesSeconds(newSpeed);
        var result = new
        {
            newSpeed = paceFormatted,
            leaderDistanceKm = leaderDistanceKm,
            leaderName = leaderName,
            live = live
        };

        return await CreateJsonResponse(req, result, HttpStatusCode.OK);
    }

    public async Task<(double leaderDistanceKm, TimeSpan leaderElapsedTime, string? leaderName, bool isLive)> GetLeaderDataAsync(string raceName, bool dryRun = false, double userElapsedTimeMinutes = 0)
    {
        if (string.IsNullOrWhiteSpace(raceName))
        {
            throw new ArgumentException("Race name cannot be null or empty", nameof(raceName));
        }

        if (dryRun)
        {
            // Dry run mode: Simulate leader based on user's elapsed time
            // Leader pace: 2:18 min/km (2 minutes 18 seconds per km) = 26.09 km/h
            const double leaderPaceMinPerKm = 2.3; // 2.3 minutes = 2 min 18 sec

            var leaderDistanceKm = userElapsedTimeMinutes / leaderPaceMinPerKm;
            var leaderElapsedTime = TimeSpan.FromMinutes(userElapsedTimeMinutes);

            _logger.LogInformation("DRY RUN: Using user's elapsed time ({Elapsed} min) to simulate leader at {Distance} km (pace: 2:18 min/km)",
                userElapsedTimeMinutes, leaderDistanceKm);

            return (leaderDistanceKm, leaderElapsedTime, null, false);
        }

        // Create scraper with logger for Application Insights integration
        var loggerFactory = LoggerFactory.Create(builder => builder.AddApplicationInsights());
        var scraperLogger = loggerFactory.CreateLogger<LiveScraper.LiveScraper>();
        var scraper = new LiveScraper.LiveScraper(_httpClient, scraperLogger);
        var url = GetRaceUrl(raceName);

        if (url == null)
        {
            _logger.LogInformation("No race data available yet for '{RaceName}'.", raceName);
            throw new InvalidOperationException($"No race data available yet for '{raceName}'.");
        }

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
            _logger.LogWarning("Failed to extract leader data from race page: {Url}. Falling back to simulation mode.", url);
            _logger.LogWarning("Possible reasons: 1. Scraper service ({ScraperServiceUrl}) may be unreachable, " +
                              "2. The race may not have started yet, " +
                              "3. The race page format may have changed, " +
                              "4. AI extraction failed", scraperServiceUrl);

            // Fallback: Simulate leader based on user's elapsed time and 2:18 min/km pace
            // Assume leader has been going at 2:18 min/km pace since race start
            const double leaderPaceMinPerKm = 2.3; // 2.3 minutes = 2 min 18 sec

            // Use user's elapsed time to calculate where leader would be
            var leaderDistanceKm = userElapsedTimeMinutes / leaderPaceMinPerKm;
            var leaderElapsedTime = TimeSpan.FromMinutes(userElapsedTimeMinutes);

            _logger.LogInformation("FALLBACK MODE: Using user's elapsed time ({UserElapsed} min) to simulate leader at {Distance} km (pace: 2:18 min/km)",
                userElapsedTimeMinutes, leaderDistanceKm);

            return (leaderDistanceKm, leaderElapsedTime, null, false);
        }

        _logger.LogInformation("Successfully scraped leader data: {Distance} km, Time: {Time}",
            leaderData.DistanceKm, leaderData.ElapsedTime);

        // Convert nullable TimeSpan to non-nullable (use distance/2.3 pace if time is missing)
        var leaderTime = leaderData.ElapsedTime ?? TimeSpan.FromMinutes(leaderData.DistanceKm * 2.3);

        return (leaderData.DistanceKm, leaderTime, leaderData.LeaderName, true);
    }

    public async Task<(double requiredPaceMinPerKm, double leaderDistanceKm, string? leaderName, bool isLive)> DeriveTempoDelta(string raceName, string myProgressStr, string elapsedTimeStr, double medalTimePct = 50.0, bool dryRun = false)
    {
        var myProgress = double.Parse(myProgressStr, CultureInfo.InvariantCulture);
        var totalDistance = GetTotalDistance(raceName);

        // Parse user's elapsed time (optional when dryRun=true)
        double myElapsedTimeMinutes;
        if (string.IsNullOrWhiteSpace(elapsedTimeStr))
        {
            if (!dryRun)
            {
                throw new ArgumentException("Elapsed time is required when dryRun is false. Provide elapsedTime parameter (in minutes).", nameof(elapsedTimeStr));
            }
            // For dryRun mode without elapsed time, use progress and a default pace to estimate elapsed time
            // Assume user is going at 5 min/km pace
            myElapsedTimeMinutes = myProgress * 5.0;
            _logger.LogInformation("[DryRun] No elapsed time provided. Estimating {Elapsed} minutes based on {Progress} km at 5 min/km pace", 
                myElapsedTimeMinutes, myProgress);
        }
        else
        {
            if (!double.TryParse(elapsedTimeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out myElapsedTimeMinutes))
            {
                throw new ArgumentException($"Invalid elapsed time value '{elapsedTimeStr}'. Must be a valid number in minutes.", nameof(elapsedTimeStr));
            }
        }

        _logger.LogWarning("[DEVICE_PARAMS] race={Race}, km={Km}, elapsed={Elapsed}, medalTimePct={MedalTimePct}%, dryRun={DryRun}", 
            raceName, myProgressStr, elapsedTimeStr ?? "(estimated)", medalTimePct, dryRun);


        var (leaderDistanceKm, leaderElapsedTime, leaderName, isLive) = await GetLeaderDataAsync(raceName, dryRun, myElapsedTimeMinutes);

        // Validate elapsed time - must be non-negative and realistic
        if (myElapsedTimeMinutes < 0 || double.IsNaN(myElapsedTimeMinutes) || double.IsInfinity(myElapsedTimeMinutes))
        {
            _logger.LogWarning("Invalid elapsed time: {ElapsedTime}. Must be >= 0. Returning default pace with live leader data.", 
                myElapsedTimeMinutes);
            // Return default pace but include actual leader distance and live status
            return (5.0, leaderDistanceKm, leaderName, isLive);
        }

        // Validate leader data
        if (leaderDistanceKm <= 0 || leaderElapsedTime.TotalMinutes <= 0)
        {
            _logger.LogWarning("Leader has not started yet or invalid leader data. Distance: {Distance} km, Time: {Time}", 
                leaderDistanceKm, leaderElapsedTime);
            return (5.0, 0, leaderName, false);
        }

        // Calculate target finishing time based on medalTimePct (e.g., 50% means leader's time + 50%)
        // medalTimePct is in percent (50 = 50%), so multiplier is 1 + (medalTimePct / 100)
        var timeMultiplier = 1.0 + (medalTimePct / 100.0);
        TimeSpan targetFinishTime;

        if (leaderDistanceKm >= totalDistance)
        {
            // Leader has finished - we have exact target time
            targetFinishTime = TimeSpan.FromSeconds(leaderElapsedTime.TotalSeconds * timeMultiplier);
            _logger.LogInformation("Leader finished in {LeaderTime}. Target time ({MedalTimePct}%): {TargetTime}", 
                leaderElapsedTime.ToString(@"hh\:mm\:ss"),
                medalTimePct,
                targetFinishTime.ToString(@"hh\:mm\:ss"));
        }
        else
        {
            // Leader still racing - extrapolate their finishing time
            var leaderMeanPaceMinPerKm = leaderElapsedTime.TotalMinutes / leaderDistanceKm;
            var leaderEstimatedFinishTime = TimeSpan.FromMinutes(totalDistance * leaderMeanPaceMinPerKm);
            targetFinishTime = TimeSpan.FromSeconds(leaderEstimatedFinishTime.TotalSeconds * timeMultiplier);

            _logger.LogInformation("Leader at {LeaderDist} km in {LeaderTime} (pace: {LeaderPace:F2} min/km). Estimated finish: {EstFinish}. Target time ({MedalTimePct}%): {TargetTime}",
                leaderDistanceKm,
                leaderElapsedTime.ToString(@"hh\:mm\:ss"),
                leaderMeanPaceMinPerKm,
                leaderEstimatedFinishTime.ToString(@"hh\:mm\:ss"),
                medalTimePct,
                targetFinishTime.ToString(@"hh\:mm\:ss"));
        }

        // Calculate required pace for remaining distance
        var distanceRemaining = totalDistance - myProgress;

        if (distanceRemaining <= 0)
        {
            _logger.LogWarning("Already at or past finish line");
            return (0, leaderDistanceKm, leaderName, isLive);
        }

        // Calculate time remaining and required pace (works for km=0 and km>0)
        var actualTimeRemaining = Math.Max(0, targetFinishTime.TotalMinutes - myElapsedTimeMinutes);
        var requiredPaceMinPerKm = actualTimeRemaining / distanceRemaining;

        // Calculate current pace for logging (if user has moved)
        var currentPace = myProgress > 0 ? myElapsedTimeMinutes / myProgress : 0;

        if (double.IsInfinity(requiredPaceMinPerKm) || double.IsNaN(requiredPaceMinPerKm) || requiredPaceMinPerKm <= 0)
        {
            requiredPaceMinPerKm = currentPace > 0 ? currentPace : 5.0;
        }

        _logger.LogInformation(
            "Progress: {Progress} km / {Total} km. Elapsed: {Elapsed}. Current pace: {CurrentPace:F2} min/km. Required pace: {RequiredPace:F2} min/km. Time remaining: {TimeRemaining}",
            myProgress, totalDistance, TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"),
            currentPace, requiredPaceMinPerKm, TimeSpan.FromMinutes(actualTimeRemaining).ToString(@"hh\:mm\:ss"));

        _logger.LogInformation("Progress: {Progress} km / {Total} km. Elapsed: {Elapsed}. Required pace: {RequiredPace:F2} min/km. Time remaining: {TimeRemaining}",
            myProgress, totalDistance, TimeSpan.FromMinutes(myElapsedTimeMinutes).ToString(@"hh\:mm\:ss"),
            requiredPaceMinPerKm, TimeSpan.FromMinutes(actualTimeRemaining).ToString(@"hh\:mm\:ss"));

        return (Math.Round(requiredPaceMinPerKm, 2), Math.Round(leaderDistanceKm, 2), leaderName, isLive);
    }

    private double GetTotalDistance(string raceName)
    {
        return raceName.ToLower() switch
        {
            // Season XVIII full calendar
            "bad-gastein-prologue"      => 10.0,
            "sportgastein-criterium"    => 20.0,
            "bad-gastein-itt-2"         => 10.0,
            "bad-gastein-criterium"     => 20.0,
            "engadin-la-diagonela"      => 47.0,
            "zuoz-st-moritz-sprint"     => 10.0,
            "marcialonga"               => 70.0,
            "bedrichov-sprint"          => 10.0,
            "jizerska-padesatka"        => 50.0,
            "oxberg-mora-sprint-women"  => 10.0,
            "oxberg-mora-sprint-men"    => 10.0,
            "vasaloppet"                => 90.0,
            "birkebeinerrennet"         => 53.0,
            "reistadlopet"              => 60.0,
            "summit-2-senja"            => 40.0,
            // Legacy/test slugs
            "birken"                    => 53.0,
            "halvvasan"                 => 45.0,
            "ladiagonela"               => 47.0,
            "craft"                     => 42.0,
            "craft ski marathon"        => 42.0,
            "finlandia"                 => 42.0,
            "moraloppet"                => 90.0,
            "mora"                      => 90.0,
            "mora25"                    => 25.0,
            "zelta"                     => 50.0,
            "sya"                       => 40.0,
            "k-byggslingan"             => 40.0,
            "test10k"                   => 10.0,
            "test20k"                   => 20.0,
            _                           => 40.0
        };
    }

    private string? GetRaceUrl(string raceName)
    {
        if (string.IsNullOrWhiteSpace(raceName))
        {
            throw new ArgumentException("Race name cannot be null or empty", nameof(raceName));
        }

        var raceBaseUrl = raceName.ToLower() switch
        {
            // Season XVIII full calendar
            "bad-gastein-prologue"      => null,
            "sportgastein-criterium"    => "https://skiclassics.com/results/?race_id=1528",
            "bad-gastein-itt-2"         => null,
            "bad-gastein-criterium"     => null,
            "engadin-la-diagonela"      => "https://skiclassics.com/live-center/?event=9620&season=2026&gender=men",
            "zuoz-st-moritz-sprint"     => null,
            "marcialonga"               => "https://skiclassics.com/results/?race_id=1534",
            "bedrichov-sprint"          => null,
            "jizerska-padesatka"        => "https://skiclassics.com/results/?race_id=1544",
            "oxberg-mora-sprint-women"  => null,
            "oxberg-mora-sprint-men"    => null,
            "vasaloppet"                => "https://skiclassics.com/live-center/?event=1264&season=2026&gender=men",
            "birkebeinerrennet"         => "https://skiclassics.com/live-center/?event=1265&season=2026&gender=men",
            "reistadlopet"              => "https://skiclassics.com/results/?race_id=1576",
            "summit-2-senja"            => null,
            // Legacy/test slugs
            "birken"                    => "https://skiclassics.com/live-center/?event=1265&season=2026&gender=men",
            "ladiagonela"               => "https://skiclassics.com/live-center/?event=9620&season=2026&gender=men",
            "moraloppet"                => "https://live.eqtiming.com/76514",
            "mora"                      => "https://live.eqtiming.com/76514",
            "mora25"                    => "https://live.eqtiming.com/73153",
            "craft"                     => "https://live.eqtiming.com/73152",
            "craft ski marathon"        => "https://live.eqtiming.com/73152",
            "finlandia"                 => "https://skiclassics.com/live-center/?event=12066&season=2026&gender=men",
            "zelta"                     => "https://skiclassics.com/live-center/?event=8338&season=2026&gender=men",
            "test10k"                   => "_",
            "test20k"                   => "_",
            _                           => throw new ArgumentException($"Unknown race name '{raceName}'.", nameof(raceName))
        };

        if (raceBaseUrl != null && raceBaseUrl.Contains("eqtiming"))
        {
            return $"{raceBaseUrl}#result";
        }

        return raceBaseUrl;
    }

    private static string FormatPaceAsMinutesSeconds(double paceMinPerKm)
    {
        var minutes = (int)paceMinPerKm;
        var seconds = (int)Math.Round((paceMinPerKm - minutes) * 60);

        // Handle edge case where rounding causes 60 seconds
        if (seconds >= 60)
        {
            minutes += 1;
            seconds = 0;
        }

        return $"{minutes}:{seconds:D2}";
    }

    private static async Task<HttpResponseData> CreateJsonResponse(HttpRequestData req, object value, HttpStatusCode status)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        resp.Headers.Add("Connection", "keep-alive");
        resp.Headers.Add("Keep-Alive", "timeout=60, max=100");

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(value, options);
        var bytes = Encoding.UTF8.GetBytes(json);
        await resp.Body.WriteAsync(bytes, 0, bytes.Length);
        return resp;
    }
}