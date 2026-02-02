const express = require('express');
const { chromium } = require('playwright');

const app = express();
const PORT = process.env.PORT || 3000;

// Middleware to parse JSON
app.use(express.json());

// Health check endpoint
app.get('/health', (req, res) => {
  res.json({ status: 'healthy', service: 'playwright-scraper' });
});

// Main render endpoint
app.post('/render', async (req, res) => {
  const startTime = Date.now();
  let browser = null;
  
  try {
    const { url, waitUntil = 'networkidle' } = req.body;
    
    if (!url) {
      return res.status(400).json({ error: 'Missing required field: url' });
    }
    
    console.log(`[${new Date().toISOString()}] Rendering: ${url}`);
    
    // Launch browser
    browser = await chromium.launch({
      headless: true,
      args: [
        '--no-sandbox',
        '--disable-setuid-sandbox',
        '--disable-dev-shm-usage',
        '--disable-gpu'
      ]
    });
    
    const context = await browser.newContext({
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'
    });
    
    const page = await context.newPage();
    
    // Set timeout - increased to accommodate the additional 13s of waits
    page.setDefaultTimeout(90000);

    // Navigate to URL and wait for network to be idle
    await page.goto(url, {
      waitUntil: 'networkidle',
      timeout: 90000
    });

    // Additional wait for JavaScript to populate dynamic content
    // EQTiming and SkiClassics load results via JavaScript after page load
    console.log(`[${new Date().toISOString()}] Waiting for JavaScript to execute...`);
    await page.waitForTimeout(3000); // Wait 3 seconds for initial JS execution

    // Try to wait for common table elements with longer timeout
    try {
      await page.waitForSelector('table tbody tr, .col-point-scroll, [data-checkpoint]', { 
        timeout: 8000  // Increased to 8 seconds
      });
      console.log(`[${new Date().toISOString()}] Results table detected, waiting for data population...`);

      // Give extra time for table data to populate
      await page.waitForTimeout(2000);

    } catch (e) {
      console.log(`[${new Date().toISOString()}] No results table found after wait, continuing anyway`);
      // Still wait a bit in case data loads without the selector
      await page.waitForTimeout(2000);
    }

    // Get rendered HTML
    const html = await page.content();
    console.log(`[${new Date().toISOString()}] Retrieved HTML content (${html.length} chars)`);
    
    await browser.close();
    browser = null;
    
    const duration = Date.now() - startTime;
    console.log(`[${new Date().toISOString()}] Successfully rendered ${url} in ${duration}ms (${html.length} chars)`);
    
    // Return HTML content
    res.json({
      success: true,
      html: html,
      url: url,
      duration: duration,
      size: html.length
    });
    
  } catch (error) {
    console.error(`[${new Date().toISOString()}] Error rendering page:`, error.message);
    
    if (browser) {
      try {
        await browser.close();
      } catch (closeError) {
        console.error('Error closing browser:', closeError.message);
      }
    }
    
    res.status(500).json({
      success: false,
      error: error.message,
      duration: Date.now() - startTime
    });
  }
});

// Start server
app.listen(PORT, '0.0.0.0', () => {
  console.log(`Playwright Scraper Service running on port ${PORT}`);
  console.log(`Health check: http://localhost:${PORT}/health`);
  console.log(`Render endpoint: POST http://localhost:${PORT}/render`);
});

// Graceful shutdown
process.on('SIGTERM', () => {
  console.log('SIGTERM received, shutting down gracefully...');
  process.exit(0);
});

process.on('SIGINT', () => {
  console.log('SIGINT received, shutting down gracefully...');
  process.exit(0);
});
