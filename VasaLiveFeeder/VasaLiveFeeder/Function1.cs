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
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        // Try to get raceName/progressInKm from query string first (manual parse)
        string raceName = null;
        string progressStr = null;
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
                            if (root.TryGetProperty("dryRun", out var jDry)) dryRun = jDry.GetBoolean();
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
             newSpeed = await DeriveTempoDelta(raceName, progressStr, currentSpeedStr, dryRun);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error deriving tempo delta for race {RaceName} at {Progress} km", raceName, progressInKm);
            return await CreateJsonResponse(req, new { error = e.Message, details = e.ToString() }, HttpStatusCode.InternalServerError);
        }

        // return parsed data as JSON
        var result = new { newSpeed = newSpeed };

        return await CreateJsonResponse(req, result, HttpStatusCode.OK);
    }

    public async Task<double> DeriveDistanceDelta(string raceName, double myProgress, bool dryRun = false)
    {
        if (dryRun)
        {
            // Dry run mode: Simulate a race starting at the top of each hour
            // Leader pace: 4.7619 m/s (17.143 km/h or 3.5 min/km)
            const double leaderPaceMetersPerSecond = 4.7619;

            var now = DateTime.UtcNow;
            var minutesSinceHour = now.Minute + (now.Second / 60.0);
            var raceTimeMinutes = minutesSinceHour;
            var leaderDistanceKm = (leaderPaceMetersPerSecond * raceTimeMinutes * 60) / 1000.0;

            _logger.LogInformation("DRY RUN: Current time {Time}, minutes since hour: {Minutes}, leader at {Distance} km", 
                now.ToString("HH:mm:ss"), raceTimeMinutes, leaderDistanceKm);

            var progressPlusFiftyPercent = myProgress * 1.5;
            var distanceDelta = leaderDistanceKm - progressPlusFiftyPercent;

            if (distanceDelta < 0)
            {
                if (dryRun)
                {
                    return 0;
                }
                throw  new InvalidOperationException($"Distance delta cannot be negative. Leader: {leaderDistanceKm:F2} km, My progress + 50%: {progressPlusFiftyPercent:F2} km");
            }

            return distanceDelta;
        }

        var scraper = ServiceLocator.Resolve<ILiveScraper>();

        // debugUrl = "https://live.eqtiming.com/73153#result:297321-0-1308925-1-1-";
        var url = GetRaceUrl(raceName);

        _logger.LogInformation("Attempting to scrape race URL: {Url}", url);

        // Log environment variables for debugging
        var scraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");
        var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

        _logger.LogInformation("Environment - SCRAPER_SERVICE_URL: {ScraperServiceUrl}", scraperServiceUrl ?? "(not set)");
        _logger.LogInformation("Environment - BROWSERLESS_TOKEN: {HasToken}", !string.IsNullOrWhiteSpace(browserlessToken) ? "present" : "(not set)");
        _logger.LogInformation("Environment - GROQ_API_KEY: {HasKey}", !string.IsNullOrWhiteSpace(groqApiKey) ? "present" : "(not set)");

        // Validate configuration
        var missingConfig = new List<string>();
        if (string.IsNullOrWhiteSpace(scraperServiceUrl))
            missingConfig.Add("SCRAPER_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(groqApiKey))
            missingConfig.Add("GROQ_API_KEY");

        if (missingConfig.Any())
        {
            var errorMsg = $"Missing required configuration: {string.Join(", ", missingConfig)}. " +
                          "Configure these in Azure Function App Settings or local.settings.json. " +
                          "See PlaywrightScraper/DEPLOYMENT.md for setup instructions.";
            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        var leaderData = await scraper.GetLeaderDataWithScraperAsync(url);

        if (leaderData == null)
        {
            // More specific error message based on configuration
            var configErrors = new List<string>();

            if (string.IsNullOrWhiteSpace(scraperServiceUrl))
                configErrors.Add("SCRAPER_SERVICE_URL not configured");

            if (string.IsNullOrWhiteSpace(groqApiKey))
                configErrors.Add("GROQ_API_KEY not configured");

            string errorMsg;
            if (configErrors.Any())
            {
                errorMsg = $"Configuration error: {string.Join(", ", configErrors)}. " +
                          "Set these in Azure Function App Settings.";
            }
            else
            {
                errorMsg = $"Failed to extract leader data from race page: {url}. " +
                          "Possible reasons:\n" +
                          $"1. Scraper service ({scraperServiceUrl}) may be unreachable - test: {scraperServiceUrl}/health\n" +
                          "2. The race may not have started yet (no leader data available)\n" +
                          "3. The race page format may have changed\n" +
                          "4. AI extraction failed - check GROQ_API_KEY validity and credits\n" +
                          "5. Network connectivity issues\n" +
                          "Check Azure Function logs (Application Insights) for detailed error messages.";
            }

            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        _logger.LogInformation("Successfully scraped leader data: {Distance} km, Time: {Time}", leaderData.DistanceKm, leaderData.ElapsedTime);

        var leaderKm = leaderData.DistanceKm;
        var ourPlus50Percent = myProgress * 1.5;
        var delta = leaderKm - ourPlus50Percent;

        return delta;
    }

    public async Task<double> DeriveTempoDelta(string raceName, string myProgressStr, string meanSpeedStr, bool dryRun = false)
    {
        var meanSpeed = Speed.FromKilometersPerHour(double.Parse(meanSpeedStr, CultureInfo.InvariantCulture));

        var myProgress = double.Parse(myProgressStr, CultureInfo.InvariantCulture);
        var totalDistance = GetTotalDistance(raceName);

        var behindInKm = await DeriveDistanceDelta(raceName, myProgress, dryRun);

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

        return double.Round(requiredSpeed.MinutesPerKilometer, 2);
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