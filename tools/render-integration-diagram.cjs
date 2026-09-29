// Regenerate the README image from the editable Mermaid diagram.
// Requires Node.js, Playwright and an installed Chromium browser.
// Optional environment variables: PLAYWRIGHT_MODULE, DIAGRAM_BROWSER.
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');

async function main() {
  const root = path.resolve(__dirname, '..');
  const source = fs.readFileSync(path.join(root, 'docs', 'INTEGRATION_OVERVIEW.md'), 'utf8');
  const match = source.match(/```mermaid\r?\n([\s\S]*?)```/);
  if (!match) throw new Error('Editable Mermaid diagram not found');
  const browser = await chromium.launch({
    headless: true,
    chromiumSandbox: true,
    ...(process.env.DIAGRAM_BROWSER ? { executablePath: process.env.DIAGRAM_BROWSER } : {}),
  });
  try {
    const page = await browser.newPage({ viewport: { width: 1800, height: 1200 }, deviceScaleFactor: 2 });
    await page.setContent('<!doctype html><html lang="ja"><head><meta charset="utf-8"></head><body style="margin:0;background:white"><main id="diagram" style="display:inline-block;padding:28px"></main></body></html>');
    await page.addScriptTag({ url: 'https://cdn.jsdelivr.net/npm/mermaid@11.12.0/dist/mermaid.min.js' });
    await page.evaluate(async (diagram) => {
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: 'neutral',
        themeVariables: {
          fontFamily: '"Yu Gothic", "Meiryo", "Segoe UI", sans-serif',
          fontSize: '20px',
        },
        flowchart: { useMaxWidth: false, htmlLabels: true, nodeSpacing: 36, rankSpacing: 68 },
      });
      const { svg } = await mermaid.render('integration-overview', diagram);
      document.getElementById('diagram').innerHTML = svg;
      await document.fonts.ready;
    }, match[1]);
    const image = page.locator('#diagram');
    const bounds = await image.boundingBox();
    if (!bounds || bounds.width < 500 || bounds.height < 200) throw new Error('Invalid diagram dimensions');
    await page.setViewportSize({ width: Math.ceil(bounds.width), height: Math.ceil(bounds.height) });
    const output = path.join(root, 'docs', 'images', 'integration-overview.png');
    fs.mkdirSync(path.dirname(output), { recursive: true });
    await image.screenshot({ path: output });
    console.log(`Rendered ${Math.ceil(bounds.width)} x ${Math.ceil(bounds.height)} at 2x: ${output}`);
  } finally {
    await browser.close();
  }
}

main().catch((error) => { console.error(error); process.exitCode = 1; });
