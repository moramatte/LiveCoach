using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<LiveScraper>? _logger;

    private static readonly Dictionary<string, (LeaderData? Data, DateTime Timestamp)> _cache = new();
    private static readonly object _cacheLock = new object();
    private static readonly TimeSpan _cacheTTL = TimeSpan.FromSeconds(30);

    public LiveScraper(HttpClient httpClient, ILogger<LiveScraper>? logger = null, string? groqApiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _groqApiKey = groqApiKey ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
    }

    public LiveScraper() : this(new HttpClient(), null) { }

    public async Task<LeaderData?> GetLeaderDataAsync(string url)
    {
        if (TryGetFromCache(url, out var cachedData))
        {
            _logger?.LogInformation("[Cache HIT] Returning cached data for {Url}", url);
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
                    _logger?.LogInformation("[GetLeaderDataAsync] Trying Browserless for {Url}", url);
                    var result = await GetLeaderDataWithBrowserlessOrPlaywrightAsync(url, browserlessToken, 60000).ConfigureAwait(false);
                    if (result != null)
                    {
                        AddToCache(url, result);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Browserless error]: {Message}. Falling back to Playwright...", ex.Message);
                }
            }

                // Fall back to Playwright for JavaScript rendering
                _logger?.LogInformation("[GetLeaderDataAsync] Using Playwright for {Url}", url);
                var playwrightResult = await GetLeaderDataWithScraperAsync(url, 60000).ConfigureAwait(false);
                if (playwrightResult != null)
                {
                    AddToCache(url, playwrightResult);
                }
                return playwrightResult;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[GetLeaderDataAsync error]: {Message}", ex.Message);
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
                    _logger?.LogInformation("[Browserless] Successfully rendered HTML for {Url}", url);
                    var result = await AnalyzeWithAgentAsync(htmlContent).ConfigureAwait(false);
                    return result;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[GetLeaderDataWithBrowserlessOrPlaywrightAsync error]: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<LeaderData?> GetLeaderDataWithScraperAsync(string url, int timeoutMs = 60000)
    {
        if (TryGetFromCache(url, out var cachedData))
        {
            _logger?.LogInformation("[Cache HIT] Returning cached data for {Url}", url);
            return cachedData;
        }

        try
        {
            var browserlessToken = Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN");
            var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

            _logger?.LogInformation("[GetLeaderDataWithScraperAsync] BROWSERLESS_TOKEN present: {HasToken}, GROQ_API_KEY present: {HasKey}", 
                !string.IsNullOrWhiteSpace(browserlessToken), !string.IsNullOrWhiteSpace(groqApiKey));

            string? htmlContent = null;
            string renderMethod = "unknown";

            // Try scraper service first (if SCRAPER_SERVICE_URL is set)
            var scraperServiceUrl = Environment.GetEnvironmentVariable("SCRAPER_SERVICE_URL");
            if (!string.IsNullOrWhiteSpace(scraperServiceUrl))
            {
                try
                {
                    _logger?.LogInformation("[ScraperService] Using scraper service at {ServiceUrl}", scraperServiceUrl);
                    htmlContent = await GetRenderedHtmlViaScraperServiceAsync(url, scraperServiceUrl, timeoutMs).ConfigureAwait(false);
                    renderMethod = "ScraperService";
                    _logger?.LogInformation("[ScraperService] Successfully rendered HTML ({Length} chars)", htmlContent?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[ScraperService] Failed: {Message}. Will try Browserless or Playwright...", ex.Message);
                    htmlContent = null;
                }
            }

            // Try Browserless if scraper service didn't work and we have a token
            if (htmlContent == null && !string.IsNullOrWhiteSpace(browserlessToken))
            {
                try
                {
                    _logger?.LogInformation("[Browserless] Attempting to fetch {Url}", url);
                    htmlContent = await GetRenderedHtmlViaBrowserlessAsync(url, browserlessToken, timeoutMs).ConfigureAwait(false);
                    renderMethod = "Browserless";
                    _logger?.LogInformation("[Browserless] Successfully rendered HTML ({Length} chars)", htmlContent?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Browserless] Failed: {Message}. Will try Playwright...", ex.Message);
                    htmlContent = null;
                }
            }

            // Try Playwright as last resort
            if (htmlContent == null)
            {
                _logger?.LogInformation("[Playwright] Attempting fallback for {Url}", url);
                await EnsurePlaywrightBrowsersInstalledAsync().ConfigureAwait(false);
                using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
                var page = await browser.NewPageAsync().ConfigureAwait(false);
                page.SetDefaultTimeout(timeoutMs);

                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
                htmlContent = await page.ContentAsync().ConfigureAwait(false);
                renderMethod = "Playwright";
                _logger?.LogInformation("[Playwright] Successfully fetched HTML ({Length} chars)", htmlContent.Length);
            }

            // Now analyze the HTML with AI
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                _logger?.LogError("[ERROR] All rendering methods failed to produce HTML content");
                return null;
            }

            _logger?.LogInformation("[{RenderMethod}] Analyzing HTML with AI...", renderMethod);
            var result = await AnalyzeWithAgentAsync(htmlContent).ConfigureAwait(false);

            if (result != null)
            {
                _logger?.LogInformation("[{RenderMethod}] AI extraction succeeded: {Distance} km, Time: {Time}", renderMethod, result.DistanceKm, result.ElapsedTime);
                AddToCache(url, result);
            }
            else
            {
                _logger?.LogWarning("[{RenderMethod}] AI extraction returned null. HTML was retrieved successfully but AI could not extract race data. " +
                    "This likely means: 1) Race page format changed, 2) Race not started yet, 3) AI model failed to parse, 4) GROQ_API_KEY invalid/out of credits", renderMethod);
            }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[GetLeaderDataWithScraperAsync error]: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<LeaderData?> AnalyzeWithAgentAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(_groqApiKey))
        {
            _logger?.LogWarning("[AnalyzeWithAgent]: No GROQ_API_KEY found - cannot extract data with AI");
            return null;
        }

        try
        {
            var extractedData = ExtractRaceData(content);
            _logger?.LogInformation("[Groq] Processing {Length} chars of extracted race data", extractedData.Length);

            // Log a sample of what we're sending to the AI
            var sample = extractedData.Length > 500 ? extractedData.Substring(0, 500) + "..." : extractedData;
            _logger?.LogDebug("[Groq] Sample data being sent to AI: {Sample}", sample);

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
                _logger?.LogError("[Groq API error {StatusCode}]: {ResponseBody}", response.StatusCode, responseBody);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger?.LogWarning("[Groq] Authentication failed - GROQ_API_KEY may be invalid");
                }
                else if ((int)response.StatusCode == 429)
                {
                    _logger?.LogWarning("[Groq] Rate limit exceeded or out of credits");
                }

                return null;
            }

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var aiResult = jsonResponse.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            _logger?.LogInformation("[AI Response]: {AIResult}", aiResult);

            // DIAGNOSTIC: Log the extracted data sent to AI
            var extractedSample = extractedData.Length > 1000 ? extractedData.Substring(0, 1000) + "..." : extractedData;
            _logger?.LogDebug("[DIAGNOSTIC] Extracted data sent to Groq ({Length} chars): {Sample}", extractedData.Length, extractedSample);

            var parsed = ParseAIResponse(aiResult);
            if (parsed == null)
            {
                _logger?.LogWarning("[DIAGNOSTIC] ParseAIResponse returned NULL for AI result: '{AIResult}'. This means the AI could not extract valid distance/time from the HTML", aiResult);
            }
            else
            {
                _logger?.LogInformation("[SUCCESS] Parsed leader data: {Distance} km, Time: {Time}", parsed.DistanceKm, parsed.ElapsedTime);
            }

                return parsed;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[AnalyzeWithAgent error]: {Message}", ex.Message);
            return null;
        }
    }

    private string ExtractRaceData(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sections = new List<string>();

        // Provider detection
        string provider = html.Contains("live.skiklasserna.se") || html.Contains("skiclassics") ? "SkiClassics"
                        : html.Contains("live.eqtiming.com") || html.Contains("eqtiming") ? "EQTiming"
                        : "Unknown";

        _logger?.LogInformation("[Provider] Detected: {Provider}", provider);
        sections.Add($"PROVIDER: {provider}");

        // Extract page title for event context
        var titleMatch = Regex.Match(html, @"<title>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (titleMatch.Success)
        {
            sections.Add($"Page Title: {titleMatch.Groups[1].Value.Trim()}");
            _logger?.LogInformation("[Title] {Title}", titleMatch.Groups[1].Value.Trim());
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
                    _logger?.LogInformation("[SkiClassics] Found {Count} checkpoints", checkpointMatches.Count);
                }
                else
                {
                    _logger?.LogWarning("[SkiClassics] WARNING: No checkpoints found in h3/h4 headings");
                }

                // Also look for active checkpoint marker
                var activeMatch = Regex.Match(html, @"data-checkpoint-active=""true""[^>]*>.*?<h[34][^>]*>\s*([\d.,]+)\s*km", 
                                             RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (activeMatch.Success)
                {
                    sections.Add($"\nActive Checkpoint: {activeMatch.Groups[1].Value} km");
                    _logger?.LogInformation("[SkiClassics] Active checkpoint: {Distance} km", activeMatch.Groups[1].Value);
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
                _logger?.LogInformation("[EQTiming] Found {Count} unique race distances", distances.Count);
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
                        _logger?.LogInformation("[EQTiming] Found {Count} Point column values, showing first 10", pointMatches.Count);

                        // If we see "Mål" (Finish), note it
                        if (pointValues.Any(v => v.Contains("Mål") || v.Contains("mal")))
                        {
                            sections.Add($"\n[Note: 'Mål' (Finish) detected - leader has finished race]");
                            _logger?.LogInformation("[EQTiming] Detected 'Mål' (Finish) in Point column");
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("[EQTiming] WARNING: No Point column values found");
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
            _logger?.LogDebug("[Table] Trying class-based pattern");
        }

        // Pattern 3: If still no match, look for any table with tbody
        if (!tableMatch.Success)
        {
            tableMatch = Regex.Match(html, @"<table[^>]*>.*?<tbody>.*?</tbody>.*?</table>", 
                                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            _logger?.LogDebug("[Table] Trying tbody pattern");
        }
        
        if (tableMatch.Success)
        {
            var tableHtml = tableMatch.Value;
            _logger?.LogInformation("[Table] Found table with {Length} chars", tableHtml.Length);

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
                _logger?.LogInformation("[Table] Extracted {Count} rows from {Total} total", Math.Min(20, rowMatches.Count), rowMatches.Count);
                }
            }
            else
            {
                _logger?.LogWarning("[Table] WARNING: No results table found with any pattern");
            }

            var result = string.Join("\n", sections);

            // Truncate if still too large (stay under 20K for AI token limits)
            if (result.Length > 20000)
            {
                result = result.Substring(0, 20000) + "\n... [truncated]";
                _logger?.LogInformation("[ExtractRaceData] Truncated output to 20K chars");
            }
            else
            {
                _logger?.LogInformation("[ExtractRaceData] Output size: {Length} chars", result.Length);
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
            _logger?.LogInformation("[Browserless] Calling /content API for {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://chrome.browserless.io/content?token={apiToken}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            _logger?.LogInformation("[Browserless] Response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorPreview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                _logger?.LogError("[Browserless] Error response: {Error}", errorPreview);

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

                    _logger?.LogInformation("[Browserless] Success - received {Length} chars", responseBody.Length);
                    return responseBody;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[Browserless] Exception: {Message}", ex.Message);
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
            _logger?.LogInformation("[ScraperService] Calling {Endpoint} for {Url}", serviceEndpoint, url);

            // First, test if the service is reachable with a health check
            try
            {
                var healthEndpoint = $"{scraperServiceUrl.TrimEnd('/')}/health";
                _logger?.LogDebug("[ScraperService] Testing health endpoint: {Endpoint}", healthEndpoint);
                using var healthRequest = new HttpRequestMessage(HttpMethod.Get, healthEndpoint);
                using var healthResponse = await _httpClient.SendAsync(healthRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!healthResponse.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("[ScraperService] WARNING: Health check failed with status {StatusCode}", healthResponse.StatusCode);
                }
                else
                {
                    _logger?.LogInformation("[ScraperService] Health check passed");
                }
            }
            catch (HttpRequestException healthEx)
            {
                _logger?.LogError(healthEx, "[ScraperService] ERROR: Cannot reach scraper service at {Url}. Health check error: {Message}", scraperServiceUrl, healthEx.Message);
                throw new Exception($"Scraper service is not reachable at {scraperServiceUrl}. Ensure the service is running. Error: {healthEx.Message}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, serviceEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("User-Agent", "VasaLiveFeeder/1.0");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            _logger?.LogInformation("[ScraperService] Response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorPreview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                _logger?.LogError("[ScraperService] Error response: {Error}", errorPreview);
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

            _logger?.LogInformation("[ScraperService] Success - received {Length} chars in {Duration}ms", html?.Length ?? 0, duration);

                return html ?? string.Empty;
            }
            catch (HttpRequestException httpEx)
            {
                _logger?.LogError(httpEx, "[ScraperService] HTTP Exception: {Message}. Failed to connect to scraper service at {Url}. Ensure the service is running and accessible.", httpEx.Message, scraperServiceUrl);
                throw new Exception($"Failed to connect to scraper service at {scraperServiceUrl}: {httpEx.Message}. Ensure the service is running and accessible.", httpEx);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ScraperService] Exception: {Message}", ex.Message);
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
