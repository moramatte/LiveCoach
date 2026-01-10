using Microsoft.Playwright;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VasaLiveFeeder.LiveScraper;

/// <summary>
/// Fetches live race information and extracts how far the leader has come (in kilometers).
/// </summary>
public class LiveScraper : ILiveScraper
{
    private readonly HttpClient _httpClient;
    private readonly string? _groqApiKey;

    /// <summary>
    /// Create a new scraper using the provided <see cref="HttpClient"/>.
    /// </summary>
    public LiveScraper(HttpClient httpClient, string? groqApiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _groqApiKey = groqApiKey ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
    }

    /// <summary>
    /// Convenience constructor that creates a new HttpClient instance.
    /// </summary>
    public LiveScraper() : this(new HttpClient()) { }

    /// <summary>
    /// Fetches the content at <paramref name="url"/> and attempts to extract the leader distance in kilometers.
    /// Returns null if no sensible value could be extracted.
    /// </summary>
    public async Task<double?> GetLeaderDistanceKmAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url is required", nameof(url));
        
        // Check for Browserless token first (handles JavaScript)
        var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
        if (!string.IsNullOrEmpty(browserlessToken))
        {
            var content = await GetRenderedHtmlViaBrowserlessAsync(url, browserlessToken, 30000);
            var aiResult = await AnalyzeWithAgentAsync(content).ConfigureAwait(false);
            return aiResult ?? ParseContent(content);
        }

        // Fall back to basic HTTP (won't work for JS-rendered sites)
        var html = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
        
        // Try AI analysis first, then fall back to regex
        var aiAnalysis = await AnalyzeWithAgentAsync(html).ConfigureAwait(false);
        return aiAnalysis ?? ParseContent(html);
    }

    /// <summary>
    /// Renders the page with Playwright (executes JavaScript) and runs the same parsing logic
    /// against the rendered HTML. This requires Playwright browser binaries to be installed on the machine.
    /// </summary>
    public async Task<double?> GetLeaderDistanceWithPlaywrightAsync(string url, int timeoutMs = 30000)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url is required", nameof(url));
        
        // Try Browserless first if API token is configured
        var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
        Console.WriteLine($"[GetLeaderDistanceWithPlaywrightAsync] BROWSERLESS_TOKEN: {(string.IsNullOrEmpty(browserlessToken) ? "NOT SET" : "SET (" + browserlessToken.Substring(0, Math.Min(10, browserlessToken.Length)) + "...)")}");
        
        if (!string.IsNullOrEmpty(browserlessToken))
        {
            try
            {
                Console.WriteLine("[GetLeaderDistanceWithPlaywrightAsync] Using Browserless...");
                var htmlContent = await GetRenderedHtmlViaBrowserlessAsync(url, browserlessToken, timeoutMs);
                Console.WriteLine($"[GetLeaderDistanceWithPlaywrightAsync] Browserless returned {htmlContent?.Length ?? 0} characters");
                var browserlessAiResult = await AnalyzeWithAgentAsync(htmlContent).ConfigureAwait(false);
                var result = browserlessAiResult ?? ParseContent(htmlContent);
                Console.WriteLine($"[GetLeaderDistanceWithPlaywrightAsync] Result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Browserless failed, falling back to Playwright: {ex.Message}");
            }
        }

        // Fall back to Playwright (requires browser installation)
        Console.WriteLine("[GetLeaderDistanceWithPlaywrightAsync] Falling back to Playwright...");
        await EnsurePlaywrightBrowsersInstalledAsync().ConfigureAwait(false);

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var page = await browser.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = timeoutMs });
        var content = await page.ContentAsync();
        
        // Try AI analysis first, then fall back to regex
        var aiResult = await AnalyzeWithAgentAsync(content).ConfigureAwait(false);
        return aiResult ?? ParseContent(content);
    }

    private async Task<string> GetRenderedHtmlViaBrowserlessAsync(string url, string apiToken, int timeoutMs)
    {
        var requestBody = new
        {
            url = url,
            waitForTimeout = Math.Min(timeoutMs, 30000), // Max 30s
            gotoOptions = new { waitUntil = "networkidle0" }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            $"https://chrome.browserless.io/content?token={apiToken}",
            content
        );

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static double? ParseContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 1) Look for values followed by "km" (e.g. "12.34 km" or "12,34 km")
        var kmRegex = new Regex(@"(\d{1,3}(?:[.,]\d+)?)(?:\s?km)\b", RegexOptions.IgnoreCase);
        var kmMatch = kmRegex.Match(content);
        if (kmMatch.Success)
        {
            if (TryParseNumber(kmMatch.Groups[1].Value, out var km)) return km;
        }

        // 2) Look for meters followed by 'm' and convert to km (e.g. "12345 m")
        var mRegex = new Regex(@"(\d{1,7})(?:\s?m)\b", RegexOptions.IgnoreCase);
        var mMatch = mRegex.Match(content);
        if (mMatch.Success && double.TryParse(mMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var meters))
        {
            // sanity check: meters should be large enough to indicate leader progress (e.g. > 0)
            if (meters >= 1)
            {
                return meters / 1000.0;
            }
        }

        // 3) Try to find JSON-like fields: "distance": 12.34 or "leaderDistance":12.34
        var jsonNumberRegex = new Regex("\\\"(?:distance|leaderDistance|leader_distance)\\\"\\s*[:=]\\s*(\\d+(?:[.,]\\d+)?)", RegexOptions.IgnoreCase);
        var jsonMatch = jsonNumberRegex.Match(content);
        if (jsonMatch.Success && TryParseNumber(jsonMatch.Groups[1].Value, out var jsonKm)) return jsonKm;

        // 4) As a last resort, find the largest km-like number on the page (heuristic)
        var allKm = kmRegex.Matches(content);
        double? best = null;
        foreach (Match m in allKm)
        {
            if (TryParseNumber(m.Groups[1].Value, out var val))
            {
                if (!best.HasValue || val > best.Value) best = val;
            }
        }

        return best;
    }

    /// <summary>
    /// Uses an AI reasoning model (Groq) to analyze race content and extract leader distance in kilometers.
    /// Falls back to regex parsing if API is unavailable or parsing fails.
    /// </summary>
    /// <param name="content">Raw HTML or text content from a race results page</param>
    /// <returns>Leader distance in km, or null if extraction fails</returns>
    public async Task<double?> AnalyzeWithAgentAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // First try fast regex-based parsing
        var regexResult = ParseContent(content);
        if (regexResult.HasValue)
        {
            return regexResult;
        }

        // Fall back to AI reasoning if API key is available
        if (string.IsNullOrWhiteSpace(_groqApiKey))
        {
            return null;
        }

        try
        {
            // Truncate content if too large (Groq context limit ~8k tokens, ~32k chars safely)
            var truncated = content.Length > 30000 ? content.Substring(0, 30000) : content;

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a race data analyst. Extract the leader's distance in kilometers from race results. Return ONLY a number (e.g., '42.5' or '13.2'). If you cannot find the leader distance, return 'null'."
                    },
                    new
                    {
                        role = "user",
                        content = $"Analyze this race page content and extract how far the race leader has progressed in kilometers:\n\n{truncated}"
                    }
                },
                temperature = 0.1,
                max_tokens = 50
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_groqApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Groq API error {response.StatusCode}]: {error}");
                return null;
            }

            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseString);
            var aiResult = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(aiResult) || aiResult.Contains("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Extract number from response (handle "42.5 km" or just "42.5")
            var numberMatch = Regex.Match(aiResult, @"(\d+(?:[.,]\d+)?)");
            if (numberMatch.Success && TryParseNumber(numberMatch.Groups[1].Value, out var km))
            {
                return km;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalyzeWithAgent error]: {ex.Message}");
            return null;
        }
    }

    private static bool TryParseNumber(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        // normalize comma decimal separators to dot for invariant parsing
        if (raw.Contains(",") && !raw.Contains(".")) raw = raw.Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    // Cache to avoid running installation multiple times in the same process
    private static readonly object s_playwrightInstallLock = new object();
    private static bool s_playwrightInstallChecked;

    private static async Task EnsurePlaywrightBrowsersInstalledAsync()
    {
        // Avoid repeating installation attempts in the same process
        lock (s_playwrightInstallLock)
        {
            if (s_playwrightInstallChecked) return;
        }

        try
        {
            // On Linux (Azure Functions), use the Playwright CLI installer directly
            var playwrightType = Type.GetType("Microsoft.Playwright.Playwright, Microsoft.Playwright");
            if (playwrightType == null)
            {
                lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
                return;
            }

            // Try to use Playwright's built-in installation method
            var programType = Type.GetType("Microsoft.Playwright.Program, Microsoft.Playwright");
            if (programType != null)
            {
                var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (mainMethod != null)
                {
                    // Run: playwright install chromium --with-deps
                    mainMethod.Invoke(null, new object[] { new[] { "install", "chromium", "--with-deps" } });
                    lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
                    return;
                }
            }

            lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Playwright install warning]: {ex.Message}");
            lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
        }
    }
}
