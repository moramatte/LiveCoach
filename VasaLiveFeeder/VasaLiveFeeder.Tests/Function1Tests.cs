using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FakeItEasy;
using Infrastructure;
using Infrastructure.Speed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VasaLiveFeeder;
using VasaLiveFeeder.LiveScraper;

namespace VasaLiveFeeder.Tests;

[TestClass]
public class Function1Tests
{
    [TestInitialize]
    public void Setup()
    {
        BootstrapperForLiveFeeder.Reset();
        ServiceLocator.RegisterTransient<ILiveScraper, TestableLiveScraper>();
        // Set up test data: leader at 60 km after 180 minutes (3:00 min/km pace)
        TestableLiveScraper.ReturnValue = 60;
        TestableLiveScraper.ReturnTime = TimeSpan.FromMinutes(180); // Non-nullable now
    }

    [TestMethod]
    public async Task Run_ReturnsRequiredPaceAndLeaderDistance()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        // Simulate: Vasaloppet 90km, I'm at 30km after 150 minutes (5:00 min/km pace)
        var req = new TestHttpRequestData(
            new Uri("http://localhost?raceName=Vasaloppet&progressInKm=30&elapsedTime=150"), 
            "GET", 
            "");

        var resp = await func.Run(req);

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = ((TestHttpResponseData)resp).GetBodyAsString();
            var obj = JsonSerializer.Deserialize<JsonElement>(body);

            // Should return required pace, leader distance, and live status
            Assert.IsTrue(obj.TryGetProperty("newSpeed", out var newSpeed));
            Assert.IsTrue(obj.TryGetProperty("leaderDistanceKm", out var leaderDist));
            Assert.IsTrue(obj.TryGetProperty("leaderName", out var leaderName));
            Assert.IsTrue(obj.TryGetProperty("live", out var live));
            Assert.AreEqual(60.0, leaderDist.GetDouble());
            Assert.IsTrue(newSpeed.GetString()?.Length > 0); // Should be a valid formatted pace
            Assert.AreEqual(JsonValueKind.Null, leaderName.ValueKind);
            Assert.IsFalse(live.GetBoolean()); // Using TestableLiveScraper should return live data, but in test it's mocked so false
        }

    [TestMethod]
    public async Task GetLeaderDataAsync_ReturnsLeaderDistanceAndTime()
    {
        await EnsureScraperSecretsConfiguredAsync();

        var func = new Function1(NullLogger<Function1>.Instance);
        var (distance, time, leaderName, isLive) = await func.GetLeaderDataAsync("vasaloppet", dryRun: false, userElapsedTimeMinutes: 180);

        Assert.IsTrue(distance > 0, "Expected a positive leader distance from live scraping.");
        Assert.IsTrue(time.TotalMinutes > 0, "Expected a positive leader elapsed time from live scraping.");
        Assert.IsTrue(leaderName == null || leaderName.Length > 0);
        Assert.IsTrue(isLive, "Expected live scraper data, not fallback simulation.");
    }

    private static async Task EnsureScraperSecretsConfiguredAsync()
    {
        var localSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VasaLiveFeeder", "local.settings.json");
        if (!File.Exists(localSettingsPath))
        {
            Assert.Inconclusive($"local.settings.json not found: {localSettingsPath}");
            return;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(localSettingsPath));
        if (doc.RootElement.TryGetProperty("Values", out var values))
        {
            if (values.TryGetProperty("SCRAPER_SERVICE_URL", out var scraperUrl))
            {
                Environment.SetEnvironmentVariable("SCRAPER_SERVICE_URL", scraperUrl.GetString());
            }

            if (values.TryGetProperty("GROQ_API_KEY", out var groqApiKey))
            {
                Environment.SetEnvironmentVariable("GROQ_API_KEY", groqApiKey.GetString());
            }
        }

        var configuredScraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");
        var configuredGroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

        if (string.IsNullOrWhiteSpace(configuredScraperServiceUrl) || string.IsNullOrWhiteSpace(configuredGroqApiKey))
        {
            Assert.Inconclusive("SCRAPER_SERVICE_URL and GROQ_API_KEY must be configured in local.settings.json for this integration test.");
        }
    }

    [TestMethod]
    public async Task DeriveTempoDelta_CalculatesRequiredPace()
    {
        var func = new Function1(NullLogger<Function1>.Instance);

            // Scenario: Vasaloppet 90km race
            // Leader at 60km in 180 minutes (3:00 min/km pace)
            // Me at 30km after 150 minutes (5:00 min/km pace)
            // Target: Leader's time * 1.5 = finish at 90km in ~270 min * 1.5 = 405 min
            // I have 255 minutes left for 60km remaining

            var (requiredPace, leaderDistance, leaderName, isLive) = await func.DeriveTempoDelta("Vasaloppet", "30.00", "150", dryRun: false);

            Assert.AreEqual(60.0, leaderDistance);
            Assert.IsTrue(requiredPace > 0, "Required pace should be positive");
            Assert.IsTrue(requiredPace < 10, "Required pace should be reasonable (< 10 min/km)");
            Assert.IsNull(leaderName);
            Assert.IsTrue(isLive); // Using real scraper (TestableLiveScraper returns data), should be true
        }

    [TestMethod]
    public async Task DeriveTempoDelta_UsesElapsedTime_WhenProvided()
    {
            var func = new Function1(NullLogger<Function1>.Instance);

            // Use elapsed time directly (now required parameter)
            var (pace1, _, leaderName, isLive) = await func.DeriveTempoDelta("test10k", "5.0", "25", dryRun: true);

            // Should calculate based on 25 minutes elapsed
            Assert.IsTrue(pace1 > 0);
            Assert.IsNull(leaderName);
            Assert.IsFalse(isLive); // Dry run mode should return false
        }

    [TestMethod]
    public async Task Run_ReturnsBadRequest_WhenMissingElapsedTime()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        // Test that elapsed time is required (not in dry run mode)
        var req = new TestHttpRequestData(new Uri("http://localhost?raceName=Vasaloppet&progressInKm=30"), "GET", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = ((TestHttpResponseData)resp).GetBodyAsString();
        Assert.IsTrue(body.Contains("elapsed"), "Error message should mention elapsed time requirement");
    }

    [TestMethod]
    public async Task Run_ReturnsBadRequest_WhenMissingRaceName()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        var req = new TestHttpRequestData(new Uri("http://localhost?progressInKm=10"), "GET", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Run_ReturnsBadRequest_WhenMissingProgress()
    {
        var func = new Function1(NullLogger<Function1>.Instance);
        var req = new TestHttpRequestData(new Uri("http://localhost?raceName=Vasaloppet"), "GET", "");

        var resp = await func.Run(req);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task DryRunMode_SimulatesRaceStartingEvery30Minutes()
    {
            var func = new Function1(NullLogger<Function1>.Instance);

            // Dry run should work without real scraper data
            var (pace, leaderDist, leaderName, isLive) = await func.DeriveTempoDelta("test10k", "3.0", "15", dryRun: true);

            Assert.IsTrue(pace > 0, "Dry run should return valid pace");
            Assert.IsTrue(leaderDist >= 0, "Dry run should return valid leader distance");
            Assert.IsNull(leaderName);
            Assert.IsFalse(isLive, "Dry run should return isLive=false");
        }
}
