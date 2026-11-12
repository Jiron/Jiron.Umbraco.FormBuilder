#!/usr/bin/env node
// Renders Jiron Form Builder's mark into PNGs (ROADMAP 17.1, assets/brand.md) with the Playwright of the browser tests,
// so the repository needs no image library: assets/logo.svg at 512, 256, 128 and 64 px on a transparent background,
// as assets/icon-<size>.png. It also draws a preview sheet for the visual check, in .local/icons/preview.png: the mark
// at 128 and 32 px on light and dark and beside the name, the one-colour mark, and the backoffice icon at 48 and 24 px.
// Usage: node scripts/icons.mjs   (needs `npm ci` in tests/e2e; Edge on this machine, Chromium in CI)
import { mkdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The sizes the mark is rendered at, largest first. */
export const SIZES = [512, 256, 128, 64];

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const assets = join(root, 'assets');

/** An SVG file as a data URL an image can show. */
export function dataUrl(svg) {
  return `data:image/svg+xml;base64,${Buffer.from(svg, 'utf8').toString('base64')}`;
}

/** The PNG's path for a size. */
export function pngPath(size) {
  return join(assets, `icon-${size}.png`);
}

/** The preview sheet: the mark on light and dark at two sizes and beside the name, the one-colour mark, the icon. */
function sheet(svgs) {
  const img = (name, size, style = '') =>
    `<img src="${dataUrl(svgs[name])}" width="${size}" height="${size}" alt="" style="${style}">`;
  // The line icon draws in currentColor, which an image does not inherit: it is set inline for each background.
  const icon = (size, color) => {
    const tinted = svgs['icon-section'].replace('<svg ', `<svg color="${color}" `);
    return `<img src="${dataUrl(tinted)}" width="${size}" height="${size}" alt="">`;
  };
  const mono = (size, color) => {
    const tinted = svgs['logo-mono'].replace(/color="#[0-9A-Fa-f]{6}"/, `color="${color}"`);
    return `<img src="${dataUrl(tinted)}" width="${size}" height="${size}" alt="">`;
  };
  const name = (text, size) => `<span style="font-size:${size}px;font-weight:700;letter-spacing:-0.01em">${text}</span>`;
  const lockup = (mark, size) =>
    `<span style="display:inline-flex;align-items:center;gap:${Math.round(size / 4)}px">${mark}${name('Jiron', Math.round(size / 2))}</span>`;
  const row = (background, color, content) =>
    `<div style="background:${background};color:${color};padding:24px;display:flex;gap:28px;align-items:center;border-radius:12px">${content}</div>`;
  return `<!doctype html><html><body style="margin:0;padding:24px;background:#fff;font-family:'Segoe UI',system-ui,sans-serif;display:grid;gap:16px;width:820px">
    ${row('#ffffff', '#16181d', `${img('logo', 128)}${img('logo', 32)}${lockup(img('logo', 48), 48)}${name('Jiron Form Builder', 22)}`)}
    ${row('#15171c', '#e8ebf2', `${img('logo', 128)}${img('logo', 32)}${lockup(img('logo', 48), 48)}${name('Jiron Form Builder', 22)}`)}
    ${row('#f3f5f9', '#16181d', `${img('logo-mono', 128)}${img('logo-mono', 32)}${mono(64, '#ffffff').replace('<img', '<img style="background:#15171c;padding:12px;border-radius:8px"')}${icon(48, '#16181d')}${icon(24, '#16181d')}${icon(24, '#1b264f')}`)}
  </body></html>`;
}

async function main() {
  const require = createRequire(join(root, 'tests', 'e2e', 'package.json'));
  const { chromium } = require('@playwright/test');
  const svgs = Object.fromEntries(
    ['logo', 'logo-mono', 'icon-section'].map((name) => [name, readFileSync(join(assets, `${name}.svg`), 'utf8')]),
  );
  mkdirSync(join(root, '.local', 'icons'), { recursive: true });

  const browser = await chromium.launch(process.env.CI ? {} : { channel: 'msedge' });
  try {
    for (const size of SIZES) {
      const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
      await page.setContent(
        `<html><body style="margin:0;background:transparent"><img src="${dataUrl(svgs.logo)}" width="${size}" height="${size}" style="display:block" alt=""></body></html>`,
      );
      await page.locator('img').evaluate((image) => image.decode());
      await page.screenshot({ path: pngPath(size), omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
      await page.close();
    }

    const preview = await browser.newPage({ viewport: { width: 880, height: 720 }, deviceScaleFactor: 1 });
    await preview.setContent(sheet(svgs));
    await preview.evaluate(() => Promise.all([...document.images].map((image) => image.decode())));
    const path = join(root, '.local', 'icons', 'preview.png');
    await preview.locator('body').screenshot({ path });
    console.log(`icons: wrote ${SIZES.length} PNGs to assets/ and the preview to .local/icons/preview.png`);
  } finally {
    await browser.close();
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  await main();
}
