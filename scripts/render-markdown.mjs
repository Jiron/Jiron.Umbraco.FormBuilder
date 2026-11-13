#!/usr/bin/env node
// Renders Markdown as GitHub shows it (ROADMAP 17.2a), because the private repository can't be viewed from here: each
// file is turned into HTML by a GitHub-flavoured renderer (marked) inside GitHub's own stylesheet
// (github-markdown-css), with GitHub's heading anchors and the file's folder as the base for its images, written to
// .local/markdown/<name>.html, opened in Edge (Chromium in CI), shot whole into .local/markdown/<name>.png and checked:
// every image loads, and no table or list is left as text. Both are development tools, MIT, never shipped.
// Usage: node scripts/render-markdown.mjs [files...]   (no files: README.md and the NuGet readmes)
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Marked } from 'marked';
import { slug } from './link-check.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);

/** The files rendered by default: the repository's README and the NuGet readmes, the product's and the AI add-on's. */
export const DEFAULT_FILES = [join(root, 'README.md'), join(root, 'docs', 'README.nuget.md'), join(root, 'docs', 'README.nuget.ai.md')];

/** Markdown as HTML, the way GitHub renders it: GFM tables, task lists and autolinks, and anchors on headings. */
export function toHtml(markdown) {
  const seen = new Map();
  const marked = new Marked({ gfm: true });
  marked.use({
    renderer: {
      heading({ tokens, depth, text }) {
        const base = slug(text);
        const count = seen.get(base) ?? 0;
        seen.set(base, count + 1);
        const id = count === 0 ? base : `${base}-${count}`;
        return `<h${depth} id="${id}">${this.parser.parseInline(tokens)}</h${depth}>\n`;
      },
    },
  });
  return marked.parse(markdown);
}

/** A whole page: the rendered Markdown in GitHub's stylesheet, at the width GitHub gives a README. */
export function page(markdown, base) {
  const css = readFileSync(require.resolve('github-markdown-css/github-markdown-light.css'), 'utf8');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><base href="${base}">
    <style>${css} body { margin: 0; background: #fff; } .markdown-body { box-sizing: border-box; max-width: 980px;
    margin: 0 auto; padding: 32px 45px; }</style></head>
    <body><article class="markdown-body">${toHtml(markdown)}</article></body></html>`;
}

async function main() {
  const named = process.argv.slice(2).map((file) => resolve(file));
  const files = named.length > 0 ? named : DEFAULT_FILES;
  const { chromium } = require('@playwright/test');
  const out = join(root, '.local', 'markdown');
  mkdirSync(out, { recursive: true });
  const browser = await chromium.launch(process.env.CI ? {} : { channel: 'msedge' });
  const problems = [];
  try {
    for (const file of files) {
      const name = basename(file, '.md');
      const view = await browser.newPage({ viewport: { width: 1012, height: 900 } });
      const base = pathToFileURL(`${dirname(file)}/`).href;
      // A page of its own on disk, not setContent: Chromium loads no file:// image into an about:blank page.
      const html = join(out, `${name}.html`);
      writeFileSync(html, page(readFileSync(file, 'utf8'), base));
      await view.goto(pathToFileURL(html).href, { waitUntil: 'load' });
      const found = await view.evaluate(() => {
        const images = [...document.images];
        const left = [...document.querySelectorAll('.markdown-body p')]
          .map((paragraph) => paragraph.textContent ?? '')
          .filter((text) => /^\s*\|.*\|\s*$/m.test(text) || /^\s*[-*] \S/m.test(text));
        return {
          headings: document.querySelectorAll('h1, h2, h3, h4').length,
          tables: document.querySelectorAll('table').length,
          links: document.querySelectorAll('a[href]').length,
          images: images.length,
          broken: images.filter((image) => !image.complete || image.naturalWidth === 0).map((image) => image.getAttribute('src')),
          left,
        };
      });
      const shot = join(out, `${name}.png`);
      await view.screenshot({ path: shot, fullPage: true });
      await view.close();
      for (const src of found.broken) problems.push(`${file}: the image ${src} does not load`);
      for (const text of found.left) problems.push(`${file}: left as text, not rendered: ${text.slice(0, 80)}`);
      console.log(
        `render-markdown: ${name}: ${found.headings} headings, ${found.tables} tables, ${found.links} links, ` +
          `${found.images} images; shot at ${shot}`,
      );
    }
  } finally {
    await browser.close();
  }
  for (const problem of problems) console.error(`render-markdown: ${problem}`);
  if (problems.length > 0) process.exit(1);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) await main();
