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
        public async Task GroqCanAnalyzeLeaderDistance()
        {

        }
    }
}
