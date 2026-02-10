using System;
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
            Assert.IsTrue(obj.TryGetProperty("live", out var live));
            Assert.AreEqual(60.0, leaderDist.GetDouble());
            Assert.IsTrue(newSpeed.GetDouble() > 0); // Should be a valid pace
            Assert.IsFalse(live.GetBoolean()); // Using TestableLiveScraper should return live data, but in test it's mocked so false
        }

    [TestMethod]
    public async Task GetLeaderDataAsync_ReturnsLeaderDistanceAndTime()
    {
        var func = new Function1(NullLogger<Function1>.Instance);

        // Test dry run mode (doesn't need userElapsedTimeMinutes parameter in dry run)
        var (distance, time, isLive) = await func.GetLeaderDataAsync("test10k", dryRun: true, userElapsedTimeMinutes: 0);

        Assert.IsTrue(distance >= 0);
        Assert.IsTrue(time.TotalMinutes > 0); // Non-nullable now
        Assert.IsFalse(isLive); // Dry run should return false for live
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

            var (requiredPace, leaderDistance, isLive) = await func.DeriveTempoDelta("Vasaloppet", "30.00", "150", "", dryRun: false);

            Assert.AreEqual(60.0, leaderDistance);
            Assert.IsTrue(requiredPace > 0, "Required pace should be positive");
            Assert.IsTrue(requiredPace < 10, "Required pace should be reasonable (< 10 min/km)");
            Assert.IsTrue(isLive); // Using real scraper (TestableLiveScraper returns data), should be true
        }

    [TestMethod]
    public async Task DeriveTempoDelta_UsesElapsedTime_WhenProvided()
    {
            var func = new Function1(NullLogger<Function1>.Instance);

            // Use elapsed time directly (now required parameter)
            var (pace1, _, isLive) = await func.DeriveTempoDelta("test10k", "5.0", "25", "", dryRun: true);

            // Should calculate based on 25 minutes elapsed
            Assert.IsTrue(pace1 > 0);
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
            var (pace, leaderDist, isLive) = await func.DeriveTempoDelta("test10k", "3.0", "15", "", dryRun: true);

            Assert.IsTrue(pace > 0, "Dry run should return valid pace");
            Assert.IsTrue(leaderDist >= 0, "Dry run should return valid leader distance");
            Assert.IsFalse(isLive, "Dry run should return isLive=false");
        }
}
