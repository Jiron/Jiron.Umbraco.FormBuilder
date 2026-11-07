import type { APIRequestContext, Frame, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInFrame, axeInside } from '../../support/axe.ts';
import { API, formWithFields, unique, type DraftField } from '../../support/forms-api.ts';
import { bodyOf, headerOf, visibleText } from '../../support/mail.ts';

// The preview (ROADMAP 10.3e and 10.3f, IA 5.7, ADR-0203, ADR-0204): Preview in the footer saves what is not saved yet
// and shows the draft as visitors would get it, in a frame whose origin is opaque, at the designer's widths with
// "Narrowed to fit", light or dark, and in another language; the frame's own requests reach the public API from the
// origin null with their ticket, and its document meets axe as it stands, light and dark. An open preview's ticket is
// renewed over the frame's port once little is left of it. Fill with sample answers answers a field of every type that
// takes one, a file and a signature among them, so the form sends as it stands; what it sends is a test entry, which
// runs no action until Run actions... runs them after asking, and runs them at once with Run actions for test entries.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';
const SANDBOX = 'allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox';

/** Each field's label in the stored draft. */
async function labelsOf(api: APIRequestContext, key: string): Promise<unknown[]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages: { groups: { fields: { label?: Record<string, string> }[] }[] }[] };
  };
  return draft.definition.pages[0]?.groups[0]?.fields.map((field) => field.label?.['']) ?? [];
}

/** Relabels a field in the Design view's inspector, without saving. */
async function relabel(page: Page, from: string, to: string) {
  const design = page.locator('jfb-form-design-view');
  await design.getByRole('button', { name: new RegExp(`^${from}, `) }).click();
  await design.locator('jfb-designer-inspector').getByLabel('Label', { exact: true }).fill(to);
}

/** The preview's frame, once its document is there. */
async function frameOf(page: Page): Promise<Frame> {
  const frame = await (await page.locator('jfb-preview-modal iframe').elementHandle())?.contentFrame();
  if (!frame) throw new Error('The preview has no frame.');
  await frame.waitForLoadState();
  return frame;
}

test('the preview shows the saved draft in a sandboxed frame, at each width, light and dark, and in German', async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(180_000);
  const api = await apiAs('designer');
  const name = unique('Preview');
  const key = await formWithFields(api, name, [
    { type: 'text', alias: 'name', label: { '': 'Name', 'de-DE': 'Ihr Name' } },
    { type: 'email', alias: 'email', label: { '': 'Email', 'de-DE': 'E-Mail' } },
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  const design = page.locator('jfb-form-design-view');
  await expect(design.getByRole('button', { name: /^Name, / })).toBeVisible();

  // A label changed and not saved: Preview saves it first, and the frame shows the draft with it.
  await relabel(page, 'Email', 'Work email');
  await page.getByRole('button', { name: 'Preview', exact: true }).click();
  const preview = page.locator('jfb-preview-modal');
  await expect(preview.getByRole('heading', { name: /^Preview of Preview / })).toBeVisible();
  await expect(preview.locator('iframe')).toHaveAttribute('sandbox', SANDBOX);
  const shown = page.frameLocator('jfb-preview-modal iframe');
  await expect(shown.getByLabel('Work email')).toBeVisible();
  expect(await labelsOf(api, key)).toEqual(['Name', 'Work email']);

  // The frame's origin is opaque: it reaches neither the backoffice's page nor its cookies. Its page is titled with the
  // form's name.
  let frame = await frameOf(page);
  expect(await frame.evaluate(() => document.title)).toBe(name);
  expect(
    await frame.evaluate(() => {
      const blocked = (read: () => unknown) => {
        try {
          read();
          return 'read';
        } catch {
          return 'blocked';
        }
      };
      return [self.origin, blocked(() => window.parent.document.title), blocked(() => document.cookie)];
    }),
  ).toEqual(['null', 'blocked', 'blocked']);

  // The form's runtime asks for its challenge as the visitor starts: from the origin null, with the preview's ticket.
  const challenge = page.waitForResponse((response) => response.url().includes(`/jfb/api/v1/forms/${key}/challenge`));
  await shown.getByLabel(/^Name/).fill('Alex');
  const answered = await challenge;
  expect(answered.status()).toBe(200);
  expect(await answered.request().headerValue('origin')).toBe('null');
  expect(await answered.request().headerValue('authorization')).toMatch(/^Jfb-Preview [A-Za-z0-9_-]+$/);
  expect(await answered.headerValue('access-control-allow-origin')).toBe('null');
  await axeInFrame(frame, 'the preview, light');

  // The widths are the designer's: the frame's own window is the width chosen.
  const widths = preview.getByRole('group', { name: 'Width' });
  await expect(widths.getByRole('button', { name: 'Desktop' })).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(() => frame.evaluate(() => window.innerWidth)).toBe(960);
  await widths.getByRole('button', { name: 'Tablet' }).click();
  await expect.poll(() => frame.evaluate(() => window.innerWidth)).toBe(768);
  await widths.getByRole('button', { name: 'Phone' }).click();
  await expect.poll(() => frame.evaluate(() => window.innerWidth)).toBe(375);
  await expect(preview.getByText('Narrowed to fit')).toHaveCount(0);

  // A window narrower than the width chosen narrows the frame and says so.
  await widths.getByRole('button', { name: 'Desktop' }).click();
  await page.setViewportSize({ width: 900, height: 900 });
  await expect(preview.getByText('Narrowed to fit')).toBeVisible();
  expect(await frame.evaluate(() => window.innerWidth)).toBeLessThan(960);
  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(preview.getByText('Narrowed to fit')).toHaveCount(0);

  // Dark: the page turns dark, and the answer given stays.
  await preview.getByRole('button', { name: 'More preview options' }).click();
  await preview.getByRole('group', { name: 'Colour scheme' }).getByRole('button', { name: 'Dark' }).click();
  await expect.poll(() => frame.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark');
  await expect(shown.getByLabel(/^Name/)).toHaveValue('Alex');
  await axeInFrame(frame, 'the preview, dark');
  await axeInside(page, 'jfb-preview-modal');

  // Another language: the draft in German, rendered again, still dark.
  await preview.getByRole('button', { name: 'More preview options' }).click();
  await preview
    .getByRole('group', { name: 'Language' })
    .getByRole('button', { name: /German|Deutsch/ })
    .click();
  await expect(shown.getByLabel('Ihr Name')).toBeVisible();
  frame = await frameOf(page);
  expect(
    await frame.evaluate(() => [document.documentElement.lang, getComputedStyle(document.documentElement).colorScheme]),
  ).toEqual(['de-DE', 'dark']);

  // Light again, and closed.
  await preview.getByRole('button', { name: 'More preview options' }).click();
  await preview.getByRole('group', { name: 'Colour scheme' }).getByRole('button', { name: 'Light' }).click();
  await expect.poll(() => frame.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('light');
  await preview.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(preview).toHaveCount(0);
});

/** An option list's items, each labelled as its value with a capital. */
const items = (...values: string[]) => ({
  items: values.map((value) => ({ value, label: { '': value.charAt(0).toUpperCase() + value.slice(1) } })),
});

/** A field of every type that takes an answer, each one needing it, and of those that take none. */
const everyType = (): DraftField[] => [
  ...[
    { type: 'text', alias: 'fullName', label: { '': 'Full name' } },
    { type: 'textarea', alias: 'message', label: { '': 'Message' } },
    { type: 'email', alias: 'email', label: { '': 'Email' } },
    { type: 'phone', alias: 'phone', label: { '': 'Phone' } },
    { type: 'url', alias: 'website', label: { '': 'Website' } },
    { type: 'number', alias: 'guests', label: { '': 'Guests' } },
    { type: 'date', alias: 'day', label: { '': 'Day' } },
    { type: 'time', alias: 'arrival', label: { '': 'Arrival' } },
    { type: 'datetime', alias: 'visit', label: { '': 'Visit' } },
    { type: 'rating', alias: 'stars', label: { '': 'Stars' } },
    { type: 'scale', alias: 'likely', label: { '': 'How likely' } },
    { type: 'slider', alias: 'budget', label: { '': 'Budget' } },
    { type: 'dropdown', alias: 'size', label: { '': 'Size' }, options: items('small', 'large') },
    { type: 'radio', alias: 'room', label: { '': 'Room' }, options: items('hall', 'garden') },
    { type: 'checkboxes', alias: 'topics', label: { '': 'Topics' }, options: items('press', 'billing') },
    {
      type: 'grid',
      alias: 'dayRating',
      label: { '': 'Rate the day' },
      rows: [
        { key: crypto.randomUUID(), label: { '': 'The talks' } },
        { key: crypto.randomUUID(), label: { '': 'The food' } },
      ],
      options: items('good', 'poor'),
    },
    { type: 'ranking', alias: 'order', label: { '': 'Order' }, options: items('first', 'second', 'third') },
    { type: 'checkbox', alias: 'newsletter', label: { '': 'Newsletter' } },
    { type: 'yesno', alias: 'returning', label: { '': 'Returning' } },
    {
      type: 'consent',
      alias: 'terms',
      label: { '': 'Terms' },
      settings: { text: { '': 'I accept the terms of the event.' } },
    },
    { type: 'country', alias: 'country', label: { '': 'Country' } },
    { type: 'name', alias: 'person', label: { '': 'Your name' } },
    { type: 'address', alias: 'address', label: { '': 'Address' } },
    { type: 'file', alias: 'cv', label: { '': 'CV' } },
    { type: 'signature', alias: 'signed', label: { '': 'Signature' } },
    {
      type: 'repeater',
      alias: 'party',
      label: { '': 'Party' },
      fields: [{ key: crypto.randomUUID(), type: 'text', alias: 'guest', label: { '': 'Guest' }, required: true }],
    },
  ].map((field) => ({ ...field, required: true })),
  // And those a visitor gives no answer to: worked out, hidden, or content.
  { type: 'calculation', alias: 'total', label: { '': 'Total' }, settings: { formula: 'guests * 2' } },
  { type: 'hidden', alias: 'campaign', label: { '': 'Campaign' }, defaultValue: 'preview' },
  { type: 'heading', alias: 'about', settings: { text: { '': 'About you' } } },
  { type: 'paragraph', alias: 'intro', settings: { html: { '': '<p>Tell us a little.</p>' } } },
  { type: 'divider', alias: 'line' },
];

/** Gives the form's draft an email to the team on each entry, its subject naming the form. */
async function emailTheTeam(api: APIRequestContext, key: string) {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown>;
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        actions: [
          {
            key: crypto.randomUUID(),
            type: 'email',
            name: 'Email the team',
            enabled: true,
            trigger: 'submitted',
            stopOnFailure: false,
            settings: {
              to: ['team@example.com'],
              subject: { '': 'Test entry of {{ form.name }}' },
              body: { '': '<p>From {{ field.fullName }}.</p>' },
            },
          },
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
}

test('sample answers fill a field of every type so the form sends, as a test entry whose actions run only when asked', async ({
  page,
  loginAs,
  apiAs,
  waitForMail,
}) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  const name = unique('Sampled');
  const key = await formWithFields(api, name, everyType());
  await emailTheTeam(api, key);
  const teamMail = (mail: { raw: string }) =>
    headerOf(mail.raw, 'To').includes('team@example.com') && headerOf(mail.raw, 'Subject').includes(name);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(`${SECTION}/${key}/view/design`);
  await page.getByRole('button', { name: 'Preview', exact: true }).click();
  const preview = page.locator('jfb-preview-modal');
  const note = preview.locator('.note');
  await expect(note).toHaveText('Entries sent here are test entries. They run no actions.');

  // The sample answers fill every field; the file field gets its sample file, uploaded with the ticket.
  const uploaded = page.waitForResponse(
    (response) => response.url().includes(`/jfb/api/v1/forms/${key}/uploads`) && response.request().method() === 'POST',
  );
  await preview.getByRole('button', { name: 'Fill with sample answers' }).click();
  const shown = page.frameLocator('jfb-preview-modal iframe');
  await expect(shown.getByLabel(/^Full name/)).toHaveValue('Full name');
  await expect(shown.getByLabel(/^Email/)).toHaveValue('alex.berger@example.com');
  expect((await uploaded).status()).toBe(200);

  // Sent as it stands: the outcome shows, and the note names the test entry, whose action did not run.
  const sent = page.waitForResponse(
    (response) => response.url().endsWith(`/jfb/api/v1/forms/${key}/entries`) && response.request().method() === 'POST',
  );
  await shown.locator('form[data-jfb-form] button[type="submit"]').last().click();
  const answer = await sent;
  expect(answer.status(), await answer.text()).toBe(200);
  await expect(shown.locator('[data-jfb-outcome]')).toBeVisible();
  await expect(note.locator('span').first()).toHaveText(/^\s*Saved as test entry #\d+\.\s+No actions ran\.\s*$/);
  const run = note.getByRole('button', { name: /^Run actions for test entry #\d+\u2026$/ });
  await expect(run).toBeVisible();
  await axeInside(page, 'jfb-preview-modal');

  // Run actions... asks first, naming the action and whom it emails; then the email goes out.
  await run.click();
  const confirm = page.locator('umb-confirm-modal');
  await expect(confirm.getByRole('heading', { name: /^Run the actions for test entry #\d+\?$/ })).toBeVisible();
  await expect(
    confirm.getByText('This action runs as it would for a real entry, so its email or message really goes out.'),
  ).toBeVisible();
  await expect(confirm.locator('li')).toHaveText(['Email the team To team@example.com']);
  await confirm.getByRole('button', { name: 'Run 1 action' }).click();
  await expect(note).toHaveText(/^\s*Saved as test entry #\d+\.\s+Its actions are running\.\s*$/);
  const first = await waitForMail(teamMail, 30_000);
  expect(visibleText(bodyOf(first.raw))).toContain('From Full name.');

  // With Run actions for test entries on, the next one sends its email at once.
  await preview.getByRole('button', { name: 'Start again' }).click();
  await expect(note).toHaveText('Entries sent here are test entries. They run no actions.');
  await preview.getByRole('button', { name: 'More preview options' }).click();
  await preview
    .getByRole('group', { name: 'Test entries' })
    .getByRole('button', { name: 'Run actions for test entries' })
    .click();
  await expect(note).toHaveText(
    'Entries sent here are test entries. Their actions run, so their emails and messages really go out.',
  );
  const uploadedAgain = page.waitForResponse(
    (response) => response.url().includes(`/jfb/api/v1/forms/${key}/uploads`) && response.request().method() === 'POST',
  );
  await preview.getByRole('button', { name: 'Fill with sample answers' }).click();
  await expect(shown.getByLabel(/^Full name/)).toHaveValue('Full name');
  await expect(note).toHaveText(
    'Entries sent here are test entries. Their actions run, so their emails and messages really go out.',
  );
  expect((await uploadedAgain).status()).toBe(200);
  await shown.locator('form[data-jfb-form] button[type="submit"]').last().click();
  await expect(shown.locator('[data-jfb-outcome]')).toBeVisible();
  await expect(note).toHaveText(/^\s*Saved as test entry #\d+\.\s+Its actions are running\.\s*$/);
  await waitForMail((mail) => teamMail(mail) && mail.file !== first.file, 30_000);
});

test("an open preview's ticket is renewed over the frame's port once little is left of it", async ({
  page,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(120_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Renewed'), [{ type: 'text', alias: 'name', label: { '': 'Name' } }]);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  // The preview as one opened nine minutes ago: its answer tells the backoffice and the frame that a minute is left of
  // the ticket. (A clock moved by the test reaches the backoffice but not the sandboxed frame.)
  await page.route(
    (url) => url.pathname.endsWith(`/form/${key}/preview`),
    async (route) => {
      const response = await route.fetch();
      const read = (await response.json()) as { document: string; lifetimeSeconds: number };
      expect(read.document).toContain('data-lifetime="600"');
      await route.fulfill({
        response,
        json: {
          ...read,
          document: read.document.replace('data-lifetime="600"', 'data-lifetime="60"'),
          lifetimeSeconds: 60,
        },
      });
    },
  );
  await page.goto(`${SECTION}/${key}/view/design`);
  await page.getByRole('button', { name: 'Preview', exact: true }).click();
  const shown = page.frameLocator('jfb-preview-modal iframe');
  await expect(shown.getByLabel(/^Name/)).toBeVisible();
  const frame = await frameOf(page);
  const held = await frame.evaluate(() => document.querySelector('meta[name="jfb-preview"]')?.getAttribute('content'));
  expect(held).toBeTruthy();

  // The first request asks over the port for a new ticket, which the backoffice gets for the version shown, and goes
  // with it: the public API takes it.
  const renewed = page.waitForResponse((response) => response.url().endsWith(`/form/${key}/preview/ticket`));
  const challenge = page.waitForResponse((response) => response.url().includes(`/jfb/api/v1/forms/${key}/challenge`));
  await shown.getByLabel(/^Name/).fill('Alex');
  expect((await renewed).status()).toBe(200);
  const answered = await challenge;
  expect(answered.status()).toBe(200);
  const sent = await answered.request().headerValue('authorization');
  expect(sent).toMatch(/^Jfb-Preview [A-Za-z0-9_-]+$/);
  expect(sent).not.toBe(`Jfb-Preview ${String(held)}`);
});
