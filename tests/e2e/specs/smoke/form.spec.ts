import type { Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { CONTRACT_FORM, contractFormPage, contractOutcomePage } from '../../support/contract-form.ts';
import { formOn } from '../../support/form.ts';

// The form helpers against a page in the markup contract (ADR-0015), served by page.route: the post renders the page
// again with its errors, or redirects to the outcome (ADR-0004). From 6.2 on they drive Jiron Form Builder's own forms.

const ORIGIN = 'http://form.jfb.test';

/** Serves the contract page, and returns the answers of every post the page sends. */
async function serveContractForm(page: Page): Promise<URLSearchParams[]> {
  const posts: URLSearchParams[] = [];
  await page.route(`${ORIGIN}/**`, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const html = (body: string, status = 200) =>
      route.fulfill({ status, contentType: 'text/html; charset=utf-8', body });
    if (request.method() === 'POST' && url.pathname === CONTRACT_FORM.action) {
      const answers = new URLSearchParams(request.postData() ?? '');
      posts.push(answers);
      if (!answers.get('name')) return html(contractFormPage({ name: 'Enter your name' }));
      return route.fulfill({ status: 303, headers: { location: `${ORIGIN}/form?jfb-sent=1` } });
    }
    return html(url.searchParams.has('jfb-sent') ? contractOutcomePage() : contractFormPage());
  });
  return posts;
}

test('fillForm answers every kind of control, and submitForm reaches the outcome', async ({
  page,
  fillForm,
  submitForm,
}) => {
  const posts = await serveContractForm(page);
  await page.goto(`${ORIGIN}/form`);

  await fillForm({
    name: 'Mila Member',
    email: 'mila@visitor.jfb.test',
    guests: 2,
    topic: 'Events',
    visit: 'again',
    interests: ['Reading', 'games'],
    message: 'See you on Saturday.',
    consent: true,
  });
  const result = await submitForm();

  expect(result).toEqual({ state: 'sent', outcome: 'Thank you\n\nWe have your answers.' });
  expect(posts).toHaveLength(1);
  const answers = posts[0]!;
  expect(Object.fromEntries([...answers.keys()].map((key) => [key, answers.getAll(key)]))).toEqual({
    name: ['Mila Member'],
    email: ['mila@visitor.jfb.test'],
    guests: ['2'],
    topic: ['events'],
    visit: ['again'],
    interests: ['reading', 'games'],
    message: ['See you on Saturday.'],
    consent: ['true'],
    'jfb-consent-consent': [CONTRACT_FORM.consentHash],
    'jfb-hp': [''],
    'jfb-nav': ['submit'],
  });
});

test('submitForm returns the errors of the page the server renders again', async ({ page, fillForm, submitForm }) => {
  await serveContractForm(page);
  await page.goto(`${ORIGIN}/form`);

  await fillForm({ email: 'mila@visitor.jfb.test' });
  const result = await submitForm();

  expect(result).toEqual({ state: 'errors', errors: ['Enter your name'] });
  await expect(formOn(page).getByLabel('Your name')).toHaveAttribute('aria-invalid', 'true');
});

test('submitForm waits until a script shows its errors in place and moves focus to them', async ({
  page,
  submitForm,
}) => {
  await serveContractForm(page);
  await page.goto(`${ORIGIN}/form`);
  // What the runtime does (ADR-0004): it checks the answers, shows the summary in place and moves focus to it.
  await page.evaluate(() => {
    const form = document.querySelector('form.jfb-form') as HTMLFormElement;
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      form.setAttribute('data-jfb-busy', '');
      setTimeout(() => {
        const summary = document.createElement('div');
        summary.className = 'jfb-error-summary';
        summary.setAttribute('data-jfb-summary', '');
        summary.setAttribute('role', 'alert');
        summary.tabIndex = -1;
        summary.innerHTML =
          '<h2>Please check these answers</h2><ul><li><a href="#jfb-1-name">Enter your name</a></li></ul>';
        form.prepend(summary);
        form.removeAttribute('data-jfb-busy');
        summary.focus();
      }, 300);
    });
  });

  const result = await submitForm();

  expect(result).toEqual({ state: 'errors', errors: ['Enter your name'] });
});

test('fillForm refuses a field that a rule hides and a field the form does not have', async ({ page, fillForm }) => {
  await serveContractForm(page);
  await page.goto(`${ORIGIN}/form`);

  await expect(fillForm({ company: 'Lindenhall' })).rejects.toThrow('The field "company" is hidden by a rule');
  await expect(fillForm({ phone: '123' })).rejects.toThrow('The form has no field(s) "phone"');
  await expect(fillForm({ topic: 'Films' })).rejects.toThrow('The select has no option "Films"');
});
