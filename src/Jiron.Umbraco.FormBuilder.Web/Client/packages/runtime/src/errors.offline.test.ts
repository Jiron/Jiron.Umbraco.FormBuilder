// When the page's words cannot load (ROADMAP 6.4e, ADR-0113): a send goes on as without JavaScript and the server
// checks it, and leaving a field shows nothing, so a visitor is never stopped by what the browser cannot say.
import { describe, expect, it, vi } from 'vitest';
import { engineOf } from './engine';
import { pageErrorsOf } from './errors';
import type { FormModel } from './model';
import { ENGLISH } from './testing';

vi.mock('./messages', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./messages')>()),
  wordsFor: () => Promise.reject(new Error('The language file did not load.')),
}));

const model: FormModel = {
  alias: 'contact',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: {
    key: 'f',
    pages: [
      { key: 'p1', groups: [{ key: 'g1', fields: [{ key: 'k-name', type: 'text', alias: 'name', required: true }] }] },
    ],
  },
  fields: { 'k-name': { kind: 'text', canBeRequired: true, builtIn: true } },
  consents: {},
  ...ENGLISH,
};

describe('a page whose words do not load', () => {
  it('sends as without JavaScript and shows no error', async () => {
    document.body.innerHTML =
      '<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">' +
      '<div data-jfb-field="k-name" data-jfb-alias="name"><input id="jfb-1-name" name="name"></div></div></section>' +
      '<input name="note" value="kept"><button type="submit" name="jfb-nav" value="submit" id="send">Send</button></form>';
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const sent: string[] = [];
    form.requestSubmit = (button?: HTMLElement | null) => {
      sent.push(button instanceof HTMLButtonElement ? button.value : '');
    };
    pageErrorsOf(form, model, engineOf(model, null));
    const name = document.getElementById('jfb-1-name');
    name?.dispatchEvent(new Event('input', { bubbles: true }));
    name?.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    form.dispatchEvent(
      new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: document.getElementById('send') }),
    );
    await vi.waitFor(() => expect(sent).toEqual(['submit']));
    expect(form.querySelectorAll('[data-jfb-error], [data-jfb-summary]')).toHaveLength(0);
  });
});
