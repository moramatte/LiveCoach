# Playwright Scraper Service

A lightweight HTTP service that renders JavaScript-heavy web pages using Playwright/Chromium.

## Features
- Simple REST API for rendering web pages
- Handles JavaScript execution and waits for network idle
- Returns fully rendered HTML
- Lightweight and fast (~3-5 seconds per render)

## API

### Health Check
```bash
GET /health
```

Response:
```json
{
  "status": "healthy",
  "service": "playwright-scraper"
}
```

### Render Page
```bash
POST /render
Content-Type: application/json

{
  "url": "https://example.com",
  "waitUntil": "networkidle"
}
```

Response:
```json
{
  "success": true,
  "html": "<html>...</html>",
  "url": "https://example.com",
  "duration": 3245,
  "size": 125678
}
```

## Local Development

### Prerequisites
- Node.js 18+
- npm

### Run Locally
```bash
cd PlaywrightScraper
npm install
npm start
```

Test it:
```bash
curl -X POST http://localhost:3000/render \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com"}'
```

## Docker

### Build Image
```bash
docker build -t playwright-scraper:latest .
```

### Run Container
```bash
docker run -p 3000:3000 playwright-scraper:latest
```

## Azure Deployment

See [DEPLOYMENT.md](./DEPLOYMENT.md) for instructions on deploying to Azure Container Instances.

## Cost Estimation

Running on Azure Container Instance:
- 1 vCPU, 2 GB RAM
- ~$0.16/hour
- ~$0.70 per 4-hour race (with 5-minute scrape intervals)
