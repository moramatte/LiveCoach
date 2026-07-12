using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VasaLiveFeeder.Tests;

[TestClass]
public class LiveScraperDeterministicTests
{
    [TestMethod]
    public async Task GetLeaderDataWithScraperAsync_UsesSkiClassicsSexField()
    {
        var api = new FakeSkiClassicsApiHandler(new Dictionary<string, FakeRace>
        {
            ["91001"] = new("1001", "M", 90.0, "4:14:45.5")
        });

        using var httpClient = new HttpClient(api);
        var scraper = new LiveScraper.LiveScraper(httpClient);

        var data = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=91001&season=2099&gender=men");

        Assert.IsNotNull(data);
        Assert.AreEqual(90.0, data.DistanceKm, 0.01);
        Assert.AreEqual(new TimeSpan(4, 14, 45), data.ElapsedTime);
        Assert.AreEqual(3, api.RequestCount, "Expected information, checkpoints, and results API calls.");
    }

    [TestMethod]
    public async Task GetLeaderDataWithScraperAsync_DoesNotReuseCacheAcrossDifferentSkiClassicsRaces()
    {
        var api = new FakeSkiClassicsApiHandler(new Dictionary<string, FakeRace>
        {
            ["91011"] = new("1011", "M", 90.0, "4:14:45.5"),
            ["91012"] = new("1012", "M", 38.0, "1:26:13.1")
        });

        using var httpClient = new HttpClient(api);
        var scraper = new LiveScraper.LiveScraper(httpClient);

        var first = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=91011&season=2099&gender=men");
        var second = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=91012&season=2099&gender=men");

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(90.0, first.DistanceKm, 0.01);
        Assert.AreEqual(new TimeSpan(4, 14, 45), first.ElapsedTime);
        Assert.AreEqual(38.0, second.DistanceKm, 0.01);
        Assert.AreEqual(new TimeSpan(1, 26, 13), second.ElapsedTime);
        Assert.AreEqual(6, api.RequestCount, "Each Ski Classics race should have its own cache entry and API round-trip.");
    }

    private sealed record FakeRace(string RaceId, string Sex, double DistanceKm, string LeaderTime);

    private sealed class FakeSkiClassicsApiHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, FakeRace> _racesByEventId;
        private readonly Dictionary<string, FakeRace> _racesByRaceId;

        public FakeSkiClassicsApiHandler(IReadOnlyDictionary<string, FakeRace> racesByEventId)
        {
            _racesByEventId = racesByEventId;
            _racesByRaceId = new Dictionary<string, FakeRace>(StringComparer.OrdinalIgnoreCase);
            foreach (var race in racesByEventId.Values)
            {
                _racesByRaceId[race.RaceId] = race;
            }
        }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            var requestBody = request.Content is null
                ? throw new AssertFailedException("Expected request content.")
                : await request.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(requestBody);
            var postData = document.RootElement.GetProperty("postdata");
            var requestType = postData.GetProperty("request").GetProperty("type").GetString();

            var json = requestType switch
            {
                "information" => CreateInformationResponse(postData.GetProperty("event_id").GetString()),
                "checkpoints" => CreateCheckpointsResponse(postData.GetProperty("race_id").GetRawText().Trim('"')),
                "results" => CreateResultsResponse(postData.GetProperty("race_id").GetRawText().Trim('"')),
                _ => throw new AssertFailedException($"Unexpected request type '{requestType}'.")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private string CreateInformationResponse(string? eventId)
        {
            if (eventId is null || !_racesByEventId.TryGetValue(eventId, out var race))
            {
                throw new AssertFailedException($"Unexpected event id '{eventId}'.");
            }

            return JsonSerializer.Serialize(new
            {
                races = new object[]
                {
                    new { id = race.RaceId, sex = race.Sex, gender_label = race.Sex == "M" ? "Men" : "Women" },
                    new { id = race.RaceId + "-other", sex = race.Sex == "M" ? "W" : "M", gender_label = race.Sex == "M" ? "Women" : "Men" }
                }
            });
        }

        private string CreateCheckpointsResponse(string raceId)
        {
            if (!_racesByRaceId.TryGetValue(raceId, out var race))
            {
                throw new AssertFailedException($"Unexpected race id '{raceId}'.");
            }

            return JsonSerializer.Serialize(new
            {
                currently_reached = "99",
                checkpoints = new object[]
                {
                    new { id = "99", dist = $"{race.DistanceKm:0.0} km" }
                }
            });
        }

        private string CreateResultsResponse(string raceId)
        {
            if (!_racesByRaceId.TryGetValue(raceId, out var race))
            {
                throw new AssertFailedException($"Unexpected race id '{raceId}'.");
            }

            return JsonSerializer.Serialize(new
            {
                results = new object[]
                {
                    new { rank = "1", time = race.LeaderTime }
                }
            });
        }
    }
}
