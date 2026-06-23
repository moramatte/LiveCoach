# VasaLiveFeeder - Recent Changes

## Latest Update: 2026-06-23

### New Feature: Configurable Medal Time Percentage

**What changed:**
- Added new `medalTimePct` parameter to allow clients to configure the medal time multiplier
- Previously hard-coded as +50% (1.5x multiplier)
- Now clients can specify any percentage from 0-500%

**Parameter names** (all equivalent):
- `medalTimePct` (preferred)
- `medalPct`
- `medal`

**Examples:**
```
# Original behavior (50% slower than medal time)
?medalTimePct=50

# Faster target (25% slower than medal time)
?medalTimePct=25

# More relaxed target (75% slower than medal time)
?medalTimePct=75

# Medal time exactly (0% slower)
?medalTimePct=0
```

**Default behavior:**
- If parameter is not provided: defaults to `50` (same as before)
- This maintains backward compatibility with existing clients

**Code changes:**
- `Function1.cs`: Added medalTimePct parsing and validation
- `DeriveTempoDelta()`: Changed from hard-coded `timeMultiplier = 1.5` to `timeMultiplier = 1.0 + (medalTimePct / 100.0)`
- Input validation: Accepts values 0-500 (percent)

### Infrastructure Changes

**Migrated from Linux to Windows Function App:**
- **Why:** Linux Function Apps had severe deployment issues
  - CLI tools reported success but didn't actually deploy
  - Functions never registered in Azure
  - Multiple deployment methods all failed silently
- **Impact:** No functional changes - Playwright scraper runs in separate container
- **Result:** Deployment now works reliably via Azure CLI

**Storage cleanup:**
- Deleted 77 old deployment packages (2.7 GB → 101 MB)
- Implemented automatic lifecycle policy (auto-delete after 30 days)
- Significant cost reduction expected

**Resources removed:**
- Old Linux Function App (VasaLiveFeeder - Linux)
- Duplicate App Service Plan (SwedenCentralLinuxDynamicPlan)
- Orphaned Application Insights components

**Resources remaining:**
- VasaLiveFeeder (Windows Function App)
- vasalivestorage (Storage account)
- Application Insights + Log Analytics
- One App Service Plan (Consumption tier)

### Deployment Process

**New reliable deployment method:**
1. `dotnet publish` (builds Release to `publish-output`)
2. Compress to zip
3. `az functionapp deployment source config-zip`
4. Functions register automatically within 20 seconds

See `DEPLOYMENT.md` for detailed instructions.

See `deploy.ps1` for one-command deployment script.

### API Compatibility

**✅ Fully backward compatible:**
- Existing clients that don't send `medalTimePct` will get default behavior (50%)
- All existing parameter names still work
- Response format unchanged

**✅ Enhanced validation:**
- Better error messages for missing/invalid parameters
- Clear indication of which parameter is problematic
- Proper handling of null values in dryRun mode

### Testing

All functionality tested and verified:
- ✅ Health endpoint returns "OK"
- ✅ TempoDelta with dryRun (no elapsedTime required)
- ✅ TempoDelta with medalTimePct parameter
- ✅ TempoDelta without medalTimePct (defaults to 50)
- ✅ Parameter validation and error messages
- ✅ Multiple parameter name aliases work

### Known Issues

None currently.

### Next Steps / Future Improvements

Consider:
- Add OpenAPI/Swagger documentation
- Add metrics/telemetry for medalTimePct usage
- Consider adding min/max speed guardrails
- Add more race profiles
