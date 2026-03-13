# Scraper Container Issue - Fix Summary

## Problem
The Azure Function was failing with the error:
```
Could not extract leader distance from race page.
at VasaLiveFeeder.Function1.DeriveDistanceDelta(String raceName, Double myProgress)
```

## Root Cause Analysis

The scraper system has multiple components that must all work together:

1. **Playwright Scraper Service** (container) - Renders JavaScript-heavy race websites
2. **GROQ AI Service** - Extracts structured data from HTML using AI
3. **Azure Function** - Orchestrates the scraping and calculation

The error "Could not extract leader distance" occurs when ANY of these fail:
- Scraper service is not running or unreachable
- GROQ_API_KEY is missing or invalid
- Network issues prevent fetching the race page
- AI extraction returns null results

## Changes Made

### 1. Enhanced Logging in Function1.cs
**File:** `VasaLiveFeeder/Function1.cs`

Added detailed logging to track:
- Environment variable configuration (SCRAPER_SERVICE_URL, BROWSERLESS_TOKEN, GROQ_API_KEY)
- The race URL being scraped
- Success/failure of scraping operation
- Leader data extracted (distance, time)

This makes it much easier to diagnose where the failure occurs.

### 2. Improved Error Messages
**File:** `VasaLiveFeeder/Function1.cs`

Changed from generic errors to specific, actionable messages:
- Include the URL that failed
- Reference to check logs for more details
- Full exception details in response (during development)

### 3. Enhanced Scraper Service Connection Handling
**File:** `VasaLiveFeeder/LiveScraper/LiveScraper.cs`

Added:
- Health check before attempting to render
- Detailed HTTP error logging
- Connection failure detection with clear error messages
- Stack traces for debugging

### 4. Created Diagnostic Tools

#### diagnose-scraper-issue.ps1
Comprehensive diagnostic script that tests:
- All environment variables
- Scraper service health
- Actual rendering with a race URL
- GROQ API connectivity
- Configuration files

**Usage:**
```powershell
.\diagnose-scraper-issue.ps1
```

#### check-scraper-setup.ps1
Quick pre-flight check before starting the Azure Function:
- Verifies GROQ_API_KEY is set
- Checks scraper service is running
- Validates configuration

**Usage:**
```powershell
.\check-scraper-setup.ps1
```

### 5. Created Documentation

#### SCRAPER_TROUBLESHOOTING.md
Complete troubleshooting guide covering:
- Common issues and solutions
- Environment variable setup
- Testing procedures
- Architecture overview
- Quick start checklist

## How to Use

### Before Starting Development

1. Run the quick check:
```powershell
.\check-scraper-setup.ps1
```

2. If issues are found, run full diagnostics:
```powershell
.\diagnose-scraper-issue.ps1
```

3. Fix any issues identified (usually GROQ_API_KEY or scraper service not running)

### Starting the Services

1. Start the scraper service:
```powershell
cd PlaywrightScraper
npm start
# OR
docker run -p 3000:3000 playwright-scraper
```

2. Start the Azure Function:
```powershell
cd VasaLiveFeeder
func start
```

3. Test it:
```powershell
curl 'http://localhost:7071/api/TempoDelta?raceName=vasaloppet&progressInKm=45&currentSpeed=12'
```

### Interpreting Logs

Look for these key indicators in the logs:

**Success:**
```
[GetLeaderDataWithScraperAsync] GROQ_API_KEY present: True
[ScraperService] Health check passed
[ScraperService] Success - received 45231 chars in 2341ms
[AI Response]: distance: 45.0, time: 2:15:30
Successfully scraped leader data: 45 km, Time: 02:15:30
```

**GROQ API Key Missing:**
```
[GetLeaderDataWithScraperAsync] GROQ_API_KEY present: False
[AnalyzeWithAgent]: No GROQ_API_KEY found
```

**Scraper Service Not Running:**
```
[ScraperService] ERROR: Cannot reach scraper service at http://localhost:3000
Health check error: No connection could be made...
```

**AI Extraction Failed:**
```
[Groq] Processing 12453 chars
[AI Response]: distance: null, time: null
[Playwright] AI extraction returned null
```

## Most Common Issue

**The #1 cause of failure is missing GROQ_API_KEY.**

**Quick Fix:**
1. Get a free API key from https://console.groq.com/keys
2. Add to `VasaLiveFeeder/local.settings.json`:
```json
{
  "Values": {
    "GROQ_API_KEY": "gsk_your-actual-key-here"
  }
}
```
3. Restart the Azure Function

## Testing Without Live Data

If you want to test without hitting live race websites, you can use the test files:
- `VasaLiveFeeder.Tests/LiveScraperTest.cs` - Unit tests with saved HTML
- `VasaLiveFeeder.Tests/LiveDataSamples/*.html` - Sample race pages

Run tests:
```powershell
cd VasaLiveFeeder.Tests
dotnet test
```

## Next Steps

1. **If you still get "Could not extract leader distance":**
   - Run `.\diagnose-scraper-issue.ps1`
   - Check the saved `scraper-output.html` file
   - Verify the HTML contains race data
   - Test GROQ API directly

2. **For production deployment:**
   - Deploy scraper service to Azure Container Instance
   - Set SCRAPER_SERVICE_URL to the Azure container URL
   - Set GROQ_API_KEY in Azure Function app settings
   - Test end-to-end with production URLs

3. **For monitoring:**
   - Enable Application Insights on the Azure Function
   - Set up alerts for scraper failures
   - Monitor GROQ API usage/quota

## Files Modified

- ?? `VasaLiveFeeder/Function1.cs` - Enhanced logging and error handling
- ?? `VasaLiveFeeder/LiveScraper/LiveScraper.cs` - Improved connection handling
- ? `diagnose-scraper-issue.ps1` - Diagnostic tool
- ? `check-scraper-setup.ps1` - Quick check tool
- ? `SCRAPER_TROUBLESHOOTING.md` - Troubleshooting guide
- ? `SCRAPER_FIX_SUMMARY.md` - This file

## Build Status

? All changes compile successfully
? No breaking changes to existing code
? Enhanced debugging capabilities added
? New diagnostic tools available
