import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, formOf, formWithFields, unique } from '../../support/forms-api.ts';
import { STUB_URL } from '../../support/paths.ts';

// An action's last run and its Details (ROADMAP 10.2f, IA 5.3, ADR-0195): a form with two webhooks gets an entry; the
// stub takes one and answers the other 503. Each card says its last run, the delivered one muted and the failed one with
// its next try; the failed one's Details say what the server answered and the tries, never the entry, who can retry it,
// and offer Test, which asks the server again.

const SECTION = '/umbraco/section/jfb-forms/workspace/jfb-form/edit';

interface LastRun {
  readonly actionKey: string;
  readonly state: string;
  readonly attempts: number;
}

/** The form's last runs as the Actions view reads them. */
const lastRuns = async (api: APIRequestContext, key: string) =>
  ((await (await api.get(`${API}/form/${key}/action/last-run`)).json()) as { runs: LastRun[] }).runs;

test("an action's last run is on its card, and a failed one's Details say what the server answered and the tries", async ({
  page,
  request,
  loginAs,
  apiAs,
}) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const key = await formWithFields(api, unique('Last runs'), [
    { type: 'text', alias: 'name', label: { '': 'Your name' } },
  ]);
  const broken = `/broken/${randomUUID()}`;
  const [delivered, failing] = [randomUUID(), randomUUID()];
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown> & { settings?: Record<string, unknown> };
  };
  const webhook = (actionKey: string, name: string, url: string) => ({
    key: actionKey,
    type: 'webhook',
    name,
    enabled: true,
    trigger: 'submitted',
    stopOnFailure: false,
    settings: { url },
  });
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: {
      concurrencyVersion: draft.form.concurrencyVersion,
      definition: {
        ...draft.definition,
        // The entry goes through the public API.
        settings: {
          ...draft.definition.settings,
          developer: { ...(draft.definition.settings?.['developer'] as object | undefined), headless: true },
        },
        actions: [
          webhook(delivered, 'Tell the CRM', `${STUB_URL}/webhooks/${randomUUID()}`),
          webhook(failing, 'Tell the archive', `${STUB_URL}${broken}`),
        ],
      },
    },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);

  // The archive is down.
  const scripted = await request.put(`${STUB_URL}/_stub/answers`, {
    data: { method: 'POST', path: broken, status: 503, body: '{"down":true}' },
  });
  expect(scripted.ok(), await scripted.text()).toBe(true);

  // A visitor sends the form.
  let revision = 0;
  await expect
    .poll(async () => {
      const served = await request.get(`/jfb/api/v1/forms/${key}`);
      revision = served.ok() ? ((await served.json()) as { revision: number }).revision : 0;
      return revision;
    })
    .toBeGreaterThan(0);
  const asked = await request.post(`/jfb/api/v1/forms/${key}/challenge`);
  expect(asked.ok(), await asked.text()).toBe(true);
  const { challenge } = (await asked.json()) as { challenge: string };
  const sent = await request.post(`/jfb/api/v1/forms/${key}/entries`, {
    data: { revision, challenge, values: { name: 'Ada Visitor' } },
  });
  expect(sent.ok(), await sent.text()).toBe(true);

  // Both runs have had their first attempt: one went through, the other waits for its next try.
  await expect
    .poll(
      async () =>
        (await lastRuns(api, key))
          .map((run) => `${run.actionKey === delivered ? 'crm' : 'archive'}:${run.state}:${String(run.attempts)}`)
          .sort(),
      { timeout: 90_000 },
    )
    .toEqual(['archive:pending:1', 'crm:succeeded:1']);

  await loginAs('designer');
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`${SECTION}/${key}/view/actions`);
  const view = page.locator('jfb-form-actions-view');
  const card = (name: string) => view.locator('jfb-action-list .card').filter({ hasText: name });
  await expect(card('Tell the CRM').locator('.run')).toHaveText(/^\s*Delivered (just now|\d+ minutes? ago)\s*$/);
  const failed = card('Tell the archive').locator('.run');
  await expect(failed).toHaveText(/^\s*Failed at .+\. Next try at .+\.\s*Details\s*$/);

  await failed.getByRole('button', { name: 'Details of the last run of Tell the archive' }).click();
  const details = page.locator('jfb-action-run-modal');
  await expect(details.getByRole('heading', { name: 'What happened' })).toBeVisible();
  await expect(details.locator('.part').nth(1)).toHaveText(/What happened\s*The server answered 503\./);
  await expect(details.locator('.part').nth(2)).toHaveText(/Tries\s*Tried (once|\d+ times, the first at .+)\./);
  await expect(details).toContainText(
    `An administrator can also retry it in Activity ${String.fromCharCode(0x203a)} Deliveries.`,
  );
  // Never the entry.
  await expect(details).not.toContainText('Ada Visitor');
  await axeInside(page, 'jfb-action-run-modal');

  // Test asks the server again, after a yes, and the toast says what it answered.
  await details.getByRole('button', { name: 'Test', exact: true }).click();
  await page.locator('umb-confirm-modal').getByRole('button', { name: 'Send test' }).click();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'The server answered 503' })).toBeVisible();
  await details.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(details).toHaveCount(0);
});
