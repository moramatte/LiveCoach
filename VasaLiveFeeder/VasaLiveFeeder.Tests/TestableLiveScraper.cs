using System;
using System.Threading.Tasks;
using VasaLiveFeeder.LiveScraper;

namespace VasaLiveFeeder.Tests
{
    internal class TestableLiveScraper : ILiveScraper
    {
        public static double ReturnValue { get; set; }
        public static TimeSpan? ReturnTime { get; set; }
        
        public async Task<LeaderData?> GetLeaderDataAsync(string url)
        {
            return new LeaderData(ReturnValue, ReturnTime);
        }
        
        public async Task<LeaderData?> GetLeaderDataWithScraperAsync(string url, int timeoutMs = 30000)
        {
            return new LeaderData(ReturnValue, ReturnTime);
        }
        
        public async Task<LeaderData?> AnalyzeWithAgentAsync(string content)
        {
            return new LeaderData(ReturnValue, ReturnTime);
        }
    }
}
