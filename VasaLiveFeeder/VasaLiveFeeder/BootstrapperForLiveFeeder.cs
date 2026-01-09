using Infrastructure;
using VasaLiveFeeder.LiveScraper;

namespace VasaLiveFeeder
{
    public class BootstrapperForLiveFeeder
    {
        public static void Reset()
        {
            ServiceLocator.Reset();

            ServiceLocator.RegisterTransient<ILiveScraper, LiveScraper.LiveScraper>();
        }
    }
}
