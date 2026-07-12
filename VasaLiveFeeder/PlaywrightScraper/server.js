const express = require('express');
const { chromium } = require('playwright');

const app = express();
const PORT = process.env.PORT || 3000;

async function dismissConsentPrompts(page) {
  const selectors = [
    'button.wcc-btn.wcc-btn-accept[data-tag="detail-accept-button"]',
    'button[aria-label="Accept All"][data-tag="detail-accept-button"]',
    'button.wcc-btn-accept',
    '.wcc-btn-accept',
    '[data-tag="detail-accept-button"]',
    '#onetrust-accept-btn-handler',
    'button#onetrust-accept-btn-handler',
    'button:has-text("Accept")',
    'button:has-text("I Agree")',
    'button:has-text("Agree")',
    'button:has-text("Accept all")',
    'button:has-text("Accept All")',
    'button:has-text("Accept all cookies")',
    'button:has-text("Allow all")',
    'button:has-text("Allow All")',
    'button:has-text("Consent")',
    'button:has-text("OK")',
    '[aria-label="Accept"]',
    '[aria-label="Accept all"]',
    '[title="Accept"]',
    '[title="Accept all"]',
    '[id*="accept"]',
    '[id*="consent"]',
    '[class*="accept"]',
    '[class*="consent"]',
    '[data-testid*="accept"]',
    '[data-testid*="consent"]'
  ];

  const privacyMarkers = [
    'We value your privacy',
    'Consent Preferences',
    'partners use cookies',
    'tracking technologies',
    'personalized ads and content'
  ];

  async function tryDismissInScope(scope, scopeName) {
    for (const selector of selectors) {
      try {
        const button = scope.locator(selector).first();
        if (await button.isVisible({ timeout: 1200 })) {
          await button.click({ timeout: 2500, force: true });
          console.log(`[${new Date().toISOString()}] Dismissed consent prompt in ${scopeName} using selector: ${selector}`);
          await page.waitForTimeout(1200);
          return true;
        }
      } catch {
        // try next selector
      }
    }

    try {
      const bodyText = await scope.locator('body').innerText({ timeout: 1500 });
      const hasPrivacyBanner = privacyMarkers.some(marker => bodyText.includes(marker));
      if (!hasPrivacyBanner) {
        return false;
      }

      console.log(`[${new Date().toISOString()}] Detected privacy banner text in ${scopeName}`);

      const fallbackButtons = scope.locator('button, [role="button"], input[type="button"], input[type="submit"]');
      const count = await fallbackButtons.count();
      for (let i = 0; i < count; i++) {
        try {
          const button = fallbackButtons.nth(i);
          const text = ((await button.innerText({ timeout: 500 }).catch(() => '')) || '').trim();
          const normalizedText = text.toLowerCase();
          if (normalizedText.includes('accept') || normalizedText.includes('agree') || normalizedText.includes('allow all') || normalizedText.includes('ok')) {
            await button.click({ timeout: 2500, force: true });
            console.log(`[${new Date().toISOString()}] Dismissed privacy banner in ${scopeName} using fallback button text: ${text}`);
            await page.waitForTimeout(1200);
            return true;
          }
        } catch {
          // continue
        }
      }
    } catch {
      // ignore scope text inspection failures
    }

    return false;
  }

  if (await tryDismissInScope(page, 'main page')) {
    return true;
  }

  for (const frame of page.frames().slice(1)) {
    if (await tryDismissInScope(frame, 'frame')) {
      return true;
    }
  }

  try {
    await page.evaluate(() => {
      const acceptSelectors = [
        'button.wcc-btn.wcc-btn-accept[data-tag="detail-accept-button"]',
        'button[aria-label="Accept All"][data-tag="detail-accept-button"]',
        'button.wcc-btn-accept',
        '.wcc-btn-accept',
        '[data-tag="detail-accept-button"]'
      ];

      for (const selector of acceptSelectors) {
        const el = document.querySelector(selector);
        if (el) {
          el.click();
        }
      }

      try {
        if (typeof window !== 'undefined') {
          const consentData = { action: 'accept_all', source: 'playwright' };
          window.localStorage?.setItem('wcc_consent', JSON.stringify(consentData));
          window.localStorage?.setItem('cookieyes-consent', JSON.stringify(consentData));
          document.cookie = 'cookieyes-consent=accept; path=/; max-age=31536000';
          document.cookie = 'wt_consent=yes; path=/; max-age=31536000';
        }
      } catch {}

      const overlaySelectors = [
        '.wcc-consent-container',
        '.wcc-overlay',
        '.wcc-banner-container',
        '.wcc-modal',
        '.wcc-prefrence-btn-wrapper',
        '.wcc-footer-wrapper',
        '#onetrust-banner-sdk',
        '.onetrust-pc-dark-filter',
        '.onetrust-pc-lightbox',
        '[id*="consent"]',
        '[class*="consent"]',
        '[id*="cookie"]',
        '[class*="cookie"]',
        '[aria-modal="true"]'
      ];

      for (const selector of overlaySelectors) {
        document.querySelectorAll(selector).forEach(el => el.remove());
      }

      if (document.body) {
        document.body.style.overflow = 'auto';
      }
      if (document.documentElement) {
        document.documentElement.style.overflow = 'auto';
      }
    });

    console.log(`[${new Date().toISOString()}] Applied aggressive consent acceptance + overlay removal fallback`);
    await page.waitForTimeout(1800);
  } catch {
    // ignore DOM cleanup failures
  }

  return false;
}

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

    // Dismiss cookie / agreement prompts that block the results list
    await dismissConsentPrompts(page);

    // Additional wait for JavaScript to populate dynamic content
    // EQTiming and SkiClassics load results via JavaScript after page load
    console.log(`[${new Date().toISOString()}] Waiting for JavaScript to execute...`);
    await page.waitForTimeout(3000); // Wait 3 seconds for initial JS execution

    // Try to wait for common table elements with longer timeout
    try {
      await page.waitForSelector('table tbody tr, .col-point-scroll, [data-checkpoint]', {
        timeout: 8000
      });
      console.log(`[${new Date().toISOString()}] Results table detected, waiting for data population...`);
      await page.waitForTimeout(2000);
    } catch (e) {
      console.log(`[${new Date().toISOString()}] No results table found after first wait, retrying consent cleanup...`);
      await dismissConsentPrompts(page);
      try {
        await page.waitForSelector('table tbody tr, .col-point-scroll, [data-checkpoint]', {
          timeout: 5000
        });
        console.log(`[${new Date().toISOString()}] Results table detected after retry cleanup`);
        await page.waitForTimeout(1500);
      } catch {
        console.log(`[${new Date().toISOString()}] Still no results table after retry, continuing anyway`);
        await page.waitForTimeout(2000);
      }
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
