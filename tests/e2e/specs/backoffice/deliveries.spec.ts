import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { unique } from '../../support/forms-api.ts';
import { waitForStartupWork } from '../../support/host-health.ts';
import { hostEnvironment } from '../../support/host-env.ts';
import { startHost, type Host } from '../../support/host.ts';
import { PAUSED_BASE_URL, PAUSED_PORT } from '../../support/paths.ts';
import { readSecrets } from '../../support/users.ts';
import { goToTab } from '../../support/places.ts';

// Activity > Deliveries (ROADMAP 12.4b, IA 9, ADR-0219), on a host of its own that pauses sending, so the deliveries a
// test endpoint seeds stay as they are: the screen says once that sending is paused, with its key; the problems come
// first; a limited mail names its limit and offers no retry; an uncertain webhook run asks before its retry, as it may
// deliver twice; and a failed mail is queued again at once. The run's host keeps sending, as its other journeys need.

const SECTION = '/umbraco/section/jfb-forms';

test.describe.configure({ mode: 'serial', timeout: 180_000 });
test.use({ baseURL: PAUSED_BASE_URL });

let host: Host | undefined;

test.beforeAll(async () => {
  test.setTimeout(600_000);
  host = await startHost({
    name: `host-${PAUSED_PORT}`,
    port: PAUSED_PORT,
    env: {
      ...hostEnvironment(readSecrets(), { deliveryApi: false }),
      // The run's host holds the stub's port; this one needs no stub.
      JironFormBuilderTesting__StubPort: '0',
      JironFormBuilder__Actions__Paused: 'true',
    },
  });
  const wait = await waitForStartupWork(host.logFile);
  if (wait.state !== 'done') throw new Error(`The paused host's start-up work ended as "${wait.state}".`);
});

test.afterAll(async () => {
  await host?.stop();
});

test('Deliveries lists problems first, says once that sending is paused, names a limit, and asks before an uncertain retry', async ({
  page,
  request,
  loginAs,
}) => {
  const name = unique('Deliveries');
  const seeded = await request.post('/jfb-testing/deliveries', { data: { name } });
  expect(seeded.ok(), await seeded.text()).toBe(true);
  const { visitor } = (await seeded.json()) as { visitor: string };

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('admin');
  await page.goto(SECTION);
  await goToTab(page, 'Activity', 'Deliveries');
  await expect(page).toHaveURL(/workspace\/jfb-deliveries$/);
  const screen = page.locator('jfb-deliveries-screen');
  const paused = screen.locator('.paused');
  await expect(paused).toHaveCount(1, { timeout: 60_000 });
  await expect(paused).toContainText('Sending is paused by the host setting JironFormBuilder:Actions:Paused.');

  await screen.getByLabel('Search recipients and forms').fill(name);
  const rows = screen.locator('tbody tr');
  await expect(rows).toHaveCount(4);
  await expect(rows.locator('uui-tag')).toHaveText([
    /Failed|Uncertain|Limited/,
    /Failed|Uncertain|Limited/,
    /Failed|Uncertain|Limited/,
    'Waiting',
  ]);
  await expect(paused, 'said once, above the list').toHaveCount(1);
  await axeInside(page, 'jfb-deliveries-screen');

  // A limited mail: the limit that held it back, its key, and no retry.
  const sidebar = page.locator('jfb-delivery-modal');
  await rows
    .filter({ has: page.locator('uui-tag', { hasText: 'Limited' }) })
    .getByRole('button')
    .click();
  await expect(sidebar).toContainText(`to ${visitor} that day, the most one address gets.`);
  await expect(sidebar.locator('code')).toHaveText('JironFormBuilder:Limits:Mail:PerRecipientPerDay');
  await expect(sidebar.locator('uui-button[label^="Retry"]')).toHaveCount(0);
  await axeInside(page, 'jfb-delivery-modal');
  await sidebar.getByRole('button', { name: 'Close' }).click();
  await expect(sidebar).toHaveCount(0);

  // An uncertain webhook run: Retry... asks first, and says it may deliver twice.
  await rows.filter({ hasText: 'crm.example.com' }).getByRole('button').click();
  await expect(sidebar).toContainText('The attempt stopped before the server answered, so it may have arrived.');
  await sidebar.getByRole('button', { name: 'Retry\u2026' }).click();
  const dialog = page.locator('umb-confirm-modal');
  await expect(dialog).toContainText('It may already have arrived, so retrying could deliver it twice.');
  await dialog.getByRole('button', { name: 'Retry anyway' }).click();
  await expect(
    page.locator('uui-toast-notification').filter({ hasText: 'The delivery is queued again.' }),
  ).toBeVisible();
  await expect(rows.filter({ hasText: 'crm.example.com' }).locator('uui-tag')).toHaveText('Waiting');

  // A failed mail goes again at once.
  await rows.filter({ hasText: 'team@example.com' }).getByRole('button').click();
  await expect(sidebar).toContainText("It stopped with an error. A developer finds why in the site's log.");
  await sidebar.getByRole('button', { name: 'Retry', exact: true }).click();
  await expect(
    page.locator('uui-toast-notification').filter({ hasText: 'The delivery is queued again.' }).last(),
  ).toBeVisible();
  await expect(rows.filter({ hasText: 'team@example.com' }).locator('uui-tag')).toHaveText('Waiting');
});
