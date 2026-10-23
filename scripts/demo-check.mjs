#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the running demo site in Edge (ROADMAP 3.3): start it first with `scripts/demo.ps1 start`.
 *   - The home page in English and German: the language, the heading, the menu, the legal links, the blocks, the
 *     newsletter slot and the language switch.
 *   - The members area: the sign-in page at its address, a wrong password refused with the site's message, a
 *     generated member signed in and out, in both languages.
 *   - The backoffice: Playwright signs in with the generated admin and sees the home page in the content tree.
 *   - axe-core finds no WCAG 2.2 A or AA violation on the visitor pages, and no visitor page requests anything from
 *     another host (CLAUDE.md rule 4).
 *
 * The address comes from the demo's appsettings.json, the expected texts from its Seeding/DemoContent.json and the
 * accounts from .local/credentials.json ("demo"), so nothing here repeats a value that lives elsewhere. No password is
 * ever printed. Screenshots for review go to .local/demo/shots/.
 *
 *   node scripts/demo-check.mjs
 *
 * Exit code 1 when a check fails.
 */
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { launchOptions } from './lib/browser.mjs';

export const DEMO = 'demo/Jiron.Umbraco.FormBuilder.Demo';
export const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const MENU_TYPES = new Set(['demoPage', 'demoMembers']);

/** The demo's address from its appsettings.json, without a trailing slash; only ports 5500 to 5599 (ROADMAP section 4.11). */
export function demoUrl(appsettingsText) {
  const url = JSON.parse(appsettingsText)?.Umbraco?.CMS?.WebRouting?.UmbracoApplicationUrl;
  if (typeof url !== 'string' || url.length === 0) throw new Error('the demo has no Umbraco:CMS:WebRouting:UmbracoApplicationUrl');
  const port = Number(new URL(url).port);
  if (!(port >= 5500 && port <= 5599)) throw new Error(`the demo's address ${url} is outside the ports 5500 to 5599`);
  return url.replace(/\/+$/, '');
}

/** What each language's pages must show, from the seeder's texts. */
export function expectations(content) {
  const result = {};
  for (const language of ['en', 'de']) {
    const ofType = (type) => content.pages.find((page) => page.type === type)[language];
    result[language] = {
      path: `/${language}/`,
      heading: content.home[language].heading,
      newsletter: content.home[language].newsletterHeading,
      blocks: content.home.blocks.map((block) => block[language].heading),
      menu: content.pages.filter((page) => MENU_TYPES.has(page.type)).map((page) => page[language].name),
      legal: content.pages.filter((page) => page.type === 'demoLegalPage').map((page) => page[language].name),
      membersMenu: ofType('demoMembers').name,
      members: ofType('demoMembers').heading,
      signIn: ofType('demoLogin').heading,
      text: (key) => content.dictionary[key][language],
    };
  }
  return result;
}

/** The generated accounts of the demo, or an error that names the fix and never a secret. */
export function demoAccounts(credentials) {
  const demo = credentials?.demo;
  const complete = (person) => typeof person?.email === 'string' && typeof person?.password === 'string';
  if (!complete(demo?.admin) || !Array.isArray(demo?.members) || demo.members.length < 2 || !demo.members.every(complete)) {
    throw new Error('.local/credentials.json has no complete "demo" entry: run scripts/demo.ps1 start first');
  }
  return { admin: demo.admin, members: demo.members };
}

/** A small reporter: one line per check, and whether any failed. */
function reporter() {
  let failed = 0;
  return {
    check(ok, line) {
      console.log(`${ok ? 'ok  ' : 'FAIL'} ${line}`);
      if (!ok) failed++;
      return ok;
    },
    get failed() {
      return failed;
    },
  };
}

async function texts(locator) {
  return (await locator.allInnerTexts()).map((text) => text.trim());
}

async function main(root) {
  const url = demoUrl(readFileSync(join(root, DEMO, 'appsettings.json'), 'utf8'));
  const expected = expectations(JSON.parse(readFileSync(join(root, DEMO, 'Seeding', 'DemoContent.json'), 'utf8')));
  const credentialsFile = join(root, '.local', 'credentials.json');
  const accounts = demoAccounts(existsSync(credentialsFile) ? JSON.parse(readFileSync(credentialsFile, 'utf8')) : {});
  const shots = join(root, '.local', 'demo', 'shots');
  mkdirSync(shots, { recursive: true });

  const require = createRequire(import.meta.url);
  const { chromium } = require('playwright-core');
  const { default: AxeBuilder } = await import('@axe-core/playwright');
  const report = reporter();
  const origin = new URL(url).origin;
  const foreign = new Set();

  const browser = await chromium.launch(launchOptions());
  try {
    const visitor = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    visitor.on('request', (request) => {
      if (!/^(data|blob):/.test(request.url()) && new URL(request.url()).origin !== origin) foreign.add(request.url());
    });
    const page = await visitor.newPage();
    const axe = async (name) => {
      const result = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
      const found = result.violations.map((v) => `${v.id} (${v.nodes.length})`);
      report.check(found.length === 0, `axe on ${name}: ${found.length ? found.join(', ') : 'no WCAG 2.2 A or AA violation'}`);
    };

    for (const [language, want] of Object.entries(expected)) {
      const response = await page.goto(url + want.path);
      report.check(response?.status() === 200, `${want.path} answers ${response?.status()}`);
      report.check((await page.getAttribute('html', 'lang')) === language, `${want.path} is in ${language}`);
      report.check((await page.locator('h1').innerText()).trim() === want.heading, `${want.path} shows "${want.heading}"`);
      const menu = await texts(page.locator('.site-nav a'));
      report.check(JSON.stringify(menu) === JSON.stringify(want.menu), `${want.path} menu: ${menu.join(', ')}`);
      const legal = await texts(page.locator('.site-foot nav a'));
      report.check(JSON.stringify(legal) === JSON.stringify(want.legal), `${want.path} legal links: ${legal.join(', ')}`);
      const blocks = await texts(page.locator('.site-blocks h2'));
      report.check(JSON.stringify(blocks) === JSON.stringify(want.blocks), `${want.path} blocks: ${blocks.join(', ')}`);
      const slot = (await page.locator('[data-demo-slot="newsletter"] h2').innerText()).trim();
      report.check(slot === want.newsletter, `${want.path} newsletter slot: "${slot}"`);
      const other = language === 'en' ? '/de/' : '/en/';
      report.check((await page.getAttribute('.site-lang', 'href')) === other, `${want.path} switches language to ${other}`);
      await axe(want.path);
      await page.screenshot({ path: join(shots, `home-${language}-1440.png`), fullPage: true });
    }

    // The members area, in each language with its own member: signed out it shows the sign-in page at its address.
    for (const [index, [language, want]] of Object.entries(expected).entries()) {
      const member = accounts.members[index];
      await page.goto(url + want.path);
      await page.locator('.site-nav a', { hasText: want.membersMenu }).click();
      const membersAddress = page.url();
      report.check((await page.locator('h1').innerText()).trim() === want.signIn, `${language} members area asks to sign in`);
      const current = await texts(page.locator('.site-nav a[aria-current="page"]'));
      report.check(current.join() === want.membersMenu, `${language} menu marks "${current.join()}" as the current page`);
      if (language === 'en') {
        await axe(new URL(membersAddress).pathname);
        await page.setViewportSize({ width: 375, height: 812 });
        await page.screenshot({ path: join(shots, 'sign-in-en-375.png'), fullPage: true });
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.locator('#login-email').fill(member.email);
        await page.locator('#login-password').fill(`${member.password}-not`);
        await page.locator('.site-form button[type="submit"]').click();
        await page.waitForLoadState('load');
        const error = (await page.locator('.site-error').innerText().catch(() => '')).trim();
        report.check(error === want.text('Demo.Login.Failed'), `${language} wrong password: "${error}"`);
      }
      await page.locator('#login-email').fill(member.email);
      await page.locator('#login-password').fill(member.password);
      await page.locator('.site-form button[type="submit"]').click();
      await page.waitForLoadState('load');
      report.check(page.url() === membersAddress, `${language} sign-in returns to ${new URL(membersAddress).pathname}`);
      report.check((await page.locator('h1').innerText()).trim() === want.members, `${language} member sees "${want.members}"`);
      const signedIn = (await page.locator('.site-account strong').innerText().catch(() => '')).trim();
      report.check(signedIn === member.name, `${language} shows the signed-in member's name`);
      if (language === 'en') await page.screenshot({ path: join(shots, 'members-en-1440.png'), fullPage: true });
      await page.locator('.site-account button[type="submit"]').click();
      await page.waitForLoadState('load');
      report.check(new URL(page.url()).pathname === want.path, `${language} sign-out returns to ${want.path}`);
      await page.goto(membersAddress);
      report.check((await page.locator('h1').innerText()).trim() === want.signIn, `${language} members area is closed again`);
    }

    await page.setViewportSize({ width: 375, height: 812 });
    for (const language of Object.keys(expected)) {
      await page.goto(url + expected[language].path);
      await page.screenshot({ path: join(shots, `home-${language}-375.png`), fullPage: true });
    }
    report.check(foreign.size === 0, `visitor pages request nothing from other hosts${foreign.size ? `: ${[...foreign].join(', ')}` : ''}`);
    await visitor.close();

    // The backoffice, signed in with the generated admin (the sign-in steps of the Phase 1 sandbox).
    const editor = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const backoffice = await editor.newPage();
    await backoffice.goto(`${url}/umbraco`);
    const email = backoffice.getByLabel(/email/i).first();
    await email.waitFor({ timeout: 60_000 });
    await email.fill(accounts.admin.email);
    await backoffice.getByLabel(/password/i).first().fill(accounts.admin.password);
    await backoffice.getByRole('button', { name: /^(login|log in|sign in)$/i }).first().click();
    const signedIn = await backoffice.waitForURL(/\/umbraco\/section\//, { timeout: 90_000 }).then(
      () => true,
      () => false,
    );
    report.check(signedIn, `backoffice sign-in with the generated admin (${backoffice.url().replace(url, '')})`);
    const homeName = JSON.parse(readFileSync(join(root, DEMO, 'Seeding', 'DemoContent.json'), 'utf8')).home.en.name;
    const tree = await backoffice
      .getByText(homeName, { exact: true })
      .first()
      .waitFor({ timeout: 30_000 })
      .then(
        () => true,
        () => false,
      );
    report.check(tree, `the content tree shows "${homeName}"`);
    await backoffice.screenshot({ path: join(shots, 'backoffice-1440.png') });
    await editor.close();
  } finally {
    await browser.close();
  }
  console.log(report.failed === 0 ? 'demo-check: all checks passed' : `demo-check: ${report.failed} check(s) failed`);
  return report.failed === 0 ? 0 : 1;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
  main(root).then(
    (code) => process.exit(code),
    (error) => {
      console.error(`demo-check: ${error instanceof Error ? error.message : String(error)}`);
      process.exit(1);
    },
  );
}
