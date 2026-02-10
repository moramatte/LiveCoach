using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.Common.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VasaLiveFeeder.Tests
{
    [TestClass]
    public class LiveScraperTest
    {
        [TestInitialize]
        public void Setup()
        {
            // Load secrets from environment variables (set locally or in CI/CD)
            var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
            var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            
            if (string.IsNullOrEmpty(browserlessToken))
            {
                Console.WriteLine("WARNING: BROWSERLESS_TOKEN not set - some tests may fail");
            }
            else
            {
                Console.WriteLine($"BROWSERLESS_TOKEN set: True");
                Console.WriteLine($"Token value: {browserlessToken.Substring(0, Math.Min(10, browserlessToken.Length))}...");
            }
            
            if (string.IsNullOrEmpty(groqApiKey))
            {
                Console.WriteLine("WARNING: GROQ_API_KEY not set - AI extraction tests will be skipped");
            }
            else
            {
                Console.WriteLine($"GROQ_API_KEY is set: {groqApiKey.Substring(0, Math.Min(10, groqApiKey.Length))}...");
            }
        }

        [TestMethod]
        public async Task LiveDataCanBeScraped()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://live.eqtiming.com/73152#result:297321-0-1308925-1-1-");

            Assert.IsNotNull(data);
            Assert.IsTrue(data.DistanceKm > 0);
        }

        [TestMethod]
        public async Task LiveDataIsScraped()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://live.fis-ski.com/cc-3632/results-pda.htm");

            Assert.IsNotNull(data);
            Assert.IsTrue(data.DistanceKm > 0);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromSkiClassicsLaDiagonela()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataAsync("https://skiclassics.com/live-center/?event=9620&season=2026&gender=men");

            Assert.IsNotNull(data);
            Assert.AreEqual(30, data.DistanceKm);
            Assert.AreEqual(new TimeSpan(1, 28, 48), data.ElapsedTime);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromSkiClassics()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=1296&season=2026&gender=men");

            Assert.IsNotNull(data);
            Assert.AreEqual(30, data.DistanceKm);
            Assert.AreEqual(new TimeSpan(1, 28, 48), data.ElapsedTime);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromSkiClassicsJurassienne()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=1304&season=2026&gender=men");

            Assert.IsNotNull(data);
            Assert.AreEqual(38, data.DistanceKm);
            Assert.AreEqual(new TimeSpan(1, 26, 13), data.ElapsedTime);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromSkiClassicsFinlandia()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://skiclassics.com/live-center/?event=12066&season=2026&gender=men");

            Assert.IsNotNull(data);
            Assert.AreEqual(30, data.DistanceKm);
            Assert.AreEqual(new TimeSpan(1, 28, 48), data.ElapsedTime);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromEQTiming()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataAsync("https://live.eqtiming.com/73152#livescroll");

            Assert.IsNotNull(data);
            Assert.IsTrue(data.DistanceKm > 0);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromEQTimingMidRace()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataAsync("https://live.eqtiming.com/75342#livescroll");

            Assert.IsNotNull(data);
            Assert.IsTrue(data.DistanceKm > 0);
        }


        [TestMethod]
        public async Task LeaderDataIncludesTimeWhenAvailable()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDataWithScraperAsync("https://live.eqtiming.com/73152#result:297321-0-1308925-1-1-");

            Assert.IsNotNull(data);
            Assert.IsTrue(data.DistanceKm > 0);
            // Time may or may not be available depending on race page format
            Console.WriteLine($"Leader at {data.DistanceKm:F2} km" + 
                             (data.ElapsedTime.HasValue ? $" after {data.ElapsedTime.Value:hh\\:mm\\:ss}" : " (time not available)"));
        }

        [TestMethod]
        public async Task AIExtractionAccuracyTest_SkiClassicsSample()
        {
            // Load the sample HTML file
            var testDir = AppDomain.CurrentDomain.BaseDirectory;
            var samplePath = Path.Combine(testDir, "LiveDataSamples", "SkiClassics.html");
            
            if (!File.Exists(samplePath))
            {
                Assert.Inconclusive($"Sample file not found: {samplePath}");
                return;
            }

            var htmlContent = await File.ReadAllTextAsync(samplePath);
            Console.WriteLine($"Loaded sample HTML: {htmlContent.Length} characters");

            var scraper = new LiveScraper.LiveScraper();
            var result = await scraper.AnalyzeWithAgentAsync(htmlContent);

            Assert.IsNotNull(result, "AI should extract data from sample HTML");
            Assert.AreEqual(4.0, result.DistanceKm, 0.5, "Expected ~4 km distance from sample");
            
            if (result.ElapsedTime.HasValue)
            {
                var expectedTime = new TimeSpan(0, 10, 35); // 0:10:35
                var timeDiff = Math.Abs((result.ElapsedTime.Value - expectedTime).TotalSeconds);
                Assert.IsTrue(timeDiff < 5, $"Expected time ~0:10:35, got {result.ElapsedTime}");
            }

            Console.WriteLine($"AI Extraction: {result.DistanceKm} km, Time: {result.ElapsedTime}");
        }

        [TestMethod]
        public async Task AIExtractionAccuracyTest_SkiClassicsFinished()
        {
            var samplePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "LiveDataSamples",
                "SkiClassicsFinished.html"
            );

            Assert.IsTrue(File.Exists(samplePath), $"Sample file not found: {samplePath}");

            var htmlContent = File.ReadAllText(samplePath);
            
            // Use actual LiveScraper for AI extraction testing (requires Groq API key)
            var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (string.IsNullOrWhiteSpace(groqApiKey))
            {
                Assert.Inconclusive("GROQ_API_KEY environment variable not set - skipping AI extraction test");
                return;
            }

            using var httpClient = new HttpClient();
            var scraper = new VasaLiveFeeder.LiveScraper.LiveScraper(httpClient, null, groqApiKey);
            var result = await scraper.AnalyzeWithAgentAsync(htmlContent);

            Assert.IsNotNull(result, "Result should not be null");
            
            // For a FINISHED race, we expect the finish line distance (30 km)
            Assert.AreEqual(30.0, result.DistanceKm, 0.5, 
                "Distance should be approximately 30 km (finish line) for finished race");
            
            Assert.IsNotNull(result.ElapsedTime, "Time should be extracted");
            
            // Winner time: 1:28:48.0
            var expectedTime = new TimeSpan(1, 28, 48);
            Assert.AreEqual(expectedTime, result.ElapsedTime.Value, 
                "Time should match winner's time: 1:28:48");
        }
    }
}
