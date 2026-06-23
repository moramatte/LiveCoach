# Quick Deploy Script for VasaLiveFeeder

# Run this script from the VasaLiveFeeder solution directory

Write-Host "Building and deploying VasaLiveFeeder to Azure..." -ForegroundColor Green

# Navigate to project
Set-Location "VasaLiveFeeder\VasaLiveFeeder"

# Clean and publish
Write-Host "`n1. Publishing project..." -ForegroundColor Yellow
dotnet clean --verbosity quiet
dotnet publish VasaLiveFeeder.csproj -c Release -o .\publish-output --verbosity minimal

# Create deployment package
Write-Host "`n2. Creating deployment package..." -ForegroundColor Yellow
Remove-Item deploy.zip -ErrorAction SilentlyContinue
Compress-Archive -Path ".\publish-output\*" -DestinationPath "deploy.zip" -Force
Write-Host "   Package size: $([math]::Round((Get-Item deploy.zip).Length / 1MB, 2)) MB"

# Deploy to Azure
Write-Host "`n3. Deploying to Azure Function App..." -ForegroundColor Yellow
az functionapp deployment source config-zip `
	--resource-group VasaResources `
	--name VasaLiveFeeder `
	--src "deploy.zip" `
	--build-remote false `
	--timeout 600 `
	--output none

# Wait for function registration
Write-Host "`n4. Waiting for functions to register..." -ForegroundColor Yellow
Start-Sleep -Seconds 20

# Verify deployment
Write-Host "`n5. Verifying deployment..." -ForegroundColor Yellow
$health = curl.exe -s https://vasalivefeeder.azurewebsites.net/api/Health
if ($health -eq "OK") {
	Write-Host "   ✓ Health check: PASSED" -ForegroundColor Green
} else {
	Write-Host "   ✗ Health check: FAILED (Response: $health)" -ForegroundColor Red
}

# Test TempoDelta
$test = curl.exe -s "https://vasalivefeeder.azurewebsites.net/api/TempoDelta?race=test20k&progressInKm=2.5&dryRun=true"
if ($test -like "*newSpeed*") {
	Write-Host "   ✓ TempoDelta endpoint: WORKING" -ForegroundColor Green
} else {
	Write-Host "   ✗ TempoDelta endpoint: FAILED (Response: $test)" -ForegroundColor Red
}

# List registered functions
Write-Host "`n6. Registered functions:" -ForegroundColor Yellow
az functionapp function list --name VasaLiveFeeder --resource-group VasaResources --query "[].{Name:name}" --output table

Write-Host "`nDeployment complete!" -ForegroundColor Green
Write-Host "Endpoints:" -ForegroundColor Cyan
Write-Host "  - Health:     https://vasalivefeeder.azurewebsites.net/api/Health"
Write-Host "  - TempoDelta: https://vasalivefeeder.azurewebsites.net/api/TempoDelta"
