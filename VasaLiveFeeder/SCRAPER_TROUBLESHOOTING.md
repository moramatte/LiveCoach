# Scraper Container Troubleshooting Guide

## Issue Summary

The scraper is failing with the error:
```
Could not extract leader distance from race page.
```

This typically means one of the following:
1. The scraper service (Playwright container) is not running
2. The GROQ_API_KEY is not configured (required for AI extraction)
3. The scraper service cannot reach the race website
4. The HTML content is being returned but AI extraction is failing

## Quick Diagnostic

Run the diagnostic script to identify the issue:

```powershell
.\diagnose-scraper-issue.ps1
```

This will test:
- Environment variables
- Scraper service connectivity
- HTML rendering
- GROQ API connectivity
- Configuration files

## Common Issues and Solutions

### 1. GROQ_API_KEY Not Set

**Symptoms:**
- `[AnalyzeWithAgent]: No GROQ_API_KEY found` in logs
- Scraper returns null even though HTML is fetched

**Solution:**
1. Get a free API key from https://console.groq.com/keys
2. Add it to `VasaLiveFeeder/local.settings.json`:
```json
{
  "Values": {
    "GROQ_API_KEY": "your-key-here"
  }
}
```
3. Or set environment variable: `$env:GROQ_API_KEY = "your-key-here"`

### 2. Scraper Service Not Running

**Symptoms:**
- `Cannot reach scraper service` error
- Connection refused errors
- Health check fails

**Solution - Option A (Local Development):**
```powershell
cd PlaywrightScraper
npm install
npm start
```

**Solution - Option B (Docker):**
```powershell
# Build the container
docker build -t playwright-scraper ./PlaywrightScraper

# Run the container
docker run -p 3000:3000 playwright-scraper
```

**Solution - Option C (Azure Container Instance):**
```powershell
# Deploy to Azure (see PLAYWRIGHT_SCRAPER_SETUP.md)
.\deploy-playwright-scraper.ps1
```

### 3. Wrong SCRAPER_SERVICE_URL

**Symptoms:**
- Service runs but connection fails
- Wrong port or URL

**Solution:**
Update `VasaLiveFeeder/local.settings.json`:
```json
{
  "Values": {
    "SCRAPER_SERVICE_URL": "http://localhost:3000"
  }
}
```

For Azure deployment:
```json
{
  "Values": {
    "SCRAPER_SERVICE_URL": "https://your-container.azurecontainer.io"
  }
}
```

### 4. Playwright Fallback Issues

**Symptoms:**
- `[Playwright] Attempting fallback` in logs
- Scraper service not configured, trying local Playwright

**Solution:**
If Playwright browsers are not installed locally:
```powershell
# Install Playwright browsers
npx playwright install chromium
```

Or better: Use the scraper service (Docker/Azure) instead of local Playwright.

## Testing the Full Flow

### Step 1: Test Scraper Service Directly

```powershell
# Test health endpoint
curl http://localhost:3000/health

# Test render endpoint
curl -X POST http://localhost:3000/render `
  -H "Content-Type: application/json" `
  -d '{"url":"https://live.eqtiming.com/76514#result","waitUntil":"networkidle"}'
```

### Step 2: Test Azure Function Locally

```powershell
cd VasaLiveFeeder
func start

# Test in another terminal
curl 'http://localhost:7071/api/TempoDelta?raceName=vasaloppet&progressInKm=45&currentSpeed=12'
```

### Step 3: Check Logs

Look for these key log entries:
- `[GetLeaderDataWithScraperAsync] BROWSERLESS_TOKEN present: ...`
- `[GetLeaderDataWithScraperAsync] GROQ_API_KEY present: ...`
- `[ScraperService] Health check passed`
- `[ScraperService] Success - received ... chars`
- `[AI Response]: distance: XX.X, time: H:MM:SS`

## Environment Variable Priority

The scraper uses these variables in order:

1. **SCRAPER_SERVICE_URL** - URL of the Playwright scraper service
   - If set: Uses the scraper service (preferred)
   - If not set: Falls back to local Playwright (slower, requires browsers installed)

2. **BROWSERLESS_TOKEN** - Token for Browserless.io service
   - Used as alternative to scraper service (paid service)
   - Usually not needed if scraper service is configured

3. **GROQ_API_KEY** - API key for Groq AI service
   - **REQUIRED** for AI extraction of race data
   - Without this, extraction will fail even if HTML is fetched successfully

## Architecture Overview

```
Azure Function
    ? (calls)
ServiceLocator.Resolve<ILiveScraper>()
    ?
LiveScraper
    ? (checks)
SCRAPER_SERVICE_URL set?
    ? YES
Playwright Scraper Service (Docker/Azure)
    ? (returns HTML)
LiveScraper.AnalyzeWithAgentAsync()
    ? (requires)
GROQ_API_KEY
    ? (returns)
LeaderData (distance + time)
```

## Quick Start Checklist

- [ ] GROQ_API_KEY is set in local.settings.json
- [ ] Scraper service is running (Docker or npm start)
- [ ] SCRAPER_SERVICE_URL points to running service
- [ ] Scraper service health check passes
- [ ] Azure Function starts successfully
- [ ] Test request returns valid data

## Still Having Issues?

1. Run `.\diagnose-scraper-issue.ps1` and check the output
2. Check the saved `scraper-output.html` file to verify HTML content
3. Look at Azure Function logs for detailed error messages
4. Verify network connectivity between Function and Scraper Service
5. Test GROQ API directly with curl to verify the key works

## Related Files

- `VasaLiveFeeder/local.settings.json` - Local configuration
- `VasaLiveFeeder/LiveScraper/LiveScraper.cs` - Main scraper implementation
- `PlaywrightScraper/server.js` - Scraper service implementation
- `diagnose-scraper-issue.ps1` - Diagnostic tool
- `test-scraper-service.ps1` - Service testing tool
- `PLAYWRIGHT_SCRAPER_SETUP.md` - Azure deployment guide
