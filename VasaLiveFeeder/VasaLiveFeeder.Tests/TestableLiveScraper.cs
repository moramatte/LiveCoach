using System.Threading.Tasks;
using VasaLiveFeeder.LiveScraper;

namespace VasaLiveFeeder.Tests
{
    internal class TestableLiveScraper : ILiveScraper
    {
        public static double ReturnValue { get; set; }
        
        public async Task<double?> GetLeaderDistanceKmAsync(string url)
        {
            return ReturnValue;
        }
        
        public async Task<double?> GetLeaderDistanceWithPlaywrightAsync(string url, int timeoutMs = 30000)
        {
            return ReturnValue;
        }
        
        public async Task<double?> AnalyzeWithAgentAsync(string content)
        {
            return ReturnValue;
        }
    }
}
