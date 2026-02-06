using Microsoft.Playwright;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VasaLiveFeeder.LiveScraper;

public class LiveScraper : ILiveScraper
{
    private readonly HttpClient _httpClient;
    private readonly string? _groqApiKey;
    
    private static readonly Dictionary<string, (LeaderData? Data, DateTime Timestamp)> _cache = new();
    private static readonly object _cacheLock = new object();
    private static readonly TimeSpan _cacheTTL = TimeSpan.FromSeconds(30);

    public LiveScraper(HttpClient httpClient, string? groqApiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _groqApiKey = groqApiKey ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
    }

    public LiveScraper() : this(new HttpClient()) { }

    public async Task<LeaderData?> GetLeaderDataAsync(string url)
    {
        if (TryGetFromCache(url, out var cachedData))
        {
            Console.WriteLine($"[Cache HIT] Returning cached data for {url}");
            return cachedData;
        }
        
        try
        {
            // For SkiClassics and EQTiming, we need JavaScript rendering
            // Try Browserless first (if available), then fall back to Playwright
            var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
            if (!string.IsNullOrWhiteSpace(browserlessToken))
            {
                try
                {
                    Console.WriteLine($"[GetLeaderDataAsync] Trying Browserless for {url}");
                    var result = await GetLeaderDataWithBrowserlessOrPlaywrightAsync(url, browserlessToken, 30000).ConfigureAwait(false);
                    if (result != null)
                    {
                        AddToCache(url, result);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Browserless error]: {ex.Message}. Falling back to Playwright...");
                }
            }

            // Fall back to Playwright for JavaScript rendering
            Console.WriteLine($"[GetLeaderDataAsync] Using Playwright for {url}");
            var playwrightResult = await GetLeaderDataWithScraperAsync(url, 30000).ConfigureAwait(false);
            if (playwrightResult != null)
            {
                AddToCache(url, playwrightResult);
            }
            return playwrightResult;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetLeaderDataAsync error]: {ex.Message}");
            return null;
        }
    }

    private async Task<LeaderData?> GetLeaderDataWithBrowserlessOrPlaywrightAsync(string url, string browserlessToken, int timeoutMs)
    {
        try
        {
            var htmlContent = await GetRenderedHtmlViaBrowserlessAsync(url, browserlessToken, timeoutMs).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(htmlContent))
            {
                Console.WriteLine($"[Browserless] Successfully rendered HTML for {url}");
                var result = await AnalyzeWithAgentAsync(htmlContent).ConfigureAwait(false);
                return result;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetLeaderDataWithBrowserlessOrPlaywrightAsync error]: {ex.Message}");
            throw;
        }
    }

    public async Task<LeaderData?> GetLeaderDataWithScraperAsync(string url, int timeoutMs = 30000)
    {
        if (TryGetFromCache(url, out var cachedData))
        {
            Console.WriteLine($"[Cache HIT] Returning cached data for {url}");
            return cachedData;
        }

        try
        {
            var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
            var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

            Console.WriteLine($"[GetLeaderDataWithScraperAsync] BROWSERLESS_TOKEN present: {!string.IsNullOrWhiteSpace(browserlessToken)}");
            Console.WriteLine($"[GetLeaderDataWithScraperAsync] GROQ_API_KEY present: {!string.IsNullOrWhiteSpace(groqApiKey)}");

            string? htmlContent = null;
            string renderMethod = "unknown";

            // Try scraper service first (if SCRAPER_SERVICE_URL is set)
            var scraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");
            if (!string.IsNullOrWhiteSpace(scraperServiceUrl))
            {
                try
                {
                    Console.WriteLine($"[ScraperService] Using scraper service at {scraperServiceUrl}");
                    htmlContent = await GetRenderedHtmlViaScraperServiceAsync(url, scraperServiceUrl, timeoutMs).ConfigureAwait(false);
                    renderMethod = "ScraperService";
                    Console.WriteLine($"[ScraperService] Successfully rendered HTML ({htmlContent?.Length ?? 0} chars)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScraperService] Failed: {ex.Message}. Will try Browserless or Playwright...");
                    htmlContent = null;
                }
            }

            // Try Browserless if scraper service didn't work and we have a token
            if (htmlContent == null && !string.IsNullOrWhiteSpace(browserlessToken))
            {
                try
                {
                    Console.WriteLine($"[Browserless] Attempting to fetch {url}");
                    htmlContent = await GetRenderedHtmlViaBrowserlessAsync(url, browserlessToken, timeoutMs).ConfigureAwait(false);
                    renderMethod = "Browserless";
                    Console.WriteLine($"[Browserless] Successfully rendered HTML ({htmlContent?.Length ?? 0} chars)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Browserless] Failed: {ex.Message}. Will try Playwright...");
                    htmlContent = null;
                }
            }

            // Try Playwright as last resort
            if (htmlContent == null)
            {
                Console.WriteLine($"[Playwright] Attempting fallback for {url}");
                await EnsurePlaywrightBrowsersInstalledAsync().ConfigureAwait(false);
                using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
                var page = await browser.NewPageAsync().ConfigureAwait(false);
                page.SetDefaultTimeout(timeoutMs);

                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
                htmlContent = await page.ContentAsync().ConfigureAwait(false);
                renderMethod = "Playwright";
                Console.WriteLine($"[Playwright] Successfully fetched HTML ({htmlContent.Length} chars)");
            }

            // Now analyze the HTML with AI
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                Console.WriteLine($"[ERROR] All rendering methods failed to produce HTML content");
                return null;
            }

            Console.WriteLine($"[{renderMethod}] Analyzing HTML with AI...");
            var result = await AnalyzeWithAgentAsync(htmlContent).ConfigureAwait(false);

            if (result != null)
            {
                Console.WriteLine($"[{renderMethod}] AI extraction succeeded: {result.DistanceKm} km, Time: {result.ElapsedTime}");
                AddToCache(url, result);
            }
            else
            {
                Console.WriteLine($"[{renderMethod}] AI extraction returned null. HTML was retrieved successfully but AI could not extract race data.");
                Console.WriteLine($"[{renderMethod}] This likely means:");
                Console.WriteLine($"  1. The race page format has changed");
                Console.WriteLine($"  2. The race has not started yet (no leader data available)");
                Console.WriteLine($"  3. The AI model failed to parse the data");
                Console.WriteLine($"  4. GROQ_API_KEY may be invalid or out of credits");
            }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GetLeaderDataWithScraperAsync error]: {ex.Message}");
                    Console.WriteLine($"[GetLeaderDataWithScraperAsync stack]: {ex.StackTrace}");
                    return null;
                }
            }

            public async Task<LeaderData?> AnalyzeWithAgentAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(_groqApiKey))
        {
            Console.WriteLine("[AnalyzeWithAgent]: No GROQ_API_KEY found - cannot extract data with AI");
            return null;
        }

        try
        {
            var extractedData = ExtractRaceData(content);
            Console.WriteLine($"[Groq] Processing {extractedData.Length} chars of extracted race data");

            // Log a sample of what we're sending to the AI
            var sample = extractedData.Length > 500 ? extractedData.Substring(0, 500) + "..." : extractedData;
            Console.WriteLine($"[Groq] Sample data being sent to AI:\n{sample}");

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Extract distance (km) and leader's time from race timing data.\n\n" +
                                   "PROVIDERS:\n" +
                                   "- SkiClassics: Distance in h3/h4 headings or 'Active Checkpoint'\n" +
                                   "- EQTiming: Distance in 'Point' column or race title. 'Mål' = Finish\n\n" +
                                   "RULES:\n" +
                                   "- Distance: Current checkpoint where leader is tracked\n" +
                                   "- Time: From first data row (leader), format H:MM:SS or HH:MM:SS\n\n" +
                                   "OUTPUT FORMAT (no explanation):\n" +
                                   "distance: [number], time: [H:MM:SS]\n" +
                                   "If data unclear: distance: null, time: null"
                    },
                    new
                    {
                        role = "user",
                        content = $"Extract:\n\n{extractedData}"
                    }
                },
                temperature = 0.0, // Use 0 for deterministic output
                max_tokens = 50 // Concise output only
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_groqApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Groq API error {response.StatusCode}]: {responseBody}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine($"[Groq] Authentication failed - GROQ_API_KEY may be invalid");
                }
                else if ((int)response.StatusCode == 429)
                {
                    Console.WriteLine($"[Groq] Rate limit exceeded or out of credits");
                }

                return null;
            }

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var aiResult = jsonResponse.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            Console.WriteLine($"[AI Response]: {aiResult}");

            // DIAGNOSTIC: Log the extracted data sent to AI
            Console.WriteLine($"[DIAGNOSTIC] Extracted data sent to Groq ({extractedData.Length} chars):");
            Console.WriteLine(extractedData.Length > 1000 ? extractedData.Substring(0, 1000) + "..." : extractedData);

            var parsed = ParseAIResponse(aiResult);
            if (parsed == null)
            {
                Console.WriteLine($"[DIAGNOSTIC] ParseAIResponse returned NULL for AI result: '{aiResult}'");
                Console.WriteLine($"[DIAGNOSTIC] This means the AI could not extract valid distance/time from the HTML");
            }
            else
            {
                Console.WriteLine($"[SUCCESS] Parsed leader data: {parsed.DistanceKm} km, Time: {parsed.ElapsedTime}");
            }

            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalyzeWithAgent error]: {ex.Message}");
            return null;
        }
    }

    private static string ExtractRaceData(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sections = new List<string>();
        
        // Provider detection
        string provider = html.Contains("live.skiklasserna.se") || html.Contains("skiclassics") ? "SkiClassics"
                        : html.Contains("live.eqtiming.com") || html.Contains("eqtiming") ? "EQTiming"
                        : "Unknown";
        
        Console.WriteLine($"[Provider] Detected: {provider}");
        sections.Add($"PROVIDER: {provider}");

        // Extract page title for event context
        var titleMatch = Regex.Match(html, @"<title>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (titleMatch.Success)
        {
            sections.Add($"Page Title: {titleMatch.Groups[1].Value.Trim()}");
            Console.WriteLine($"[Title] {titleMatch.Groups[1].Value.Trim()}");
        }

        // Provider-specific extraction
        if (provider == "SkiClassics")
        {
            // Extract ALL checkpoints from checkpoint list (gives race context and total distance)
            var checkpointMatches = Regex.Matches(html, @"<h[34][^>]*>\s*([\d.,]+)\s*km\s*\|\s*([^<]+)</h[34]>", RegexOptions.IgnoreCase);
            if (checkpointMatches.Count > 0)
            {
                sections.Add($"\nCheckpoints ({checkpointMatches.Count} found):");
                foreach (Match match in checkpointMatches)
                {
                    sections.Add($"  {match.Groups[1].Value} km | {match.Groups[2].Value.Trim()}");
                }
                Console.WriteLine($"[SkiClassics] Found {checkpointMatches.Count} checkpoints");
            }
            else
            {
                Console.WriteLine($"[SkiClassics] WARNING: No checkpoints found in h3/h4 headings");
            }
            
            // Also look for active checkpoint marker
            var activeMatch = Regex.Match(html, @"data-checkpoint-active=""true""[^>]*>.*?<h[34][^>]*>\s*([\d.,]+)\s*km", 
                                         RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (activeMatch.Success)
            {
                sections.Add($"\nActive Checkpoint: {activeMatch.Groups[1].Value} km");
                Console.WriteLine($"[SkiClassics] Active checkpoint: {activeMatch.Groups[1].Value} km");
            }
        }
        else if (provider == "EQTiming")
        {
            // Extract race distance from table content (e.g., "45 km Motion", "45 km Tävling")
            var raceDistanceMatches = Regex.Matches(html, @">([\d.,]+)\s*km\s+(?:Motion|Tävling|Elite|Race)<", RegexOptions.IgnoreCase);
            if (raceDistanceMatches.Count > 0)
            {
                sections.Add($"\nRace distances found:");
                var distances = new HashSet<string>();
                foreach (Match match in raceDistanceMatches.Cast<Match>().Take(10))
                {
                    distances.Add($"{match.Groups[1].Value} km");
                }
                foreach (var dist in distances)
                {
                    sections.Add($"  {dist}");
                }
                Console.WriteLine($"[EQTiming] Found {distances.Count} unique race distances");
            }
            
            // Extract Point column values (current checkpoint positions, including "Mål")
            var pointMatches = Regex.Matches(html, @"<td[^>]*class=""[^""]*col-point-scroll[^""]*""[^>]*>([^<]+)</td>", RegexOptions.IgnoreCase);
            if (pointMatches.Count > 0)
            {
                sections.Add($"\nPoint column (first 10 entries):");
                var pointValues = new List<string>();
                for (int i = 0; i < Math.Min(10, pointMatches.Count); i++)
                {
                    var value = pointMatches[i].Groups[1].Value.Trim();
                    pointValues.Add(value);
                    sections.Add($"  {i + 1}. {value}");
                }
                Console.WriteLine($"[EQTiming] Found {pointMatches.Count} Point column values, showing first 10");
                
                // If we see "Mål" (Finish), note it
                if (pointValues.Any(v => v.Contains("Mål") || v.Contains("mal")))
                {
                    sections.Add($"\n[Note: 'Mål' (Finish) detected - leader has finished race]");
                    Console.WriteLine($"[EQTiming] Detected 'Mål' (Finish) in Point column");
                }
            }
            else
            {
                Console.WriteLine($"[EQTiming] WARNING: No Point column values found");
            }
        }

        // Extract results table with better context preservation
        // Try multiple patterns to find the results table
        Match tableMatch = null;
        
        // Pattern 1: Look for table with common timing headers
        tableMatch = Regex.Match(html, @"<table[^>]*>.*?(?:Position|Pos|Rank|Bib|Athlete|Point|Time).*?</table>", 
                                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        
        // Pattern 2: If no match, look for any table with "live-center-table" or similar class
        if (!tableMatch.Success)
        {
            tableMatch = Regex.Match(html, @"<table[^>]*(?:class=""[^""]*(?:live|result|timing)[^""]*"")[^>]*>.*?</table>", 
                                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Console.WriteLine($"[Table] Trying class-based pattern");
        }
        
        // Pattern 3: If still no match, look for any table with tbody
        if (!tableMatch.Success)
        {
            tableMatch = Regex.Match(html, @"<table[^>]*>.*?<tbody>.*?</tbody>.*?</table>", 
                                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Console.WriteLine($"[Table] Trying tbody pattern");
        }
        
        if (tableMatch.Success)
        {
            var tableHtml = tableMatch.Value;
            Console.WriteLine($"[Table] Found table with {tableHtml.Length} chars");
            
            // Keep some HTML structure to help AI understand table layout
            // Extract table headers
            var headerMatch = Regex.Match(tableHtml, @"<thead>.*?</thead>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (headerMatch.Success)
            {
                var headerText = StripHtmlTags(headerMatch.Value);
                sections.Add($"\nTABLE HEADERS:\n{headerText}");
            }
            
            // Extract first 20 rows (leader + context)
            var rowMatches = Regex.Matches(tableHtml, @"<tr[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (rowMatches.Count > 0)
            {
                sections.Add($"\nTABLE ROWS (first 20 of {rowMatches.Count}):");
                for (int i = 0; i < Math.Min(20, rowMatches.Count); i++)
                {
                    var rowText = StripHtmlTags(rowMatches[i].Value);
                    // Clean up extra whitespace
                    rowText = Regex.Replace(rowText, @"\s+", " ").Trim();
                    if (!string.IsNullOrWhiteSpace(rowText) && rowText.Length > 5)
                    {
                        sections.Add($"  Row {i + 1}: {rowText}");
                    }
                }
                Console.WriteLine($"[Table] Extracted {Math.Min(20, rowMatches.Count)} rows from {rowMatches.Count} total");
            }
        }
        else
        {
            Console.WriteLine($"[Table] WARNING: No results table found with any pattern");
        }

        var result = string.Join("\n", sections);
        
        // Truncate if still too large (stay under 20K for AI token limits)
        if (result.Length > 20000)
        {
            result = result.Substring(0, 20000) + "\n... [truncated]";
            Console.WriteLine($"[ExtractRaceData] Truncated output to 20K chars");
        }
        else
        {
            Console.WriteLine($"[ExtractRaceData] Output size: {result.Length} chars");
        }
        
        return result;
    }

    private static LeaderData? ParseAIResponse(string? aiResult)
    {
        if (string.IsNullOrWhiteSpace(aiResult) || aiResult.Contains("null"))
            return null;

        var distanceMatch = Regex.Match(aiResult, @"distance:\s*([\d.]+)", RegexOptions.IgnoreCase);
        var timeMatch = Regex.Match(aiResult, @"time:\s*(\d+:\d+:\d+)", RegexOptions.IgnoreCase);

        if (!distanceMatch.Success)
            return null;

        var distance = double.Parse(distanceMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        TimeSpan? time = null;

        if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups[1].Value, out var parsedTime))
            time = parsedTime;

        return new LeaderData(distance, time);
    }

    private async Task<string> GetRenderedHtmlViaBrowserlessAsync(string url, string apiToken, int timeoutMs)
    {
        try
        {
            // Check if we have a custom scraper service URL (our Playwright container)
            var scraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");

            if (!string.IsNullOrWhiteSpace(scraperServiceUrl))
            {
                // Use our own Playwright scraper service
                return await GetRenderedHtmlViaScraperServiceAsync(url, scraperServiceUrl, timeoutMs).ConfigureAwait(false);
            }

            // Fall back to Browserless (if token is provided)
            var requestBody = new
            {
                url,
                gotoOptions = new
                {
                    waitUntil = "networkidle2"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"[Browserless] Calling /content API for {url}");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://chrome.browserless.io/content?token={apiToken}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Console.WriteLine($"[Browserless] Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorPreview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                Console.WriteLine($"[Browserless] Error response: {errorPreview}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception($"Browserless authentication failed - check BROWSERLESS_TOKEN");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                         responseBody.Contains("rate limit") || responseBody.Contains("quota"))
                {
                    throw new Exception($"Browserless rate limit exceeded - consider upgrading plan or try later");
                }
                else if ((int)response.StatusCode >= 500)
                {
                    throw new Exception($"Browserless service error (likely temporary outage) - status {response.StatusCode}");
                }

                throw new Exception($"Browserless API failed with status {response.StatusCode}: {errorPreview}");
            }

            Console.WriteLine($"[Browserless] Success - received {responseBody.Length} chars");
            return responseBody;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Browserless] Exception: {ex.Message}");
            throw;
        }
    }

    private async Task<string> GetRenderedHtmlViaScraperServiceAsync(string url, string scraperServiceUrl, int timeoutMs)
    {
        try
        {
            var requestBody = new
            {
                url,
                waitUntil = "networkidle"
            };

            var json = JsonSerializer.Serialize(requestBody);
            var serviceEndpoint = $"{scraperServiceUrl.TrimEnd('/')}/render";
            Console.WriteLine($"[ScraperService] Calling {serviceEndpoint} for {url}");

            // First, test if the service is reachable with a health check
            try
            {
                var healthEndpoint = $"{scraperServiceUrl.TrimEnd('/')}/health";
                Console.WriteLine($"[ScraperService] Testing health endpoint: {healthEndpoint}");
                using var healthRequest = new HttpRequestMessage(HttpMethod.Get, healthEndpoint);
                using var healthResponse = await _httpClient.SendAsync(healthRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!healthResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[ScraperService] WARNING: Health check failed with status {healthResponse.StatusCode}");
                }
                else
                {
                    Console.WriteLine($"[ScraperService] Health check passed");
                }
            }
            catch (HttpRequestException healthEx)
            {
                Console.WriteLine($"[ScraperService] ERROR: Cannot reach scraper service at {scraperServiceUrl}");
                Console.WriteLine($"[ScraperService] Health check error: {healthEx.Message}");
                throw new Exception($"Scraper service is not reachable at {scraperServiceUrl}. Ensure the service is running. Error: {healthEx.Message}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, serviceEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("User-Agent", "VasaLiveFeeder/1.0");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Console.WriteLine($"[ScraperService] Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorPreview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                Console.WriteLine($"[ScraperService] Error response: {errorPreview}");
                throw new Exception($"Scraper service failed with status {response.StatusCode}: {errorPreview}");
            }

            // Parse JSON response
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (!jsonResponse.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                var error = jsonResponse.TryGetProperty("error", out var errorProp) 
                    ? errorProp.GetString() 
                    : "Unknown error";
                throw new Exception($"Scraper service returned error: {error}");
            }

            var html = jsonResponse.GetProperty("html").GetString();
            var duration = jsonResponse.TryGetProperty("duration", out var durationProp) 
                ? durationProp.GetInt32() 
                : 0;

            Console.WriteLine($"[ScraperService] Success - received {html?.Length ?? 0} chars in {duration}ms");

            return html ?? string.Empty;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"[ScraperService] HTTP Exception: {httpEx.Message}");
            Console.WriteLine($"[ScraperService] Stack trace: {httpEx.StackTrace}");
            throw new Exception($"Failed to connect to scraper service at {scraperServiceUrl}: {httpEx.Message}. Ensure the service is running and accessible.", httpEx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScraperService] Exception: {ex.Message}");
            Console.WriteLine($"[ScraperService] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private static async Task EnsurePlaywrightBrowsersInstalledAsync()
    {
        // Skip Playwright installation check - assume browsers are installed or handle errors at runtime
        await Task.CompletedTask;
    }

    private static string StripHtmlTags(string html)
    {
        return Regex.Replace(html, @"<[^>]+>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");
    }

    private static bool TryGetFromCache(string url, out LeaderData? data)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(url, out var cached) && DateTime.UtcNow - cached.Timestamp < _cacheTTL)
            {
                data = cached.Data;
                return true;
            }
            _cache.Remove(url);
        }
        data = null;
        return false;
    }

    private static void AddToCache(string url, LeaderData? data)
    {
        lock (_cacheLock)
        {
            _cache[url] = (data, DateTime.UtcNow);
            var expired = _cache.Where(kvp => DateTime.UtcNow - kvp.Value.Timestamp >= _cacheTTL)
                                 .Select(kvp => kvp.Key).ToList();
            expired.ForEach(k => _cache.Remove(k));
        }
    }
}
