// A form published again while its visitor filled it in (ROADMAP 6.4j, ADR-0004): the form as it is now comes in the old
// one's place with the answers, the page's values and the same id, a consent with new wording stays unticked, the notice
// says so and takes the focus; what cannot be carried over goes the way without JavaScript.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { copyAnswers } from './changed';
import { engineOf, loadFormulas, loadTexts } from './engine';
import { pageErrorsOf } from './errors';
import { wordsFor } from './messages';
import type { FormModel, ModelField } from './model';
import { senderOf, type SendDetail } from './send';
import { stubMembers } from './testing-cover';
import { ENGLISH } from './testing';
import type { PageUploads, UploadedFile } from './uploads';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);
const ID = '00112233-4455-4677-8899-aabbccddeeff';
const NAME: ModelField = { key: 'k-name', type: 'text', alias: 'name', settings: {}, label: 'Name' };

const modelOf = (revision: number, settings: Record<string, unknown> = {}): FormModel => ({
  alias: 'contact',
  revision,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', settings, pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [NAME] }] }] },
  fields: { 'k-name': { kind: 'text', canBeRequired: true, builtIn: true } },
  consents: {},
  ...ENGLISH,
});

const templates = (changed = true): string =>
  '<template data-jfb-templates data-jfb-sending="Sending your answers" data-jfb-thanks="Thank you.">' +
  '<p class="jfb-error" data-jfb-error></p>' +
  (changed
    ? '<div class="jfb-notice" data-jfb-notice="form_changed" role="alert" tabindex="-1" aria-labelledby="n"><h2 id="n">Updated</h2>' +
      '<p data-jfb-one="An agreement is new." data-jfb-other="Some agreements are new.">Your answers are kept.</p></div>'
    : '') +
  '<div data-jfb-notice="server_error" role="alert" tabindex="-1"></div>' +
  '<div class="jfb-outcome" data-jfb-outcome tabindex="-1"></div></template>';

interface Shape {
  readonly revision: number;
  readonly wording?: string;
  readonly more?: string;
  readonly changed?: boolean;
  readonly attributes?: string;
  readonly context?: string;
  /** The form's settings, as a summary page's. */
  readonly settings?: Record<string, unknown>;
  /** Where the form is by the time it wakes, as in the headless element. */
  readonly place?: (form: HTMLFormElement) => void;
}

const markup = ({
  revision,
  wording = 'one',
  more = '',
  changed = true,
  attributes = '',
  context = '',
}: Shape): string =>
  `<form id="jfb-1" class="jfb-form jfb-theme-acme" data-jfb-form="f" data-jfb-revision="${String(revision)}" data-jfb-contract="1" ` +
  `data-jfb-api="/jfb/api/v1" action="/contact/#jfb-1" lang="en-US" dir="ltr" ${attributes}>` +
  `<script type="application/json" data-jfb-model>${JSON.stringify(modelOf(revision))}</script>${templates(changed)}` +
  '<section data-jfb-page="p1"><div data-jfb-group="g1">' +
  '<div data-jfb-field="k-name" data-jfb-alias="name" data-jfb-type="text"><label for="n1">Name</label><input id="n1" name="name"></div>' +
  '<input type="text" name="note" id="note"><textarea name="long" id="long"></textarea>' +
  '<select name="pick" id="pick"><option value=""></option><option value="a">A</option><option value="b">B</option></select>' +
  '<select name="many" id="many" multiple><option value="x">X</option><option value="y">Y</option><option value="z">Z</option></select>' +
  '<input type="radio" name="way" id="w1" value="post"><input type="radio" name="way" id="w2" value="mail">' +
  '<input type="checkbox" name="days" id="d1" value="sat"><input type="checkbox" name="days" id="d2" value="sun">' +
  `<input type="checkbox" name="agree" id="agree" value="true"><input type="hidden" name="jfb-consent-agree" value="${wording}">` +
  `${more}</div></section>` +
  '<div class="jfb-navigation"><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button></div>' +
  `${context}<div class="jfb-hp" aria-hidden="true"><input name="jfb-hp-1" id="hp"></div><div data-jfb-live aria-live="polite"></div></form>`;

const input = (id: string): HTMLInputElement => document.getElementById(id) as HTMLInputElement;

function fillIn(): void {
  input('n1').value = 'Ana';
  input('note').value = 'A note';
  input('long').value = 'A long text';
  input('pick').value = 'b';
  for (const option of (document.getElementById('many') as HTMLSelectElement).options) {
    option.selected = option.value !== 'y';
  }
  input('w2').checked = true;
  input('d2').checked = true;
  input('agree').checked = true;
  input('hp').value = 'kept out';
}

interface Sent {
  readonly url: string;
  readonly body: Record<string, unknown>;
}

function serve(
  render: string | Response,
  entries: (count: number) => Response = () => json(409, { code: 'form_changed' }),
) {
  const sent: Sent[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      sent.push({ url, body: init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : {} });
      if (url.includes('/render')) return typeof render === 'string' ? new Response(render) : render;
      if (url.endsWith('/challenge')) return json(200, { challenge: 'c1' });
      return entries(sent.filter((each) => each.url.endsWith('/entries')).length);
    }),
  );
  return {
    sent,
    entries: (): Sent[] => sent.filter((each) => each.url.endsWith('/entries')),
    renders: (): Sent[] => sent.filter((each) => each.url.includes('/render')),
    challenges: (): Sent[] => sent.filter((each) => each.url.endsWith('/challenge')),
  };
}

const json = (status: number, body: unknown): Response => new Response(JSON.stringify(body), { status });

function wake(shape: Shape = { revision: 1 }, files?: PageUploads) {
  document.body.innerHTML = markup(shape);
  const form = document.querySelector('form') as HTMLFormElement;
  const posted: string[] = [];
  stubMembers(form, {
    requestSubmit: (button?: HTMLElement | null) => {
      posted.push(button instanceof HTMLButtonElement ? button.value : '');
    },
  });
  const heard: SendDetail[] = [];
  document.addEventListener('jfb:error', (event) => heard.push((event as CustomEvent<SendDetail>).detail), {
    once: true,
  });
  const rewoken: {
    form: HTMLFormElement;
    model: FormModel;
    carried: { id: string; challenge: string | null; files?: ReadonlyMap<string, readonly UploadedFile[]> };
  }[] = [];
  const wakeForm = (
    each: HTMLFormElement,
    model: FormModel,
    carried?: { id: string; challenge: string | null },
  ): void => {
    stubMembers(each, {
      requestSubmit: (button?: HTMLElement | null) => {
        posted.push(button instanceof HTMLButtonElement ? button.value : '');
      },
    });
    const deliver = senderOf(each, model, {
      ...(carried === undefined ? { id: ID } : { id: carried.id, challenge: carried.challenge }),
      ...(files === undefined ? {} : { files }),
      go: () => undefined,
      rewake: (next, nextModel, carry) => {
        rewoken.push({ form: next, model: nextModel, carried: carry });
        wakeForm(next, nextModel, carry);
      },
    });
    pageErrorsOf(each, model, engineOf(model, null), () => NOW, deliver);
  };
  shape.place?.(form);
  wakeForm(form, modelOf(shape.revision, shape.settings));
  input('n1').value = 'Ana';
  const press = (): void => {
    const button = document.getElementById('send');
    document
      .querySelector('form')
      ?.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button }));
  };
  return { form, posted, heard, rewoken, press };
}

const until = (check: () => void): Promise<void> => vi.waitFor(check, { timeout: 4000, interval: 2 });

beforeAll(async () => {
  await Promise.all([loadFormulas(), loadTexts(), wordsFor('en-US')]);
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('a form published again meanwhile', () => {
  it("comes in the old one's place with every answer, asked for as the page shows it", async () => {
    const server = serve(markup({ revision: 2 }));
    const { form, press, posted, heard } = wake({
      revision: 1,
      attributes: 'data-jfb-content="page-key" data-jfb-placement="sealed"',
    });
    fillIn();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const query = new URL(server.renders()[0]?.url ?? '', 'http://x').searchParams;
    expect(server.renders()[0]?.url).toContain('/jfb/api/v1/forms/f/render?');
    expect(Object.fromEntries(query)).toEqual({
      instance: 'jfb-1',
      page: document.location.pathname,
      culture: 'en-US',
      contentKey: 'page-key',
      theme: 'acme',
      placement: 'sealed',
    });
    const next = document.querySelector('form') as HTMLFormElement;
    expect(next.dataset['jfbRevision']).toBe('2');
    expect(next.hasAttribute('data-jfb-woken')).toBe(true);
    expect(input('n1').value).toBe('Ana');
    expect(input('note').value).toBe('A note');
    expect(input('long').value).toBe('A long text');
    expect(input('pick').value).toBe('b');
    expect([...(document.getElementById('many') as HTMLSelectElement).selectedOptions].map((o) => o.value)).toEqual([
      'x',
      'z',
    ]);
    expect([input('w1').checked, input('w2').checked]).toEqual([false, true]);
    expect([input('d1').checked, input('d2').checked]).toEqual([false, true]);
    expect(input('agree').checked, 'the same wording stays ticked').toBe(true);
    expect(input('hp').value, 'the honeypot starts over').toBe('');
    expect(posted).toEqual([]);
    expect(heard).toEqual([{ key: 'f', alias: 'contact', code: 'form_changed' }]);
  });

  it('moves the focus to the notice, above the pages, which says the answers are kept', async () => {
    serve(markup({ revision: 2 }));
    const { form, press } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const notice = document.querySelector('form > [data-jfb-notice="form_changed"]');
    expect(document.activeElement).toBe(notice);
    expect(notice?.textContent).toBe('UpdatedYour answers are kept.');
    expect(notice?.nextElementSibling?.hasAttribute('data-jfb-page')).toBe(true);
  });

  it('leaves a consent with new wording unticked, and says so', async () => {
    serve(markup({ revision: 2, wording: 'two' }));
    const { form, press } = wake();
    fillIn();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(input('agree').checked).toBe(false);
    expect(document.querySelector('[data-jfb-notice] p')?.textContent).toBe(
      'Your answers are kept. An agreement is new.',
    );
  });

  it('says nothing of a consent with new wording that was not ticked', async () => {
    serve(markup({ revision: 2, wording: 'two' }));
    const { form, press } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(document.querySelector('[data-jfb-notice] p')?.textContent).toBe('Your answers are kept.');
  });

  it('wakes again with the same id and the same challenge, so a send from it is a retry of neither', async () => {
    const server = serve(markup({ revision: 2 }), (count) =>
      count === 1 ? json(409, { code: 'form_changed' }) : json(200, { receipt: {}, outcome: { kind: 'message' } }),
    );
    const { form, press, rewoken } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));
    await until(() => expect(rewoken).toHaveLength(1));
    expect(rewoken[0]?.carried).toEqual({ id: ID, challenge: 'c1' });
    expect(rewoken[0]?.model.revision).toBe(2);

    press();
    await until(() => expect(server.entries()).toHaveLength(2));
    expect(server.entries()[1]?.body).toMatchObject({ submissionId: ID, revision: 2, challenge: 'c1' });
    expect(server.challenges(), 'the challenge is not asked for again').toHaveLength(1);
    await until(() => expect(document.querySelector('[data-jfb-outcome]')).not.toBeNull());
  });

  it('carries the files its server holds to the form in its place', async () => {
    serve(markup({ revision: 2 }), () => json(409, { code: 'form_changed' }));
    const held = new Map([['photo', [{ token: 't1', name: 'a.png', size: 3 }]]]);
    const { press, rewoken } = wake({ revision: 1 }, { settled: () => Promise.resolve(true), uploaded: () => held });
    press();
    await until(() => expect(rewoken).toHaveLength(1));

    expect(rewoken[0]?.carried).toMatchObject({ id: ID, challenge: 'c1', files: held });
  });

  it('keeps what the page gave its form, which a render from elsewhere does not have', async () => {
    serve(markup({ revision: 2, context: '<input type="hidden" name="jfb-context-other" value="render">' }));
    const { form, press } = wake({
      revision: 1,
      context:
        '<input type="hidden" name="jfb-context-campaign" value="spring"><input type="hidden" name="jfb-query-from" value="mail">',
    });
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const hidden = (name: string): string | null | undefined =>
      document.querySelector(`input[name="${name}"]`)?.getAttribute('value');
    expect(hidden('jfb-context-campaign')).toBe('spring');
    expect(hidden('jfb-query-from')).toBe('mail');
    expect(document.querySelector('input[name="jfb-context-other"]')).toBeNull();
  });
});

describe('a form of several pages', () => {
  const OLD = '</div></section><section data-jfb-page="p2" hidden><div>';
  const withMoves = (form: HTMLFormElement): void => {
    form
      .querySelector<HTMLTemplateElement>('template[data-jfb-templates]')
      ?.content.append(
        Object.assign(document.createElement('div'), { innerHTML: '<button data-jfb-nav="next"></button>' }),
      );
  };

  it('whose view writes no buttons to move with goes by post, asking for nothing', async () => {
    const server = serve(markup({ revision: 2, more: OLD }));
    const { press, posted } = wake({ revision: 1, more: OLD });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.renders()).toEqual([]);
  });

  it('with a summary page goes by post, as only a post writes its rows', async () => {
    const server = serve(markup({ revision: 2, more: OLD }));
    const { form, press, posted } = wake({ revision: 1, more: OLD, settings: { summaryPage: true } });
    withMoves(form);
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.renders()).toEqual([]);
  });

  it("comes in the old one's place on the page the visitor was on", async () => {
    const pages = '</div></section><section data-jfb-page="p2" hidden><div>';
    const server = serve(markup({ revision: 2, more: pages }));
    const { form, press } = wake({ revision: 1, more: pages });
    withMoves(form);
    form.querySelector<HTMLElement>('[data-jfb-page="p1"]')?.toggleAttribute('hidden', true);
    form.querySelector<HTMLElement>('[data-jfb-page="p2"]')?.toggleAttribute('hidden', false);
    press();
    await until(() => expect(server.renders()).toHaveLength(1));
    await until(() => expect(document.querySelector('[data-jfb-notice="form_changed"]')).not.toBeNull());

    expect(document.querySelector('[data-jfb-page="p1"]')?.hasAttribute('hidden')).toBe(true);
    expect(document.querySelector('[data-jfb-page="p2"]')?.hasAttribute('hidden')).toBe(false);
  });

  it('stays on a page its render hides, as that render knows no answers, for the rules to decide', async () => {
    const pages = '</div></section><section data-jfb-page="p2" hidden data-jfb-hidden><div>';
    const server = serve(markup({ revision: 2, more: pages }));
    const { form, press } = wake({ revision: 1, more: OLD });
    withMoves(form);
    form.querySelector<HTMLElement>('[data-jfb-page="p1"]')?.toggleAttribute('hidden', true);
    form.querySelector<HTMLElement>('[data-jfb-page="p2"]')?.toggleAttribute('hidden', false);
    press();
    await until(() => expect(server.renders()).toHaveLength(1));
    await until(() => expect(document.querySelector('[data-jfb-notice="form_changed"]')).not.toBeNull());

    expect(document.querySelector('[data-jfb-page="p2"]')?.hasAttribute('hidden')).toBe(false);
  });
});

describe('a form in the headless element', () => {
  const inElement = (fresh: () => Promise<HTMLFormElement | null>, more = ''): ReturnType<typeof wake> =>
    wake({
      revision: 1,
      more,
      place: (form) => {
        const host = document.createElement('jfb-form') as HTMLElement & { fresh?: typeof fresh };
        host.fresh = fresh;
        form.replaceWith(host);
        form.removeAttribute('action');
        host.append(form);
      },
    });
  const written = (): HTMLFormElement => {
    const holder = document.createElement('div');
    holder.innerHTML = markup({ revision: 2 });
    const form = holder.querySelector('form') as HTMLFormElement;
    form.removeAttribute('action');
    return form;
  };

  it('comes from the element, with the answers kept and the notice, asking the endpoint for no render', async () => {
    const server = serve(markup({ revision: 2 }));
    const fresh = vi.fn(() => Promise.resolve(written()));
    const { press } = inElement(fresh);
    input('n1').value = 'Ana';
    press();
    await until(() => expect(document.querySelector('[data-jfb-notice="form_changed"]')).not.toBeNull());

    expect(fresh).toHaveBeenCalledOnce();
    expect(server.renders()).toEqual([]);
    expect(input('n1').value).toBe('Ana');
    expect(document.querySelector('jfb-form form')?.getAttribute('data-jfb-revision')).toBe('2');
    expect(document.activeElement).toBe(document.querySelector('[data-jfb-notice="form_changed"]'));
  });

  it('comes on the page the visitor was on with a summary page, which the runtime writes where no post can', async () => {
    const pages = '</div></section><section data-jfb-page="p2" hidden><div>';
    const holder = document.createElement('div');
    holder.innerHTML = markup({ revision: 2, more: pages });
    const next = holder.querySelector('form') as HTMLFormElement;
    next.removeAttribute('action');
    serve(markup({ revision: 2 }));
    const made = wake({
      revision: 1,
      more: pages,
      settings: { summaryPage: true },
      place: (form) => {
        const host = document.createElement('jfb-form') as HTMLElement & { fresh?: () => Promise<HTMLFormElement> };
        host.fresh = () => Promise.resolve(next);
        form.replaceWith(host);
        form.removeAttribute('action');
        host.append(form);
        form
          .querySelector<HTMLTemplateElement>('template[data-jfb-templates]')
          ?.content.append(
            Object.assign(document.createElement('div'), { innerHTML: '<button data-jfb-nav="next"></button>' }),
          );
      },
    });
    made.form.querySelector<HTMLElement>('[data-jfb-page="p1"]')?.toggleAttribute('hidden', true);
    made.form.querySelector<HTMLElement>('[data-jfb-page="p2"]')?.toggleAttribute('hidden', false);
    made.press();
    await until(() => expect(next.isConnected).toBe(true));

    expect(next.querySelector('[data-jfb-page="p1"]')?.hasAttribute('hidden')).toBe(true);
    expect(next.querySelector('[data-jfb-page="p2"]')?.hasAttribute('hidden')).toBe(false);
    expect(made.posted).toEqual([]);
  });

  it('shows the notice of a failed send for a form with rows, which it cannot carry over', async () => {
    serve(markup({ revision: 2 }));
    const fresh = vi.fn(() => Promise.resolve(written()));
    const { press, form, posted } = inElement(fresh, '<div class="jfb-repeater"></div>');
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="server_error"]')).not.toBeNull());

    expect(fresh).not.toHaveBeenCalled();
    expect(posted).toEqual([]);
  });

  it('shows the notice of a failed send when the element has none to give', async () => {
    serve(markup({ revision: 2 }));
    const { press, form, posted } = inElement(() => Promise.resolve(null));
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="server_error"]')).not.toBeNull());

    expect(posted).toEqual([]);
  });
});

describe('what goes the way without JavaScript instead', () => {
  it.each([
    ['a form with rows', { more: '<div class="jfb-repeater"></div>' }],
    ['a form with a ranking', { more: '<ol class="jfb-ranking"></ol>' }],
  ])('%s, asking for nothing', async (_name, shape) => {
    const server = serve(markup({ revision: 2 }));
    const { press, posted } = wake({ revision: 1, ...shape });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.renders()).toEqual([]);
  });

  it.each([
    ['a render that fails', () => new Response(markup({ revision: 2 }), { status: 500 })],
    [
      'a form that takes no entry now',
      () => new Response('<div class="jfb-form" data-jfb-form="f"><div class="jfb-notice">Closed</div></div>'),
    ],
    [
      'a render with model data it cannot read',
      () => new Response(markup({ revision: 2 }).replace(/data-jfb-model>[^<]*</, 'data-jfb-model>nope<')),
    ],
    ["a theme's view that writes no notice", () => new Response(markup({ revision: 2, changed: false }))],
    [
      'a render of another markup contract',
      () => new Response(markup({ revision: 2 }).replace('data-jfb-contract="1"', 'data-jfb-contract="2"')),
    ],
  ])('%s, keeping the old form until then', async (_name, render) => {
    serve(render());
    const { form, press, posted } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(form.isConnected).toBe(true);
  });

  it('asks for nothing on a form with no address to post to, which shows the notice of a failed send', async () => {
    const server = serve(markup({ revision: 2 }));
    document.body.innerHTML = markup({ revision: 1 }).replace('action="/contact/#jfb-1"', '');
    const form = document.querySelector('form') as HTMLFormElement;
    const posted: string[] = [];
    stubMembers(form, { requestSubmit: () => posted.push('submit') });
    pageErrorsOf(form, modelOf(1), engineOf(modelOf(1), null), () => NOW, senderOf(form, modelOf(1), { id: ID }));
    input('n1').value = 'Ana';
    form.dispatchEvent(
      new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: document.getElementById('send') }),
    );
    await until(() => expect(form.querySelector('[data-jfb-notice="server_error"]')).not.toBeNull());

    expect(server.renders()).toEqual([]);
    expect(posted).toEqual([]);
  });
});

describe('the frame', () => {
  it("leaves alone a form that came in another's place", async () => {
    const { createFrame } = await import('./frame');
    document.body.innerHTML = markup({ revision: 2, attributes: 'data-jfb-woken' });
    const load = vi.fn(() => Promise.resolve({ attach: () => undefined }));
    const form = document.querySelector('form') as HTMLFormElement;
    const ready = vi.fn();
    form.addEventListener('jfb:ready', ready);

    createFrame(load).init();
    form.dispatchEvent(new Event('focusin', { bubbles: true }));

    expect(ready).not.toHaveBeenCalled();
    expect(load).not.toHaveBeenCalled();
  });
});

describe('copying the answers', () => {
  const twoForms = (one: string, two: string): [HTMLFormElement, HTMLFormElement] => {
    document.body.innerHTML = `<form id="a">${one}</form><form id="b">${two}</form>`;
    return [document.getElementById('a') as HTMLFormElement, document.getElementById('b') as HTMLFormElement];
  };

  it('fills several controls of one name in their order', () => {
    const [a, b] = twoForms(
      '<input name="x" value="1"><input name="x" value="2">',
      '<input name="x"><input name="x"><input name="x">',
    );

    copyAnswers(a, b);

    expect([...b.querySelectorAll('input')].map((each) => each.value)).toEqual(['1', '2', '']);
  });

  it('leaves out an answer whose question is gone, and a new question empty', () => {
    const [a, b] = twoForms('<input name="gone" value="1">', '<input name="fresh">');

    copyAnswers(a, b);

    expect(b.querySelector('input')?.value).toBe('');
    expect(b.querySelector('[name="gone"]')).toBeNull();
  });

  it("keeps the visitor's answer over a default the new form would fill in", () => {
    const [a, b] = twoForms(
      '<input name="x" value="mine">',
      '<div data-jfb-default><input name="x"></div><div data-jfb-default><input name="y"></div>',
    );

    copyAnswers(a, b);

    expect(b.querySelector('[name="x"]')?.closest('[data-jfb-default]')).toBeNull();
    expect(b.querySelector('[name="y"]')?.closest('[data-jfb-default]')).not.toBeNull();
  });

  it('counts each ticked consent with new wording', () => {
    const [a, b] = twoForms(
      '<input type="checkbox" name="p" value="true" checked><input type="hidden" name="jfb-consent-p" value="1">' +
        '<input type="checkbox" name="q" value="true" checked><input type="hidden" name="jfb-consent-q" value="1">',
      '<input type="checkbox" name="p" value="true"><input type="hidden" name="jfb-consent-p" value="2">' +
        '<input type="checkbox" name="q" value="true"><input type="hidden" name="jfb-consent-q" value="2">',
    );

    expect(copyAnswers(a, b)).toBe(2);
  });

  it('copies no file and no button', () => {
    const [a, b] = twoForms(
      '<button name="n" value="old"></button><input type="submit" name="s" value="old"><input type="hidden" name="h" value="old">',
      '<input type="submit" name="s" value="new"><input type="hidden" name="h" value="new">',
    );

    copyAnswers(a, b);

    expect(b.querySelector<HTMLInputElement>('[name="s"]')?.value).toBe('new');
    expect(b.querySelector<HTMLInputElement>('[name="h"]')?.value).toBe('new');
  });
});
