import assert from 'node:assert/strict';
import { test } from 'node:test';
import { STATIC_ORIGIN, STATIC_PORT } from './paths.ts';
import { headlessPage } from './static-site.ts';

// The static page of the headless tests: the element's module and attributes from the host, the values escaped, a name
// that is no attribute refused, and its language and heading from the culture.

test('the static page loads the element from the host and names the form for its public API', () => {
  const page = headlessPage('http://127.0.0.1:5520', { form: 'a-key', culture: 'de-DE' });

  assert.match(
    page,
    /<script type="module" src="http:\/\/127\.0\.0\.1:5520\/jfb\/assets\/element\/jfb-element\.js"><\/script>/,
  );
  assert.match(
    page,
    /<jfb-form api="http:\/\/127\.0\.0\.1:5520\/jfb\/api\/v1" form="a-key" culture="de-DE"><\/jfb-form>/,
  );
  assert.match(page, /<html lang="de">/);
  assert.match(page, /<h1>Engagieren Sie sich in der Bibliothek<\/h1>/);
  assert.match(page, /<link rel="icon" href="data:,">/);
});

test('the static page escapes what it writes and refuses a name that is no attribute', () => {
  const page = headlessPage('http://127.0.0.1:5520', { form: '"><script>', 'context-topic': 'a & b' });

  assert.match(page, /form="&quot;&gt;&lt;script&gt;"/);
  assert.match(page, /context-topic="a &amp; b"/);
  assert.match(page, /<html lang="en">/);
  assert.throws(() => headlessPage('http://127.0.0.1:5520', { 'on load': 'x' }), /no attribute name/);
});

test("the static page's port is 5521, in the run's range", () => {
  assert.equal(STATIC_PORT, 5521);
  assert.equal(STATIC_ORIGIN, 'http://127.0.0.1:5521');
});
