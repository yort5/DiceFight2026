// Renders app-icon.svg to the PNGs the manifest and iOS use. Needs a
// Playwright Chromium: node web/icons/render-icons.cjs [chromium-path]
const { chromium } = require("playwright");
const fs = require("fs");
const path = require("path");
const svg = fs.readFileSync(path.join(__dirname, "app-icon.svg"), "utf8");
const out = path.join(__dirname, "..", "public");
const SIZES = { "icon-192.png": 192, "icon-512.png": 512, "apple-touch-icon.png": 180 };
(async () => {
  const browser = await chromium.launch(process.argv[2] ? { executablePath: process.argv[2] } : {});
  const page = await browser.newPage();
  for (const [name, size] of Object.entries(SIZES)) {
    await page.setViewportSize({ width: size, height: size });
    await page.setContent(`<style>*{margin:0}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`);
    await page.screenshot({ path: path.join(out, name), omitBackground: false });
  }
  await browser.close();
})();
