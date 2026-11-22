import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Browser, type Locator, type Page } from '@playwright/test';
import { settled } from '../../support/axe.ts';
import { pathOf, type Language } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';
import { logicCome, press, pressOnThePage } from '../../support/form.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { PLACES_BASE_URL, PLACES_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';

// What cached markup cannot carry (ROADMAP 7.1e, ADR-0156), on a host of its own whose pages come from Umbraco's output
// cache, so every visitor gets the same bytes: the challenge's answer tells the page the places left on the demo's
// volunteer evenings before the visitor touches the form, the numbers follow another visitor's send on the page the
// cache still serves, a full option is disabled, and a visitor who chose it before it filled up keeps their answers and
// hears why. A member's rule shows what the page holds for a member, and no one else's page shows it. axe finds no
// WCAG 2.2 A or AA violation.

test.describe.configure({ mode: 'serial', timeout: 120_000 });
test.use({ baseURL: PLACES_BASE_URL });

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${PLACES_PORT}`,
    port: PLACES_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
      JironFormBuilderTesting__Csp: 'true',
      Umbraco__CMS__Website__OutputCache__Enabled: 'true',
      Umbraco__CMS__Website__OutputCache__ContentDuration: '00:05:00',
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

const pathOfVolunteer = (language: Language = 'en'): string =>
  pathOf({ name: language === 'en' ? 'Volunteer' : 'Ehrenamt', heading: '' }, language);

/** The page the form's block goes to after a send. */
const THANKS = /thank-you/;

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');
const evening = (form: Locator, value: string): Locator => form.locator(`[name="evening"][value="${value}"]`);
const labelOf = (form: Locator, value: string): Locator =>
  form.locator(`[data-jfb-alias="evening"] .jfb-choice:has([value="${value}"]) label`);

async function axe(page: Page, what: string): Promise<void> {
  await settled(page);
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(
    result.violations.map(
      (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
    ),
    `${what}: axe`,
  ).toEqual([]);
}

/** Fills in what the form asks up to its second page, as a visitor does. */
async function upToTheEvenings(form: Locator, who: string): Promise<void> {
  await logicCome(form);
  await form.locator('[name="name"]').fill(who);
  await form.locator('[name="email"]').fill('visitor@visitor.jfb.test');
  await form.locator('[name="role"][value="events"]').check();
  await press(form, 'next');
  await expect(form.locator('[name="days"]').first()).toBeVisible();
  await form.locator('[name="days"][value="mon"]').check();
  await form.locator('[data-jfb-alias="privacy"] input[type="checkbox"]').check();
}

/** Another visitor takes an evening, in a browser of their own. */
async function anotherVisitorTakes(browser: Browser, value: string): Promise<void> {
  const context = await browser.newContext({ baseURL: PLACES_BASE_URL });
  const page = await context.newPage();
  await page.goto(pathOfVolunteer());
  const form = formOn(page);
  await upToTheEvenings(form, `Another for ${value}`);
  await evening(form, value).check();
  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await page.waitForURL(THANKS);
  await context.close();
}

test('the places show before a touch on a page the cache serves, which holds none itself', async ({
  browser,
  page,
}) => {
  // Another visitor's visit renders the page, and the cache keeps it.
  const first = await browser.newPage({ baseURL: PLACES_BASE_URL });
  expect((await first.goto(pathOfVolunteer()))?.ok()).toBe(true);
  await first.close();

  const response = await page.goto(pathOfVolunteer());
  expect(response?.headers()['age'], 'the page comes from the output cache').toBeDefined();
  expect(await response?.text(), 'no number in the bytes everyone gets').not.toContain('places left');

  const form = formOn(page);
  await expect(labelOf(form, 'tue')).toHaveText('Tuesday 12 places left');
  await expect(labelOf(form, 'thu')).toHaveText('Thursday 1 place left');
  await expect(evening(form, 'thu')).toBeEnabled();
});

test('the numbers follow another visitor on the cached page, and a full option is disabled, with its reason', async ({
  browser,
  page,
}) => {
  await anotherVisitorTakes(browser, 'tue');
  const response = await page.goto(pathOfVolunteer());
  expect(response?.headers()['age'], 'the page still comes from the output cache').toBeDefined();
  const form = formOn(page);
  await expect(labelOf(form, 'tue')).toHaveText('Tuesday 11 places left');
  await expect(labelOf(form, 'thu')).toHaveText('Thursday 1 place left');

  // Someone with the page open chooses the last place on Thursday, as another visitor takes it.
  await upToTheEvenings(form, 'Slow Sam');
  await evening(form, 'thu').check();
  await anotherVisitorTakes(browser, 'thu');

  await pressOnThePage(form.locator('button[data-jfb-nav="submit"]'));
  await expect(form.locator('[data-jfb-summary]')).toBeFocused();
  await expect(form.locator('[data-jfb-alias="evening"] .jfb-error')).toContainText('Thursday is now full');
  await expect(evening(form, 'thu'), 'the answer stays theirs to change').toBeChecked();
  await expect(labelOf(form, 'thu'), 'and the page hears the numbers anew').toHaveText('Thursday Full');
  await expect(form.locator('[name="days"][value="mon"]')).toBeChecked();

  const next = await page.goto(pathOfVolunteer());
  expect(next?.headers()['age'], 'and the next visit comes from the cache as well').toBeDefined();
  const fresh = formOn(page);
  await expect(labelOf(fresh, 'thu')).toHaveText('Thursday Full');
  await expect(evening(fresh, 'thu')).toBeDisabled();
  await expect(labelOf(fresh, 'tue')).toHaveText('Tuesday 11 places left');
  await upToTheEvenings(fresh, 'Quick Quinn');
  await expect(evening(fresh, 'thu')).toBeDisabled();
  await axe(page, 'the evenings with a full one');

  await evening(fresh, 'tue').check();
  await pressOnThePage(fresh.locator('button[data-jfb-nav="submit"]'));
  await page.waitForURL(THANKS);
});

test("the places speak the page's language", async ({ page }) => {
  await page.goto(pathOfVolunteer('de'));
  const form = formOn(page);
  await expect(labelOf(form, 'tue')).toHaveText('Dienstag 10 Pl\u00e4tze frei');
  await expect(labelOf(form, 'thu')).toHaveText('Donnerstag Ausgebucht');
});

test('a rule on the member shows for a signed-in member, on the page every visitor gets, and for no one else', async ({
  browser,
  page,
}) => {
  const line = 'You are a member already';
  const visitor = await browser.newPage({ baseURL: PLACES_BASE_URL });
  await visitor.goto(pathOfVolunteer());
  await expect(labelOf(formOn(visitor), 'tue')).toContainText('places left');
  await expect(formOn(visitor).getByText(line)).toBeHidden();
  await visitor.close();

  const member = readSecrets().members[0]!;
  await signInMember(page, member.email, member.password);
  const response = await page.goto(pathOfVolunteer());
  expect(await response?.text(), 'the markup says so as it says to anyone').toContain('data-jfb-hidden');
  const form = formOn(page);
  await expect(form.getByText(line)).toBeVisible();
  await axe(page, 'the form for a member');
});
