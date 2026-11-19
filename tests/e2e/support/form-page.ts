import { expect, type APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { pathOf } from './demo-content.ts';
import { unique } from './forms-api.ts';

// A page of the demo's site of its own for a spec's own form: a page of the events type under the home page, whose type
// picks a form in its `form` property and whose template renders it on the server, so a spec shows a form it made
// without changing the demo's pages, which other specs share.

const MANAGEMENT_API = '/umbraco/management/api/v1';
const HOME_KEY = '7bbb6953-972d-499e-a05a-c7cf1d45b1db';
/** The demo's events page, whose type picks a form in its `form` property. */
const EVENTS_KEY = '8603878d-af82-4867-92ba-ee28f9f12c86';

/** A page under the home page that shows the form, published in English, named from `name`; its path. */
export async function pageWith(api: APIRequestContext, form: string, name = 'Form page'): Promise<string> {
  const model = (await (await api.get(`${MANAGEMENT_API}/document/${EVENTS_KEY}`)).json()) as {
    documentType: { id: string };
    template: { id: string } | null;
  };
  const named = unique(name);
  const key = randomUUID();
  const created = await api.post(`${MANAGEMENT_API}/document`, {
    data: {
      id: key,
      parent: { id: HOME_KEY },
      documentType: { id: model.documentType.id },
      template: model.template,
      values: [{ alias: 'form', value: form, culture: null, segment: null }],
      variants: [
        { culture: 'en-US', segment: null, name: named },
        { culture: 'de-DE', segment: null, name: named },
      ],
    },
  });
  expect(created.status(), await created.text()).toBe(201);
  const published = await api.put(`${MANAGEMENT_API}/document/${key}/publish`, {
    data: { publishSchedules: [{ culture: 'en-US' }] },
  });
  expect(published.ok(), await published.text()).toBe(true);
  return pathOf({ name: named, heading: '' }, 'en');
}
