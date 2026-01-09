namespace VasaLiveFeeder.LiveScraper
{
    public interface ILiveScraper
    {
        Task<double?> GetLeaderDistanceWithPlaywrightAsync(string url, int timeoutMs = 30000);
    }
}
