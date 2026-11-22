import AxeBuilder from '@axe-core/playwright';
import { randomInt, randomUUID } from 'node:crypto';
import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axe, settled } from '../../support/axe.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';
import { fillForm, press, pressOnThePage } from '../../support/form.ts';

// The form picker on the demo (ROADMAP 6.7h, ADR-0136): the volunteer page shows the form its form block picks, and a
// visitor who sends it with JavaScript lands on the picker's page after a send; an editor of Umbraco's own Editors
// group, without the Forms section, adds a form block to a page of their own, picks the newsletter in it, publishes,
// and a visitor sends it to the same thanks page; axe finds nothing in the picker, its sidebar and its settings.

const MANAGEMENT_API = '/umbraco/management/api/v1';
const VOLUNTEER_KEY = 'b7d3e0a1-5c94-4f2e-8a6b-0d1c3f9e2a47';
const HOME_KEY = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';
/** The picker's data type that "Create a form block for me" makes (ADR-0130). */
const FORM_BLOCK_DATA_TYPE = 'c4f1a9e2-6b3d-4e58-9a07-2d8c5f1e3b64';
/** The picker's elements, whose violations count; the rest of the backoffice is Umbraco's. */
const PICKER = ['jfb-form-picker', 'jfb-form-picker-modal', 'jfb-form-picker-allowed', 'jfb-form-picker-theme'];

const volunteerPath = pathOf({ name: 'Volunteer', heading: '' }, 'en');
const thanks = pageOf('demoThanks', 'en');
const thanksPath = pathOf(thanks, 'en');

/** The page's own form; the newsletter in the footer is another. */
const formOf = (page: Page): Locator => page.locator('main form.jfb-form');

/** Axe's WCAG 2.2 A and AA rules on the backoffice page, the violations inside the picker's elements only. */
async function axeOnThePicker(page: Page, what: string): Promise<void> {
  await settled(page);
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  const ours = result.violations.flatMap((violation) =>
    violation.nodes
      .map((node) => JSON.stringify(node.target))
      .filter((target) => PICKER.some((element) => target.includes(element)))
      .map((target) => `${violation.id}: ${target}`),
  );
  expect(ours, `${what}: axe inside the picker`).toEqual([]);
}

/**
 * Sends the form and expects the thanks page, opened at its top: the post's own fragment (#jfb-1, the form on its page)
 * is not carried to it, where it would name the newsletter form in the footer (ADR-0136).
 */
async function sendToTheThanksPage(page: Page, form: Locator): Promise<void> {
  const answered = page.waitForResponse((response) => response.url().includes('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await answered;
  expect(answer.status(), 'sent with JavaScript').toBe(200);
  expect((answer.request().postDataJSON() as { placement?: string }).placement, "the picker's page").toBeTruthy();
  await expect(page).toHaveURL(new RegExp(`${thanksPath}#?$`));
  await expect(page.locator('main h1')).toHaveText(thanks.heading);
  expect(await page.evaluate(() => window.scrollY), 'the thanks page at its top').toBe(0);
}

test('the volunteer page shows the form its form block picks, which goes to the thanks page once sent', async ({
  page,
}) => {
  await page.goto(volunteerPath);
  const form = formOf(page);
  await expect(form).toBeVisible();
  await axe(page, 'the volunteer page', true);

  await fillForm(form, { name: 'Ada Lovelace', email: 'ada@visitor.jfb.test', role: 'reading' });
  await press(form, 'next');
  await fillForm(formOf(page), { days: ['sat'], privacy: true });
  await sendToTheThanksPage(page, formOf(page));
  await axe(page, 'the thanks page', true);
});

test("an editor picks a form in a block, publishes, and a visitor's send goes to the picker's page after a send", async ({
  page,
  browser,
  apiAs,
  loginAs,
}) => {
  test.setTimeout(180_000);
  // The editor's own page of the demo's page type, made through the Management API so the seeded pages stay as they are.
  const api = await apiAs('editor');
  const volunteer = (await (await api.get(`${MANAGEMENT_API}/document/${VOLUNTEER_KEY}`)).json()) as {
    documentType: { id: string };
    template: { id: string } | null;
  };
  const name = `Form block ${randomInt(100_000, 999_999)}`;
  const key = randomUUID();
  const created = await api.post(`${MANAGEMENT_API}/document`, {
    data: {
      id: key,
      parent: { id: HOME_KEY },
      documentType: { id: volunteer.documentType.id },
      template: volunteer.template,
      values: [],
      variants: [
        { culture: 'en-US', segment: null, name },
        { culture: 'de-DE', segment: null, name },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);

  // The editor adds a form block and picks the newsletter in its picker's sidebar.
  await loginAs('editor');
  await page.goto(`/umbraco/section/content/workspace/document/edit/${key}/en-us`);
  const blocks = page.locator('umb-property-editor-ui-block-list');
  await blocks.locator('uui-button[look="placeholder"]').first().click();
  await page.locator('uui-card-block-type[name="Form"]').click();
  const picker = page.locator('jfb-form-picker');
  await picker.locator('#choose').click();
  const sidebar = page.locator('jfb-form-picker-modal');
  const newsletter = sidebar.locator('button.row', { hasText: 'Newsletter' });
  await newsletter.click();
  await expect(newsletter).toHaveAttribute('aria-pressed', 'true');
  await axeOnThePicker(page, 'the sidebar');
  await sidebar.locator('uui-button[label="Choose"]').click();
  await expect(picker.locator('uui-ref-node')).toHaveAttribute('name', 'Newsletter');
  await axeOnThePicker(page, 'the picker in the block');
  await page
    .getByRole('button', { name: /^create$/i })
    .last()
    .click();
  // The block's label names the picked form (ADR-0134).
  await expect(blocks.locator('ufm-jfb-form-name')).toHaveText('Newsletter');

  // A page in two languages asks which to publish, the editor's own chosen.
  await page.getByRole('button', { name: /^save and publish/i }).click();
  // The dialog's button has no ellipsis; its content is slotted, which a role inside the dialog does not reach.
  await page.getByRole('button', { name: 'Save and publish', exact: true }).click();

  // A visitor sends the newsletter with JavaScript and lands on the page after a send.
  const visitor = await browser.newPage();
  try {
    const path = pathOf({ name, heading: '' }, 'en');
    await expect.poll(async () => (await visitor.goto(path))?.status(), { timeout: 30_000 }).toBe(200);
    const form = formOf(visitor);
    await fillForm(form, { email: 'grace@visitor.jfb.test', agree: true });
    await sendToTheThanksPage(visitor, form);
  } finally {
    await visitor.close();
  }
});

test("a send with JavaScript carries the picker's page in its token, and a value a visitor changed is refused", async ({
  page,
}) => {
  await page.goto(volunteerPath);
  await fillForm(formOf(page), { name: 'Ada Lovelace', email: 'ada@visitor.jfb.test', role: 'reading' });
  await press(formOf(page), 'next');
  const form = formOf(page);
  await fillForm(form, { days: ['sat'], privacy: true });
  const token = await form.getAttribute('data-jfb-placement');
  expect(token, 'the form carries it').toBeTruthy();
  expect(token, 'that a visitor cannot read').not.toContain(thanks.name);

  // A changed token is refused, nothing is stored, and the post that follows goes by the route token, which is intact.
  await form.evaluate((element) => {
    const given = element.getAttribute('data-jfb-placement') ?? '';
    element.setAttribute('data-jfb-placement', (given.endsWith('A') ? 'B' : 'A') + given.slice(1));
  });
  const refused = page.waitForResponse((response) => response.url().includes('/entries'));
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  const answer = await refused;
  expect(answer.status(), 'refused').toBe(400);
  await expect(page).toHaveURL(new RegExp(`${thanksPath}#?$`));
});

test("the form block's picker settings name the thanks page, and axe finds nothing in them", async ({
  page,
  loginAs,
}) => {
  await loginAs('admin');
  await page.goto(`/umbraco/section/settings/workspace/data-type/edit/${FORM_BLOCK_DATA_TYPE}`);
  await expect(page.locator('jfb-form-picker-theme uui-select')).toBeVisible();
  await expect(page.locator('umb-property[alias="redirect"]')).toContainText(thanks.name);
  await expect(page.locator('jfb-form-picker-allowed #choose')).toHaveCount(2);
  await axeOnThePicker(page, 'the settings');

  await page.locator('jfb-form-picker-allowed').first().locator('#choose').click();
  // The test host's only folder holds no published form, so the folder mode says what makes a folder show.
  await expect(page.locator('jfb-form-picker-modal uui-box').locator('.message, button.row').first()).toBeVisible();
  await axeOnThePicker(page, "the settings' sidebar of folders");
});
