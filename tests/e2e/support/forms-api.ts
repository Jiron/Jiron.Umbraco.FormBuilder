// Forms the backoffice journeys make through the management API (ROADMAP 9.2, 9.3a): each test makes its own, with a
// name no other test on the shared host uses.
import { expect, type APIRequestContext, type APIResponse } from '@playwright/test';

/** The management API of the section. */
export const API = '/umbraco/jfb/management/api/v1';

/** The built-in contact template (4.5): a name, an email address and a message. */
const CONTACT_TEMPLATE = '66c2bf3e-fe43-41cc-8470-f446c2f01a89';

export const KEY = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A name no other test on the shared host uses. */
export const unique = (name: string) => `${name} ${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

/** The key a creating answer gives, once it says Created. */
export async function created(response: APIResponse, what: string): Promise<string> {
  expect(response.status(), `${what}: ${await response.text()}`).toBe(201);
  const body = (await response.json()) as { key?: string; form?: { key: string } };
  const key = body.key ?? body.form?.key ?? '';
  expect(key, `${what}: its key`).toMatch(KEY);
  return key;
}

/** A form from the built-in contact template. */
export const contactForm = async (api: APIRequestContext, name: string, folderKey: string | null = null) =>
  created(
    await api.post(`${API}/form/from-template`, { data: { templateKey: CONTACT_TEMPLATE, name, folderKey } }),
    `the form ${name}`,
  );

export const blankForm = async (api: APIRequestContext, name: string) =>
  created(await api.post(`${API}/form`, { data: { name } }), `the form ${name}`);

/** A form as the API gives it. */
export const formOf = async (api: APIRequestContext, key: string) =>
  (await (await api.get(`${API}/form/${key}`)).json()) as {
    name: string;
    concurrencyVersion: number;
    folderKey?: string | null;
    isPublished: boolean;
  };

/** Gives the form's first field this label and publishes it, so the field catalogue holds the label; the field's key. */
export async function publishWithLabel(api: APIRequestContext, key: string, label: string): Promise<string> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: { pages: { groups: { fields: { key: string; label: Record<string, string> }[] }[] }[] };
  };
  const field = draft.definition.pages[0]?.groups[0]?.fields[0];
  expect(field, 'a field to label').toBeTruthy();
  (field as { label: Record<string, string> }).label = { '': label };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition: draft.definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  const published = await api.post(`${API}/form/${key}/publish`, {
    data: { concurrencyVersion: (await formOf(api, key)).concurrencyVersion },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return (field as { key: string }).key;
}
