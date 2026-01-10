using System;
using System.Collections.Generic;
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
            // Set Browserless token for tests
            Environment.SetEnvironmentVariable("BROWSERLESS_TOKEN", "2TlLSZWMFnAuZLO8eff5cee189ee3d1b81d7dfe44129cf7da");
            
            // Debug: Verify it's set
            var token = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
            Console.WriteLine($"BROWSERLESS_TOKEN set: {!string.IsNullOrEmpty(token)}");
            if (!string.IsNullOrEmpty(token))
            {
                Console.WriteLine($"Token value: {token.Substring(0, Math.Min(10, token.Length))}...");
            }
        }

        [TestMethod]
        public async Task LiveDataCanBeScraped()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDistanceWithPlaywrightAsync("https://live.eqtiming.com/73152#result:297321-0-1308925-1-1-");

            Assert.IsTrue(data > 0);
        }

        [TestMethod]
        public async Task LiveDataIsScraped()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDistanceWithPlaywrightAsync("https://live.fis-ski.com/cc-3632/results-pda.htm");

            Assert.IsTrue(data > 0);
        }

        [TestMethod]
        public async Task LiveDataIsScrapedFromSkiClassics()
        {
            var scraper = new LiveScraper.LiveScraper();
            var data = await scraper.GetLeaderDistanceKmAsync("https://skiclassics.com/live-center/?event=1296&season=2026&gender=men");

            Assert.IsTrue(data > 0);
        }


        [TestMethod]
        public async Task GroqCanAnalyzeLeaderDistance()
        {

        }
    }
}
