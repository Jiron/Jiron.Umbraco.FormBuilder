import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { E2E_FORM_LIMIT } from '../../support/host-env.ts';

// A visitor without JavaScript over a rate limit (ROADMAP 7.1a, ADR-0012, ADR-0137): the page comes again with 429, the
// visitor's answers, and a notice that says when to send again. The spec uses up what one address may send to the demo's
// contact form in a minute, so its project runs after every spec that posts to that form.

test.use({ javaScriptEnabled: false });

const FORM = formPages().find((page) => page.form === 'contact');
if (!FORM) throw new Error('The demo has no contact page.');
const PATH = pathOf(FORM.texts.en, 'en');

test('a post over the rate limit comes back with 429, its answers and when to send again', async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto(PATH);
  const form = page.locator('main form.jfb-form');
  const hidden = ['ufprt', 'jfb-form', 'jfb-instance', 'jfb-revision'];
  const fields: Record<string, string> = { 'jfb-nav': 'submit' };
  for (const name of hidden) {
    fields[name] = (await form.locator(`input[name="${name}"]`).first().getAttribute('value')) ?? '';
  }

  const action = (await form.getAttribute('action'))!.split('#')[0]!;
  const posted = [];
  for (let sent = 0; sent < E2E_FORM_LIMIT; sent += posted.length) {
    posted.length = 0;
    posted.push(
      ...Array.from({ length: Math.min(10, E2E_FORM_LIMIT - sent) }, () => page.request.post(action, { form: fields })),
    );
    for (const answer of await Promise.all(posted)) expect(answer.status(), 'within the limit').toBe(200);
  }

  await form.locator('[name="name"]').fill('Mira Okafor');
  const answered = page.waitForResponse((response) => response.request().method() === 'POST');
  await form.locator('.jfb-navigation .jfb-button-primary').click();
  const answer = await answered;

  expect(answer.status()).toBe(429);
  expect(Number(answer.headers()['retry-after']), 'says when').toBeGreaterThan(0);
  expect(answer.headers()['cache-control']).toContain('no-store');
  const notice = form.locator('.jfb-notice-error');
  await expect(notice).toContainText(/Wait \d+ (seconds|minutes), then send it again\./);
  await expect(notice).toContainText('Your answers are kept.');
  await expect(form.locator('[name="name"]')).toHaveValue('Mira Okafor');
});
