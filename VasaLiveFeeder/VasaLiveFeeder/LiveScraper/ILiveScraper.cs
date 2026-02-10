namespace VasaLiveFeeder.LiveScraper;

/// <summary>
/// Scrapes live race data from timing websites.
/// Supports multiple rendering methods:
/// 1. Browserless.io API (set BROWSERLESS_TOKEN env var) - JavaScript support, no local browsers
/// 2. Playwright (local browsers) - JavaScript support, requires installation
/// 3. Basic HTTP - Static HTML only
/// 4. AI Analysis (Groq) - Set GROQ_API_KEY for intelligent parsing
/// </summary>
public interface ILiveScraper
{
    /// <summary>
    /// Gets leader data (distance and time) using basic HTTP or Browserless if configured.
    /// Tries AI analysis, falls back to regex parsing.
    /// </summary>
    Task<LeaderData?> GetLeaderDataAsync(string url);
    
    /// <summary>
    /// Gets leader data (distance and time) using the scraper (Browserless or Playwright fallback).
    /// Tries Browserless first if BROWSERLESS_TOKEN is set, falls back to Playwright if needed.
    /// Tries AI analysis, falls back to regex parsing.
    /// </summary>
    Task<LeaderData?> GetLeaderDataWithScraperAsync(string url, int timeoutMs = 60000);
    
    /// <summary>
    /// Uses AI (Groq) to analyze HTML content and extract leader data.
    /// Requires GROQ_API_KEY environment variable.
    /// </summary>
    Task<LeaderData?> AnalyzeWithAgentAsync(string htmlContent);
}
