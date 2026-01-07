using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Infrastructure.Logger;

namespace VasaLiveFeeder;

/// <summary>
/// Fetches live race information and extracts how far the leader has come (in kilometers).
/// </summary>
public class LiveScraper
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Create a new scraper using the provided <see cref="HttpClient"/>.
    /// </summary>
    public LiveScraper(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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

        string content;
        try
        {
            content = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return ParseContent(content);
    }

    /// <summary>
    /// Renders the page with Playwright (executes JavaScript) and runs the same parsing logic
    /// against the rendered HTML. This requires Playwright browser binaries to be installed on the machine.
    /// </summary>
    public async Task<double?> GetLeaderDistanceWithPlaywrightAsync(string url, int timeoutMs = 30000)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("url is required", nameof(url));
        // Ensure Playwright browser binaries are installed. Prefer API call when available,
        // otherwise fall back to running the generated installer script (playwright.ps1).
        await EnsurePlaywrightBrowsersInstalledAsync().ConfigureAwait(false);

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
        var page = await browser.NewPageAsync();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = timeoutMs });
        var content = await page.ContentAsync();
        return ParseContent(content);
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

    //private void AnalyzeWithML(string context)
    //{
    //    var logText = string.Empty;
    //    Log.Info(GetType(), $"Initiating ML analysis: context: {context}. {Environment.NewLine}logText length: {logText.Length}");

    //    var tokenLimit = 2000;
    //    var tokens = Infrastructure.Utilities.TokenEvaluator.Evaluate(logText);
    //    if (tokens > tokenLimit)
    //    {
    //        logText = Infrastructure.Utilities.TokenEvaluator.Reduce(logText, tokenLimit);
    //    }

    //    var requestBody = new
    //    {
    //        model = Config.ModelName,
    //        messages = new[]
    //        {
    //            new { role = "system", content = context },
    //            new { role = "user", content = logText }
    //        },
    //        max_tokens = 256
    //    };

    //    try
    //    {
    //        var payload = requestBody.ToJson();

    //        using var request = new HttpRequestMessage(HttpMethod.Post, foundryLocalEndpoint)
    //        {
    //            Content = new StringContent(payload, Encoding.UTF8, "application/json")
    //        };
    //        using var response = await httpClient.SendAsync(request);
    //        if (!response.IsSuccessStatusCode)
    //            return $"[Phi model error: {response.StatusCode} - {response.ReasonPhrase}]";
    //        var responseString = await response.Content.ReadAsStringAsync();
    //        using var doc = System.Text.Json.JsonDocument.Parse(responseString);
    //        var content = doc.RootElement
    //            .GetProperty("choices")[0]
    //            .GetProperty("message")
    //            .GetProperty("content").GetString();
    //        return content ?? "[No response from model]";
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error(GetType(), $"Error calling model {Config.ModelName}", ex);
    //        return $"[Exception: {ex.Message}]";
    //    }
    
    //}

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
        try
        {
            // Avoid repeating installation attempts in the same process
            lock (s_playwrightInstallLock)
            {
                if (s_playwrightInstallChecked) return;
            }

            var playwrightType = Type.GetType("Microsoft.Playwright.Playwright, Microsoft.Playwright");
            var installMethod = playwrightType?.GetMethod("InstallAsync", BindingFlags.Public | BindingFlags.Static);
            if (installMethod != null)
            {
                var installTask = (Task)installMethod.Invoke(null, null)!;
                await installTask.ConfigureAwait(false);
                lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
                return;
            }

            // Fallback: run the generated PowerShell installer script next to the executing assembly.
            var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var scriptPath = Path.Combine(baseDir, "playwright.ps1");
            if (!File.Exists(scriptPath))
            {
                // No script and no API installer available; nothing we can do here.
                lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
                return;
            }

            // Try common PowerShell executables. pwsh (PowerShell Core) is preferred, fall back to Windows PowerShell.
            var candidates = new[] { "pwsh", "powershell.exe" };
            Exception? lastEx = null;
            foreach (var cmd in candidates)
            {
                try
                {
                    var args = cmd.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
                        ? $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" install"
                        : $"-ExecutionPolicy Bypass -File \"{scriptPath}\" install";

                    var psi = new ProcessStartInfo(cmd, args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    await p.WaitForExitAsync().ConfigureAwait(false);
                    if (p.ExitCode != 0)
                    {
                        var err = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
                        throw new InvalidOperationException($"Playwright install script failed ({cmd}): {err}");
                    }

                    lock (s_playwrightInstallLock) { s_playwrightInstallChecked = true; }
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    // try next candidate
                }
            }

            throw new InvalidOperationException($"Could not run the Playwright installer script (playwright.ps1). Tried executables: {string.Join(", ", candidates)}. Ensure PowerShell is installed and on PATH.", lastEx);
        }
        catch (Exception ex)
        {
            // Bubble up so caller sees the real installation problem instead of a later file-not-found.
            throw new InvalidOperationException("Failed to ensure Playwright browser binaries are installed.", ex);
        }
    }
}
