# VasaLiveFeeder Azure Function - Deployment Guide

## Important: This is a WINDOWS Function App

The VasaLiveFeeder Function App runs on **Windows (not Linux)**. This is crucial for deployment to work correctly.

## Prerequisites

- Azure CLI installed and logged in (`az login`)
- .NET 8 SDK installed
- Access to the `VasaResources` resource group in Azure

## Quick Deployment (Recommended Method)

### 1. Build and Publish Locally

```powershell
# Navigate to the project directory
cd VasaLiveFeeder\VasaLiveFeeder

# Clean and publish
dotnet clean
dotnet publish VasaLiveFeeder.csproj -c Release -o .\publish-output
```

### 2. Create Deployment Package

```powershell
# Remove old zip if exists
Remove-Item deploy.zip -ErrorAction SilentlyContinue

# Create zip from publish output
Compress-Archive -Path ".\publish-output\*" -DestinationPath "deploy.zip" -Force
```

### 3. Deploy to Azure

```powershell
# Deploy using Azure CLI (this works reliably for Windows Function Apps)
az functionapp deployment source config-zip `
	--resource-group VasaResources `
	--name VasaLiveFeeder `
	--src "deploy.zip" `
	--build-remote false `
	--timeout 600
```

### 4. Verify Deployment

```powershell
# Wait a moment for functions to register
Start-Sleep -Seconds 20

# Check registered functions
az functionapp function list --name VasaLiveFeeder --resource-group VasaResources --output table

# Test Health endpoint
curl https://vasalivefeeder.azurewebsites.net/api/Health

# Test TempoDelta endpoint
curl "https://vasalivefeeder.azurewebsites.net/api/TempoDelta?race=test20k&progressInKm=2.5&dryRun=true"
```

## Alternative: Visual Studio Publish

If the Azure CLI method fails:

1. Open the solution in Visual Studio
2. Right-click the `VasaLiveFeeder` project (not the solution)
3. Select **Publish...**
4. Select the existing `VasaLiveFeeder` publish profile
5. Click **Publish**

Note: This should work but has been unreliable in VS 2026 Insiders.

## Troubleshooting

### Deployment says "Succeeded" but functions return 404

This typically means:
- You're deploying to a **Linux Function App** instead of Windows
- Solution: Delete the Linux app and recreate as Windows (see below)

### Creating a New Windows Function App

```powershell
# Create Windows Function App
az functionapp create `
	--resource-group VasaResources `
	--consumption-plan-location "swedencentral" `
	--runtime dotnet-isolated `
	--runtime-version 8 `
	--functions-version 4 `
	--name VasaLiveFeeder `
	--storage-account vasalivestorage `
	--os-type Windows

# Copy app settings from old app (if needed)
# Set GROQ_API_KEY, BROWSERLESS_TOKEN, SCRAPER_SERVICE_URL, OPENAI_API_KEY
az functionapp config appsettings set `
	--name VasaLiveFeeder `
	--resource-group VasaResources `
	--settings "GROQ_API_KEY=your-key" "BROWSERLESS_TOKEN=your-token" ...
```

### Linux Function Apps DON'T WORK

**Never create this as a Linux Function App!** Linux Function Apps have terrible deployment tooling and will fail in mysterious ways:
- `az functionapp deployment source config-zip` reports success but doesn't deploy
- Visual Studio Publish fails
- Kudu API returns 401 Unauthorized
- Functions don't register even after deployment

## Cost Management

### Automatic Cleanup of Old Deployments

Old deployment packages accumulate in blob storage and cost money. A lifecycle policy is configured to auto-delete packages older than 30 days.

To verify the policy is active:

```powershell
az storage account management-policy show `
	--account-name vasalivestorage `
	--resource-group VasaResources
```

### Manual Cleanup

If needed, manually delete old deployment packages:

```powershell
$key = az storage account keys list --account-name vasalivestorage --resource-group VasaResources --query "[0].value" --output tsv

# List all packages
az storage blob list `
	--account-name vasalivestorage `
	--account-key $key `
	--container-name function-releases `
	--output table

# Delete old packages (keep newest 3)
$allBlobs = az storage blob list --account-name vasalivestorage --account-key $key --container-name function-releases --query "[].name" -o json | ConvertFrom-Json
$toDelete = $allBlobs | Select-Object -Skip 3
foreach ($blob in $toDelete) {
	az storage blob delete --account-name vasalivestorage --account-key $key --container-name function-releases --name $blob
}
```

## Key Endpoints

- **Health Check:** https://vasalivefeeder.azurewebsites.net/api/Health
- **TempoDelta:** https://vasalivefeeder.azurewebsites.net/api/TempoDelta

### TempoDelta Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `race` or `raceName` | Yes | - | Race name (e.g., "vasaloppet", "test20k") |
| `progressInKm` or `progress` or `km` | Yes | - | Current distance in kilometers |
| `elapsedTime` or `elapsed` or `time` | No* | Estimated | Elapsed time in minutes (*required if `dryRun=false`) |
| `medalTimePct` or `medalPct` or `medal` | No | 50 | Medal time percentage (e.g., 50 = medal time + 50%) |
| `dryRun` | No | false | Use simulated data instead of live scraping |
| `currentSpeed` or `speed` | No | - | Current speed (optional, for logging) |

### Example Requests

**Dry run (no elapsedTime needed):**
```
GET /api/TempoDelta?race=test20k&progressInKm=2.5&dryRun=true
```

**Live with all parameters:**
```
GET /api/TempoDelta?race=vasaloppet&progressInKm=45.2&elapsedTime=180&medalTimePct=75
```

**JSON body:**
```json
POST /api/TempoDelta
{
  "raceName": "vasaloppet",
  "progressInKm": 45.2,
  "elapsedTime": 180,
  "medalTimePct": 75
}
```

## Architecture Notes

- **Function App:** Windows-based, .NET 8 isolated worker
- **Scraper Service:** Runs separately in a container (configured via `SCRAPER_SERVICE_URL`)
- **Playwright:** NOT bundled with the Function App - it calls an external scraper service
- **Storage:** Uses `vasalivestorage` for function metadata and deployment packages
- **App Service Plan:** Consumption (Dynamic) tier - only pay for execution time

## Lesson Learned

**Why Windows instead of Linux?**

The Function App was originally created as Linux, which caused endless deployment problems:
- Azure CLI tools reported success but didn't actually deploy
- Functions never registered in Azure
- wwwroot remained empty after every deployment attempt
- Multiple deployment methods (zipdeploy, config-zip, OneDeploy, Visual Studio) all failed

Switching to Windows solved all deployment issues immediately. Since the Playwright scraper runs in a separate container anyway, **there's no benefit to using Linux for this Function App**.

## Last Successful Deployment

- **Date:** 2026-06-23
- **Method:** `az functionapp deployment source config-zip`
- **Result:** Functions registered successfully, endpoints responding
- **Deployment Time:** ~30 seconds
