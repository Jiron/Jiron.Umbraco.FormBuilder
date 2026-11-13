#!/usr/bin/env node
// Takes the documentation's screenshots (ROADMAP 17.3) on the running demo, in Edge (Chromium in CI): the backoffice and
// the website at 1600 x 900, 16:9 as the Marketplace asks, and the website on a phone, three states side by side on one
// 16:9 picture. Each PNG is made smaller without changing a pixel (scripts/png.mjs), checked against the browser's own
// shot pixel by pixel, and written to docs/screenshots/.
//
// The demo brings forms, not entries, so the script first sends entries through the public API, as visitors' browsers
// do, each with a fixed submission id: a second run gets the same receipts and makes no second entry. Its website shots
// send the contact form twice more, as a visitor would. For the history's comparison it publishes the workshop form
// once more with another Send button, only while the form has a single version. Nothing else on the demo changes.
//
// Usage: node scripts/screenshots.mjs [--only name,name] [--no-seed]
//   Start the demo first: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 start
//   The backoffice sign-in uses the demo's generated admin from .local/credentials.json, never printed.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { DEMO, demoUrl } from './demo-check.mjs';
import { optimizePng } from './png.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);

/** The demo's address, from its appsettings.json as the demo script reads it (ROADMAP 4.11), unless JFB_DEMO_URL names another. */
export const BASE = process.env.JFB_DEMO_URL ?? demoUrl(readFileSync(join(root, DEMO, 'appsettings.json'), 'utf8'));

/** Where the pictures go. */
export const OUT = join(root, 'docs', 'screenshots');

/** The size of every picture: 16:9. */
export const SIZE = { width: 1600, height: 900 };

/** A phone's window, drawn at twice its size so it stays sharp when the picture shows it smaller. */
const PHONE = { width: 390, height: 844 };

const SECTION = '/umbraco/section/jfb-forms';
const PREVIEW = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Preview"]';
const PUBLISH = '[data-mark="workspace-action:Jiron.FormBuilder.WorkspaceAction.Form.Publish"]';

/** A submission id that is the same on every run for the same name, as a GUID. */
export function submissionIdOf(name) {
  const hash = createHash('sha256').update(`jfb-screenshots:${name}`).digest('hex');
  return `${hash.slice(0, 8)}-${hash.slice(8, 12)}-4${hash.slice(13, 16)}-8${hash.slice(17, 20)}-${hash.slice(20, 32)}`;
}

/** The number whose SHA-256 of `nonce:number` starts with `bits` zero bits, as the proof of work asks (ADR-0141). */
export function solveProof(nonce, bits) {
  for (let n = 0; ; n++) {
    const hash = createHash('sha256').update(`${nonce}:${n}`).digest();
    if (hash.readUInt32BE(0) >>> (32 - bits) === 0) return String(n);
  }
}

/** The forms a page of the demo shows: each one's key, revision, page and the wording hashes of its consents. */
export function formsIn(html) {
  return html
    .split('<form ')
    .slice(1)
    .map((part) => part.slice(0, part.indexOf('</form>') >>> 0))
    .filter((part) => part.includes('data-jfb-form="'))
    .map((part) => ({
      key: /data-jfb-form="([^"]+)"/.exec(part)?.[1],
      revision: Number(/data-jfb-revision="(\d+)"/.exec(part)?.[1]),
      content: /data-jfb-content="([^"]+)"/.exec(part)?.[1] ?? null,
      consents: Object.fromEntries(
        [...part.matchAll(/name="jfb-consent-([A-Za-z0-9_.-]+)" value="([0-9a-f]+)"/g)].map((m) => [m[1], m[2]]),
      ),
    }));
}

const wait = (ms) => new Promise((done) => setTimeout(done, ms));

async function post(path, body) {
  return fetch(`${BASE}/jfb/api/v1/${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
}

const retryAfter = (response) => (Number(response.headers.get('retry-after')) || 10) * 1000 + 500;

/** Sends one entry as a visitor's browser would, waiting where the rate limits say; returns once it is stored. */
async function sendEntry(form, name, values, consents = {}) {
  for (let attempt = 1; attempt <= 12; attempt++) {
    const asked = await post(`forms/${form.key}/challenge`, { contentKey: form.content });
    if (asked.status === 429) {
      await wait(retryAfter(asked));
      continue;
    }
    if (!asked.ok) throw new Error(`${name}: the challenge answered ${asked.status}`);
    const challenge = await asked.json();
    await wait((challenge.minFillSeconds ?? 0) * 1000 + 600);
    const body = {
      submissionId: submissionIdOf(name),
      revision: form.revision,
      culture: 'en-US',
      contentKey: form.content,
      challenge: challenge.challenge,
      values,
      consents,
    };
    if (challenge.proofOfWork) body.proofOfWork = solveProof(challenge.proofOfWork.nonce, challenge.proofOfWork.bits);
    const sent = await post(`forms/${form.key}/entries`, body);
    if (sent.ok) return;
    if (sent.status === 429) {
      await wait(retryAfter(sent));
      continue;
    }
    const problem = await sent.json().catch(() => ({}));
    // The same id for other answers: the entry of an earlier run, from a form published again since.
    if (problem.code === 'duplicate_submission' || problem.code === 'already_submitted') return;
    throw new Error(`${name}: the entry answered ${sent.status} ${problem.code ?? ''}`);
  }
  throw new Error(`${name}: still rate limited after twelve tries`);
}

// Visitors of the demo's library, made up, at addresses of example.com.
const PEOPLE = [
  ['Ada Brennan', 'booking', 'Could I reserve the small meeting room on Thursday afternoon for a book club of eight?'],
  ['Jonas Keller', 'account', 'My library card expired last month. Can I renew it online, or do I need to come in?'],
  ['Mira Okafor', 'collection', 'Do you have the new edition of the city atlas? I could not find it in the catalogue.'],
  ['Lukas Brandt', 'general', 'Thank you for the repair workshop on Saturday. Will there be another one before the summer?'],
  ['Sofia Marino', 'booking', 'We would like to visit with a school class of 22 children. Which mornings suit you best?'],
  ['Tomas Ruiz', 'collection', 'I would like to donate about forty novels in good condition. Do you take donations?'],
  ['Hannah Weiss', 'general', 'Is the reading room quiet in the evenings? I am looking for a place to study for exams.'],
  ['Noah Fischer', 'account', 'I changed my address recently. Where can I update it for reminders and notices?'],
];

/** The entries the shots show: the contact form's and a few newsletter sign-ups, sent once whatever the runs. */
async function seed() {
  const page = await fetch(`${BASE}/en/contact/`);
  if (!page.ok) throw new Error(`the demo's contact page answered ${page.status}`);
  const [contact, newsletter] = formsIn(await page.text());
  if (!contact) throw new Error("the demo's contact page shows no form");
  for (const [index, [person, topic, message]] of PEOPLE.entries()) {
    const email = `${person.split(' ')[0].toLowerCase()}@example.com`;
    await sendEntry(contact, `contact-${index}`, { name: person, email, topic, message });
    process.stdout.write('.');
  }
  if (!newsletter?.consents.agree) {
    console.warn("\nscreenshots: the contact page's newsletter form or its consent was not found; no sign-ups sent");
  } else {
    for (const [index, [person]] of PEOPLE.slice(0, 4).entries()) {
      const email = `${person.split(' ')[1].toLowerCase()}@example.com`;
      await sendEntry(newsletter, `newsletter-${index}`, { email, agree: true }, { agree: newsletter.consents.agree });
      process.stdout.write('.');
    }
  }
  process.stdout.write('\n');
}

/** Waits for motion to end, with the pointer out of the way. */
async function settle(page) {
  await page.mouse.move(1, 1);
  await page
    .waitForFunction(() => document.getAnimations().every((animation) => animation.playState !== 'running'), undefined, {
      timeout: 3000,
    })
    .catch(() => undefined);
  await page.waitForTimeout(400);
}

/** Scrolls so the shot shows the form, or the outcome in its place, with no line cut at the window's top. */
async function showForm(page, selector = '#jfb-1') {
  const { height } = page.viewportSize();
  await page.locator(selector).first().evaluate((form, height) => {
    const box = form.getBoundingClientRect();
    const top = box.top + scrollY;
    // The whole form with what fits above it, or its start when it is taller than the window.
    const wanted = Math.min(top - 24, Math.max(0, box.bottom + scrollY + 24 - height));
    // The window's top edge never cuts a line: it moves up to the start of a block it would cross.
    const crossed = [...document.querySelectorAll('h1, h2, h3, p, address, ul, ol, header')]
      .map((block) => block.getBoundingClientRect())
      .filter((block) => block.top + scrollY < wanted && block.bottom + scrollY > wanted)
      .map((block) => block.top + scrollY - 16);
    window.scrollTo({ top: Math.max(0, Math.min(wanted, ...crossed)), behavior: 'instant' });
  }, height);
}

/** The demo's contact form, filled in as a visitor would. */
async function fillContact(page) {
  const form = page.locator('#jfb-1');
  await form.locator('[name="name"]').fill('Clara Novak');
  await form.locator('[name="email"]').fill('clara@example.com');
  await form.locator('[name="topic"]').selectOption('booking');
  await form.locator('[name="message"]').fill('Is the meeting room free next Tuesday from two to four?');
}

async function sendContact(page) {
  await page.locator('#jfb-1 [data-jfb-nav="submit"]').click();
}

/** The website's shots, each a function of a page at 1600 x 900. */
const WEBSITE = {
  'website-default': async (page) => {
    await page.goto(`${BASE}/en/contact/?theme=default`);
    await showForm(page);
  },
  'website-bootstrap5': async (page) => {
    await page.goto(`${BASE}/en/contact/?theme=bootstrap5`);
    await showForm(page);
  },
  'website-unstyled': async (page) => {
    await page.goto(`${BASE}/en/contact/?theme=unstyled`);
    await showForm(page);
  },
  'website-errors': async (page) => {
    await page.goto(`${BASE}/en/contact/?theme=default`);
    await page.locator('#jfb-1 [name="email"]').fill('clara@example');
    await sendContact(page);
    await page.locator('#jfb-1 .jfb-error-summary').waitFor({ timeout: 15_000 });
    await showForm(page);
  },
  'website-sent': async (page) => {
    await page.goto(`${BASE}/en/contact/?theme=default`);
    await fillContact(page);
    await sendContact(page);
    await page.locator('#jfb-1 [data-jfb-outcome], [data-jfb-outcome]').first().waitFor({ timeout: 30_000 });
    await showForm(page, '[data-jfb-outcome]');
  },
};

/** The phone's three states, each shot in a phone's window and then set side by side. */
async function phoneStates(browser) {
  const context = await browser.newContext({ viewport: PHONE, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  const shots = [];
  try {
    await page.goto(`${BASE}/en/contact/?theme=default`);
    await showForm(page);
    await settle(page);
    shots.push(await page.screenshot());
    await sendContact(page);
    await page.locator('#jfb-1 .jfb-error-summary').waitFor({ timeout: 15_000 });
    await showForm(page);
    await settle(page);
    shots.push(await page.screenshot());
    await page.goto(`${BASE}/en/contact/?theme=default`);
    await fillContact(page);
    await sendContact(page);
    await page.locator('[data-jfb-outcome]').first().waitFor({ timeout: 30_000 });
    await showForm(page, '[data-jfb-outcome]');
    await settle(page);
    shots.push(await page.screenshot());
  } finally {
    await context.close();
  }
  return shots;
}

/** The three phone shots side by side on a calm ground, at 16:9. */
async function phonePicture(browser, shots) {
  const page = await browser.newPage({ viewport: SIZE });
  try {
    const images = shots
      .map((png) => `<img alt="" src="data:image/png;base64,${png.toString('base64')}">`)
      .join('');
    await page.setContent(
      `<!doctype html><html><head><style>
        html, body { margin: 0; height: 100%; }
        body { display: flex; align-items: center; justify-content: center; gap: 64px; background: #eef1f6; }
        img { height: 780px; border-radius: 32px; border: 1px solid #ccd3e0; box-shadow: 0 12px 32px rgb(36 87 245 / 0.12); }
      </style></head><body>${images}</body></html>`,
      { waitUntil: 'load' },
    );
    return await page.screenshot();
  } finally {
    await page.close();
  }
}

async function signIn(page) {
  const credentials = JSON.parse(readFileSync(join(root, '.local', 'credentials.json'), 'utf8'));
  const admin = credentials.demo?.admin;
  if (!admin) throw new Error('.local/credentials.json holds no demo admin: start the demo once with scripts/demo.ps1');
  await page.goto(`${BASE}/umbraco`);
  await page.getByLabel(/email/i).first().fill(admin.email);
  await page.getByLabel(/password/i).first().fill(admin.password);
  await page.getByRole('button', { name: /^(login|log in|sign in)$/i }).first().click();
  await page.locator('umb-backoffice-header').waitFor({ timeout: 90_000 });
}

/** The key of a form by its name in the section's tree. */
async function formKey(page, name) {
  await page.goto(`${BASE}${SECTION}`);
  await page.locator('jfb-overview uui-box').first().waitFor({ timeout: 60_000 });
  await page.locator('umb-section-sidebar').getByText(name, { exact: true }).first().click();
  await page.waitForURL(/workspace\/jfb-form\/edit\/[0-9a-f-]{36}/, { timeout: 60_000 });
  return /edit\/([0-9a-f-]{36})/.exec(page.url())[1];
}

const workspaceOf = (key) => `${BASE}${SECTION}/workspace/jfb-form/edit/${key}`;

async function openDesign(page, key) {
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await design.locator('jfb-form-canvas jfb-canvas-field').first().waitFor({ timeout: 60_000 });
  return design;
}

async function selectField(design, label) {
  const field = design.locator('jfb-canvas-field', { hasText: label }).locator('[data-control="select"]').first();
  await field.scrollIntoViewIfNeeded();
  await field.click();
  await design.locator('jfb-designer-inspector h2').waitFor();
}

/** Publishes the workshop form once more with another Send button, while it has a single version. */
async function secondVersion(page, key) {
  await page.goto(`${workspaceOf(key)}/view/history`);
  const history = page.locator('jfb-form-history-view');
  // The rows, not the toggle: the toggle shows while the list is still a skeleton.
  await history.locator('tbody tr[data-number]').first().waitFor({ timeout: 60_000 });
  if ((await history.locator('tbody tr[data-number]').count()) > 1) return;
  await page.goto(`${workspaceOf(key)}/view/settings`);
  const settings = page.locator('jfb-form-settings-view');
  const head = settings.locator('#group-submission');
  await head.waitFor({ timeout: 60_000 });
  if ((await head.getAttribute('aria-expanded')) !== 'true') await head.click();
  const label = settings.locator('#submit-label');
  // The box shows the draft's label once the view has read the form; a change before then is overwritten.
  await page.waitForFunction((box) => box.value !== '', await label.elementHandle(), { timeout: 30_000 });
  // Another label than the one it has, so the new version differs from the first.
  await label.fill((await label.inputValue()) === 'Reserve my place' ? 'Book my place' : 'Reserve my place');
  await label.press('Tab');
  await page.locator(PUBLISH).click();
  const dialog = page.locator('jfb-publish-modal');
  await dialog.locator('textarea, input[type="text"]').first().fill('A friendlier Send button.');
  await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
  await dialog.waitFor({ state: 'detached', timeout: 60_000 });
}

/** The backoffice's shots, each a function of a signed-in page at 1600 x 900 and the forms' keys. */
const BACKOFFICE = {
  overview: async (page) => {
    await page.goto(`${BASE}${SECTION}`);
    await page.locator('jfb-overview uui-box').first().waitFor({ timeout: 60_000 });
    await page.locator('jfb-overview [aria-busy="true"]').waitFor({ state: 'detached', timeout: 30_000 }).catch(() => {});
  },
  designer: async (page, keys) => {
    const design = await openDesign(page, keys.contact);
    await selectField(design, 'Email address');
  },
  'rule-builder': async (page, keys) => {
    const design = await openDesign(page, keys.feedback);
    await design.getByRole('button', { name: 'More actions for the canvas' }).click();
    await design.getByRole('button', { name: 'Rules overview' }).click();
    const overview = page.locator('jfb-rules-overview-modal');
    await overview.locator('button.row').first().click();
    await overview.waitFor({ state: 'detached' });
    const rules = design.locator('jfb-designer-inspector [data-section="rules"] .section-head button');
    if ((await rules.getAttribute('aria-expanded')) !== 'true') await rules.click();
    await design.locator('jfb-designer-inspector button.rule').first().click();
    await page.locator('jfb-rule-builder-modal').waitFor();
  },
  'action-email': async (page, keys) => {
    await page.goto(`${workspaceOf(keys.contact)}/view/actions`);
    const actions = page.locator('jfb-form-actions-view');
    await actions.locator('jfb-action-list .open').first().click();
    await page.locator('jfb-action-modal jfb-email-preview').first().waitFor({ timeout: 30_000 });
  },
  preview: async (page, keys) => {
    await openDesign(page, keys.contact);
    await page.locator(PREVIEW).click();
    const modal = page.locator('jfb-preview-modal');
    await page.frameLocator('jfb-preview-modal iframe').locator('form[data-jfb-form]').waitFor({ timeout: 30_000 });
    await modal.locator('uui-button.fill').click();
    await page.waitForTimeout(800);
  },
  history: async (page, keys) => {
    await secondVersion(page, keys.workshop);
    await page.goto(`${workspaceOf(keys.workshop)}/view/history`);
    const history = page.locator('jfb-form-history-view');
    const boxes = history.locator('tbody tr[data-number] uui-checkbox');
    await boxes.nth(1).waitFor({ timeout: 60_000 });
    await boxes.nth(0).click();
    await boxes.nth(1).click();
    await history.locator('.toolbar uui-button').first().click();
    await page.locator('jfb-version-comparison-modal .part').first().waitFor();
  },
  entries: async (page, keys) => {
    await page.goto(`${workspaceOf(keys.contact)}/view/entries`);
    await page.locator('jfb-entries-view tbody tr').first().waitFor({ timeout: 60_000 });
  },
  'entry-panel': async (page, keys) => {
    await page.goto(`${workspaceOf(keys.contact)}/view/entries`);
    const view = page.locator('jfb-entries-view');
    await view.locator('tbody tr').first().waitFor({ timeout: 60_000 });
    await view.locator('tbody tr').first().locator('.name').click();
    await view.locator('jfb-entry-panel h2').waitFor();
  },
  inbox: async (page) => {
    await page.goto(`${BASE}${SECTION}/workspace/jfb-inbox`);
    await page.locator('jfb-entries-view tbody tr').first().waitFor({ timeout: 60_000 });
  },
  insights: async (page, keys) => {
    await page.goto(`${workspaceOf(keys.contact)}/view/insights`);
    await page.locator('jfb-form-insights-view dl.stats').waitFor({ timeout: 60_000 });
  },
  'theme-editor': async (page) => {
    await page.goto(`${BASE}${SECTION}/workspace/jfb-theme/create/default`);
    const view = page.locator('jfb-theme-view');
    await view.locator('iframe[data-scheme="dark"]').waitFor({ timeout: 60_000 });
    await page.getByRole('textbox', { name: 'Name of the theme' }).fill('Lindenhall');
    await page.waitForTimeout(1500);
  },
  settings: async (page) => {
    await page.goto(`${BASE}${SECTION}/workspace/jfb-settings-spam`);
    await page.locator('jfb-settings-protection uui-box[headline]').first().waitFor({ timeout: 60_000 });
  },
  privacy: async (page) => {
    await page.goto(`${BASE}${SECTION}/workspace/jfb-privacy-people`);
    const email = page.locator('jfb-people-screen input[type="email"]');
    await email.waitFor({ timeout: 60_000 });
    await email.fill('ada@example.com');
    await email.press('Enter');
    await page.waitForTimeout(2500);
  },
};

/** Whether a PNG shows exactly the pixels of another, as the browser decodes both. */
async function samePixels(page, a, b) {
  return page.evaluate(
    async ([first, second]) => {
      const pixels = async (base64) => {
        const image = new Image();
        image.src = `data:image/png;base64,${base64}`;
        await image.decode();
        const canvas = new OffscreenCanvas(image.naturalWidth, image.naturalHeight);
        const context = canvas.getContext('2d');
        context.drawImage(image, 0, 0);
        return context.getImageData(0, 0, image.naturalWidth, image.naturalHeight).data;
      };
      const [x, y] = [await pixels(first), await pixels(second)];
      if (x.length !== y.length) return false;
      for (let i = 0; i < x.length; i++) if (x[i] !== y[i]) return false;
      return true;
    },
    [a.toString('base64'), b.toString('base64')],
  );
}

function waitForDemoJobs() {
  const log = join(root, '.local', 'demo', 'demo.log');
  const until = Date.now() + 240_000;
  return (async () => {
    // The demo says it is ready before Umbraco registers its background jobs; signing in before then can hang SQLite.
    while (existsSync(log) && !readFileSync(log, 'utf8').includes('Completed registering distributed background jobs')) {
      if (Date.now() > until) throw new Error("the demo did not finish starting within 4 minutes; see .local/demo/demo.log");
      await wait(2000);
    }
  })();
}

async function main() {
  const args = process.argv.slice(2);
  const onlyAt = args.indexOf('--only');
  const only = onlyAt >= 0 ? new Set((args[onlyAt + 1] ?? '').split(',').filter(Boolean)) : null;
  const wanted = (name) => !only || only.has(name);
  const home = await fetch(`${BASE}/en/`).catch(() => null);
  if (!home?.ok) {
    console.error(`screenshots: the demo does not answer at ${BASE}; start it with scripts/demo.ps1 start`);
    process.exit(1);
  }
  await waitForDemoJobs();
  if (!args.includes('--no-seed')) {
    process.stdout.write('screenshots: sending the demo entries ');
    await seed();
  }

  const { chromium } = require('@playwright/test');
  const browser = await chromium.launch(process.env.CI ? {} : { channel: 'msedge' });
  const totals = { count: 0, before: 0, after: 0 };
  try {
    mkdirSync(OUT, { recursive: true });
    const check = await browser.newPage();
    // Each picture is made smaller, checked and written at once, so a later step that fails keeps the ones before it.
    const keep = async (name, png) => {
      const smaller = optimizePng(png);
      if (!(await samePixels(check, png, smaller))) throw new Error(`${name}: the smaller PNG does not show the same pixels`);
      writeFileSync(join(OUT, `${name}.png`), smaller);
      totals.count++;
      totals.before += png.length;
      totals.after += smaller.length;
      console.log(`screenshots: ${name}, ${(smaller.length / 1024).toFixed(0)} KB`);
    };
    const context = await browser.newContext({ viewport: SIZE, locale: 'en-US', colorScheme: 'light' });
    const page = await context.newPage();
    page.setDefaultTimeout(30_000);
    for (const [name, shoot] of Object.entries(WEBSITE)) {
      if (!wanted(name)) continue;
      await shoot(page);
      await settle(page);
      await keep(name, await page.screenshot());
    }
    if (wanted('website-phone')) {
      await keep('website-phone', await phonePicture(browser, await phoneStates(browser)));
    }

    const office = await browser.newContext({ viewport: SIZE, locale: 'en-US', colorScheme: 'light' });
    const backoffice = await office.newPage();
    backoffice.setDefaultTimeout(30_000);
    if (Object.keys(BACKOFFICE).some(wanted)) {
      await signIn(backoffice);
      const keys = {
        contact: await formKey(backoffice, 'Contact'),
        feedback: await formKey(backoffice, 'Feedback'),
        workshop: await formKey(backoffice, 'Workshop booking'),
      };
      for (const [name, shoot] of Object.entries(BACKOFFICE)) {
        if (!wanted(name)) continue;
        // A step that fails leaves a picture of where it stood in .local/, which is not committed.
        await shoot(backoffice, keys).catch(async (error) => {
          await backoffice.screenshot({ path: join(root, '.local', `screenshots-failed-${name}.png`) }).catch(() => {});
          throw error;
        });
        await settle(backoffice);
        await keep(name, await backoffice.screenshot());
      }
    }

    console.log(
      `screenshots: ${totals.count} picture(s) in docs/screenshots, ${(totals.after / 1024).toFixed(0)} KB ` +
        `(${(totals.before / 1024).toFixed(0)} KB as shot), each checked pixel for pixel`,
    );
  } finally {
    await browser.close();
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) await main();
