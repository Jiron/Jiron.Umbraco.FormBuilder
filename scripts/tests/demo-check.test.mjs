// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/demo-check.mjs, the demo's texts and scripts/demo.ps1. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, test } from 'node:test';
import { DEMO, demoAccounts, demoUrl, expectations } from '../demo-check.mjs';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const content = JSON.parse(readFileSync(join(root, DEMO, 'Seeding', 'DemoContent.json'), 'utf8'));
const settings = (url) => JSON.stringify({ Umbraco: { CMS: { WebRouting: { UmbracoApplicationUrl: url } } } });

describe("the demo's address", () => {
  test('comes from its appsettings.json, on port 5500', () => {
    assert.equal(demoUrl(readFileSync(join(root, DEMO, 'appsettings.json'), 'utf8')), 'http://127.0.0.1:5500');
    assert.equal(demoUrl(settings('http://127.0.0.1:5599/')), 'http://127.0.0.1:5599');
  });

  test('is refused outside the ports 5500 to 5599, and when missing', () => {
    for (const url of ['http://127.0.0.1:5400', 'http://127.0.0.1:1210', 'http://127.0.0.1:5600', 'http://127.0.0.1']) {
      assert.throws(() => demoUrl(settings(url)), /outside the ports 5500 to 5599/, url);
    }
    assert.throws(() => demoUrl('{}'), /UmbracoApplicationUrl/);
  });
});

describe('what the pages must show', () => {
  test('comes from the seeded texts, per language', () => {
    const want = expectations(content);
    assert.deepEqual(Object.keys(want), ['en', 'de']);
    assert.equal(want.en.heading, content.home.en.heading);
    assert.equal(want.de.path, '/de/');
    assert.deepEqual(want.en.menu, ['Events', 'Careers', 'Feedback', 'Room hire', 'Contact', 'Members']);
    assert.deepEqual(want.en.legal, ['Privacy', 'Imprint']);
    assert.equal(want.en.blocks.length, content.home.blocks.length);
    assert.equal(want.de.text('Demo.Login.Failed'), content.dictionary['Demo.Login.Failed'].de);
  });
});

describe("the demo's accounts", () => {
  const person = { name: 'A', email: 'a@lindenhall.example', password: 'JFB_FAKE-not-a-password' };

  test('are the admin and two members of the "demo" entry', () => {
    const accounts = demoAccounts({ demo: { admin: person, members: [person, person] } });
    assert.equal(accounts.members.length, 2);
  });

  test('missing or incomplete, name the fix and never a secret', () => {
    for (const credentials of [{}, { demo: { admin: person } }, { demo: { admin: person, members: [person, { email: 'x' }] } }]) {
      assert.throws(
        () => demoAccounts(credentials),
        (error) => /run scripts\/demo.ps1 start first/.test(error.message) && !error.message.includes(person.password),
      );
    }
  });
});

describe("the seeder's texts (DemoContent.json)", () => {
  const types = new Set(['demoPage', 'demoMembers', 'demoLogin', 'demoLegalPage']);
  const blockTypes = new Set(['demoTextBlock', 'demoHighlightBlock']);
  const pages = [content.home, ...content.pages];

  test('give every page a unique fixed key, a known type and every text in English and German', () => {
    assert.equal(content.home.type, 'demoHome');
    assert.equal(new Set(pages.map((page) => page.key)).size, pages.length);
    for (const page of pages) {
      assert.match(page.key, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/, page.en.name);
      if (page !== content.home) assert.ok(types.has(page.type), page.type);
      for (const language of ['en', 'de']) {
        for (const field of ['name', 'heading', 'lede', 'body']) assert.equal(typeof page[language][field], 'string', `${page.en.name} ${language} ${field}`);
        assert.ok(page[language].name.length > 0 && page[language].heading.length > 0);
      }
      for (const block of page.blocks) {
        assert.ok(blockTypes.has(block.type), block.type);
        for (const language of ['en', 'de']) assert.ok(block[language].heading && block[language].text, `${block.en.heading} ${language}`);
      }
    }
    for (const language of ['en', 'de']) assert.ok(content.home[language].newsletterHeading && content.home[language].newsletterText);
  });

  test('have exactly one members area and one sign-in page, and the dictionary item of every view text', () => {
    assert.equal(content.pages.filter((page) => page.type === 'demoMembers').length, 1);
    assert.equal(content.pages.filter((page) => page.type === 'demoLogin').length, 1);
    const views = ['Shared/_DemoLayout.cshtml', 'demoLogin.cshtml', 'demoMembers.cshtml'].map((view) =>
      readFileSync(join(root, DEMO, 'Views', view), 'utf8'),
    );
    const used = new Set(views.join('\n').match(/GetDictionaryValue\("([^"]+)"\)/g).map((call) => call.slice(20, -2)));
    assert.deepEqual([...used].filter((key) => !content.dictionary[key]), [], 'every key the views use is seeded');
    for (const [key, text] of Object.entries(content.dictionary)) assert.ok(text.en && text.de, key);
  });
});

describe('scripts/demo.ps1', () => {
  const script = readFileSync(join(root, 'scripts', 'demo.ps1'), 'utf8');

  test('never prints a password', () => {
    const printing = script.split('\n').filter((line) => /Write-(Host|Output)|Out-Host/.test(line));
    assert.ok(printing.length > 0);
    for (const line of printing) assert.doesNotMatch(line, /\.password|\$secrets|imagingHmacSecretKey/i, line.trim());
  });

  test('passes the secrets through the environment only, and stops only its own process tree by PID', () => {
    assert.match(script, /UnattendedUserPassword' = \$secrets\.admin\.password/);
    assert.doesNotMatch(script, /ArgumentList[^\n]*password/i);
    assert.match(script, /taskkill\.exe \/PID \$process\.Id \/T \/F/);
    assert.doesNotMatch(script, /taskkill[^\n]*\/IM|Stop-Process -Name/i);
  });
});
