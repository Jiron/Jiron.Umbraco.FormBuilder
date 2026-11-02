// What a slow device shows while it solves a form's puzzle (ROADMAP 7.2c, ADR-0157): a hint once the puzzle takes its time, in
// the form's live region, which a quick answer never shows and which the send's own words replace.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { engineOf, loadFormulas, loadTexts } from './engine';
import { pageErrorsOf } from './errors';
import { wordsFor } from './messages';
import type { FormModel } from './model';
import { senderOf } from './send';
import { ENGLISH } from './testing';

let answer: (value: string) => void = () => undefined;
vi.mock('./protect-chunk', () => ({
  solve: () =>
    new Promise<string>((resolve) => {
      answer = resolve;
    }),
}));

const MODEL: FormModel = {
  alias: 'contact',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: {
    key: 'f',
    pages: [
      { key: 'p1', groups: [{ key: 'g1', fields: [{ key: 'k-name', type: 'text', alias: 'name', settings: {} }] }] },
    ],
  },
  fields: { 'k-name': { kind: 'text', canBeRequired: true, builtIn: true } },
  consents: {},
  ...ENGLISH,
};

const wake = (): { form: HTMLFormElement; live: HTMLElement; press: () => void } => {
  document.body.innerHTML =
    '<form id="jfb-1" data-jfb-form="f" data-jfb-api="/jfb/api/v1" data-jfb-contract="1" action="/c/#jfb-1">' +
    '<template data-jfb-templates data-jfb-sending="Sending" data-jfb-thanks="Thanks"><p data-jfb-error></p><div data-jfb-outcome tabindex="-1"></div></template>' +
    '<section data-jfb-page="p1"><div data-jfb-group="g1"><div data-jfb-field="k-name" data-jfb-alias="name" data-jfb-type="text"><label for="n">Name</label><input id="n" name="name" value="Ana"></div></div></section><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button>' +
    '<div data-jfb-live aria-live="polite"></div></form>';
  const form = document.querySelector('form') as HTMLFormElement;
  pageErrorsOf(form, MODEL, engineOf(MODEL, null), Date.now, senderOf(form, MODEL, { go: () => undefined }));
  return {
    form,
    live: form.querySelector('[data-jfb-live]') as HTMLElement,
    press: () => {
      form.dispatchEvent(
        new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: document.getElementById('send') }),
      );
    },
  };
};

beforeAll(async () => {
  await Promise.all([loadFormulas(), loadTexts(), wordsFor('en-US')]);
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

const serve = (): void => {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) =>
      Promise.resolve(
        new Response(
          JSON.stringify(
            url.endsWith('/challenge')
              ? { challenge: 'c1', proofOfWork: { nonce: 'n', bits: 8 } }
              : { receipt: {}, outcome: { kind: 'message', html: '<p>Done</p>' } },
          ),
        ),
      ),
    ),
  );
};

describe('a puzzle that takes its time', () => {
  it('gives the live region back once it is solved, for the send still going', async () => {
    let answerEntries: (response: Response) => void = () => undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) =>
        url.endsWith('/challenge')
          ? Promise.resolve(new Response(JSON.stringify({ challenge: 'c1', proofOfWork: { nonce: 'n', bits: 8 } })))
          : new Promise<Response>((resolve) => {
              answerEntries = resolve;
            }),
      ),
    );
    const { form, live, press } = wake();
    press();
    await vi.waitFor(() => expect(live.textContent).toBe('Sending'));
    await new Promise((resolve) => setTimeout(resolve, 50));
    answer('7');
    await new Promise((resolve) => setTimeout(resolve, 1_700));
    expect(live.textContent, 'no hint for a slow send').toBe('Sending');

    answerEntries(new Response(JSON.stringify({ receipt: {}, outcome: { kind: 'message', html: '<p>Done</p>' } })));
    await vi.waitFor(() => expect(form.isConnected).toBe(false));
  });

  it('says so, and the entry still goes', async () => {
    serve();
    const { form, live, press } = wake();
    press();
    await vi.waitFor(() => expect(live.textContent).toBe('Sending'));
    await vi.waitFor(() => expect(live.textContent).toBe('Checking your browser, which can take a moment.'), {
      timeout: 3_000,
    });

    answer('7');
    await vi.waitFor(() => expect(live.textContent === 'Sending' || !form.isConnected).toBe(true));
    await vi.waitFor(() => expect(form.isConnected).toBe(false));
  });

  it('says what is going on again once it has the answer, while the send goes', async () => {
    let answerEntries: (response: Response) => void = () => undefined;
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) =>
        url.endsWith('/challenge')
          ? Promise.resolve(new Response(JSON.stringify({ challenge: 'c1', proofOfWork: { nonce: 'n', bits: 8 } })))
          : new Promise<Response>((resolve) => {
              answerEntries = resolve;
            }),
      ),
    );
    const { form, live, press } = wake();
    press();
    await vi.waitFor(() => expect(live.textContent).toContain('Checking'), { timeout: 3_000 });
    answer('7');
    await vi.waitFor(() => expect(live.textContent).toBe('Sending'));

    answerEntries(new Response(JSON.stringify({ receipt: {}, outcome: { kind: 'message', html: '<p>Done</p>' } })));
    await vi.waitFor(() => expect(form.isConnected).toBe(false));
  });

  it('says nothing when the answer comes in time', async () => {
    serve();
    const { form, live, press } = wake();
    press();
    await vi.waitFor(() => expect(live.textContent).toBe('Sending'));
    await new Promise((resolve) => setTimeout(resolve, 50));
    answer('7');
    await vi.waitFor(() => expect(form.isConnected).toBe(false));
    await new Promise((resolve) => setTimeout(resolve, 1_700));

    expect(live.textContent).not.toContain('Checking');
  });
});
