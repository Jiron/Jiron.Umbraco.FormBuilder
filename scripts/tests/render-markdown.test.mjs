// The Markdown renderer for checking the READMEs in Edge (ROADMAP 17.2a): GitHub's anchors on headings, repeated ones
// numbered, GFM tables, task lists and autolinks, and the page in GitHub's stylesheet with the file's folder as base.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { page, toHtml } from '../render-markdown.mjs';

test("headings carry GitHub's anchors, the same ones the link check expects", () => {
  const html = toHtml('# Getting started\n\n## Put `{{ field.email }}` in it\n\n## Getting started\n');
  assert.match(html, /<h1 id="getting-started">Getting started<\/h1>/);
  assert.match(html, /<h2 id="put--fieldemail--in-it">Put <code>\{\{ field\.email \}\}<\/code> in it<\/h2>/);
  assert.match(html, /<h2 id="getting-started-1">Getting started<\/h2>/);
});

test('tables, task lists and bare addresses render as GitHub renders them', () => {
  const html = toHtml('| A | B |\n| --- | --- |\n| 1 | 2 |\n\n- [x] done\n- [ ] open\n\nSee https://example.com.\n');
  assert.match(html, /<table>[\s\S]*<th>A<\/th>[\s\S]*<td>2<\/td>/);
  assert.match(html, /<input checked="" disabled="" type="checkbox">/);
  assert.match(html, /<a href="https:\/\/example\.com">https:\/\/example\.com<\/a>/);
});

test("the page wraps the Markdown in GitHub's stylesheet, with the file's folder as the base for images", () => {
  const html = page('![shot](screenshots/a.png)', 'file:///C:/repo/docs/');
  assert.match(html, /<base href="file:\/\/\/C:\/repo\/docs\/">/);
  assert.match(html, /\.markdown-body/);
  assert.match(html, /<article class="markdown-body"><p><img src="screenshots\/a\.png" alt="shot"><\/p>/);
});
