import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axe } from '../../support/axe.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { signInMember } from '../../support/demo-members.ts';
import { fillForm, submitForm } from '../../support/form.ts';
import { demoProject } from '../../support/paths.ts';

// Prefill on the demo's contact form (ROADMAP 6.5c, ADR-0124): a signed-in member finds their name in its field and no
// email field at all, since their own address stands in for it, and the entry holds that address; every visitor finds
// the page's topic chosen, from the context value the contact page gives, and the field prefilled from the query takes
// the address's value the form allows, while a value under a name it does not allow is left out. The server's markup
// writes no query value. axe finds no WCAG 2.2 A or AA violation, and nothing writes an error to the console.

const formOf = (page: Page): Locator => page.locator('main form.jfb-form');

const contactPath = (): string => {
  const shown = formPages().find((each) => each.form === 'contact');
  if (shown === undefined) throw new Error('The demo has no contact page.');
  return pathOf(shown.texts.en, 'en');
};

/** The contact form's field keys by alias, from its seeded definition. */
function keysOfContact(): Record<string, string> {
  const definition = JSON.parse(readFileSync(join(demoProject, 'Seeding', 'Forms', 'contact.json'), 'utf8')) as {
    pages: { groups: { fields: { key: string; alias: string }[] }[] }[];
  };
  return Object.fromEntries(
    definition.pages.flatMap((page) =>
      page.groups.flatMap((group) => group.fields.map((field) => [field.alias, field.key])),
    ),
  );
}

/** Each entry of the contact form, oldest first, with its stored texts by field key. */
async function entriesOf(request: APIRequestContext): Promise<{ field: string; text: string | null }[][]> {
  const response = await request.get('/jfb-testing/entries', { params: { form: 'contact' } });
  expect(response.status()).toBe(200);
  return ((await response.json()) as { entries: { values: { field: string; text: string | null }[] }[] }).entries.map(
    (entry) => entry.values,
  );
}

/** Console errors and uncaught exceptions on a page. */
function errorsOn(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

test("a signed-in member's form starts with their name, leaves out the email address their own stands in for, and stores it", async ({
  page,
  memberOf,
}) => {
  const member = memberOf(0);
  await signInMember(page, member.email, member.password);
  const errors = errorsOn(page);
  const path = contactPath();
  const served = await (await page.request.get(path + '?source=Newsletter')).text();
  expect(served, 'the server writes no query value').not.toContain('Newsletter');
  expect(served, "and never the member's address").not.toContain(member.email);

  await page.goto(path + '?source=Newsletter');
  const form = formOf(page);
  await expect(form.locator('[name="name"]'), "the member's name, theirs to change").toHaveValue(member.name);
  await expect(form.locator('[data-jfb-alias="email"]'), 'no email field: their own address stands in').toHaveCount(0);
  await expect(form.locator('select[name="topic"]'), "the contact page's topic").toHaveValue('general');
  await expect(form.locator('[name="source"]'), 'the query value the form allows').toHaveValue('Newsletter');
  await axe(page, "the member's contact form, prefilled", true);

  const keys = keysOfContact();
  const before = (await entriesOf(page.request)).length;
  const message = `Prefill check ${Date.now().toString(36)}: when does the reading room open?`;
  await fillForm(form, { message });
  const sent = await submitForm(form);
  expect(sent.state, 'sent without an email field').toBe('sent');
  const own = (await entriesOf(page.request))
    .slice(before)
    .filter((entry) => entry.some((value) => value.field === keys['message'] && value.text === message));
  expect(own).toHaveLength(1);
  expect(own[0]!.find((value) => value.field === keys['email'])?.text, "the member's own address").toBe(member.email);
  expect(own[0]!.find((value) => value.field === keys['source'])?.text).toBe('Newsletter');
  expect(errors, 'console errors').toEqual([]);
});

test('a visitor answers the email address, finds the topic chosen, and only an allowed query value prefills', async ({
  page,
}) => {
  const errors = errorsOn(page);
  const path = contactPath();
  await page.goto(path + '?heard=Poster');
  const form = formOf(page);
  await expect(form.locator('[data-jfb-default]'), 'the runtime filled what the server marked').toHaveCount(0);
  await expect(form.locator('[name="source"]'), 'a name the form does not allow is left out').toHaveValue('');
  await expect(form.locator('[name="email"]'), 'a visitor who is not signed in answers it').toHaveValue('');
  await expect(form.locator('[name="name"]')).toHaveValue('');
  await expect(form.locator('select[name="topic"]')).toHaveValue('general');
  await axe(page, "a visitor's contact form", true);

  await page.goto(path + '?source=Poster&heard=Radio');
  await expect(form.locator('[name="source"]')).toHaveValue('Poster');
  await form.locator('select[name="topic"]').selectOption('booking');
  await expect(form.locator('select[name="topic"]'), 'the visitor changes it as any answer').toHaveValue('booking');
  expect(errors, 'console errors').toEqual([]);
});
