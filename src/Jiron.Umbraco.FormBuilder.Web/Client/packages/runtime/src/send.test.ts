// Sending on the page (ROADMAP 6.4i, ADR-0120): a send that passes goes to the entries endpoint with its challenge,
// its id and what the page holds; a second press sends nothing; a refused or failed send shows the notice above the
// buttons with every answer kept; the entry's outcome takes the form's place; and whatever the page cannot answer goes
// the way without JavaScript.
import type { ReadPost } from '@jfb/logic';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { engineOf, loadFormulas, loadTexts } from './engine';
import { pageErrorsOf, type InvalidDetail } from './errors';
import { wordsFor } from './messages';
import type { FormModel, ModelField } from './model';
import { bodyOf, senderOf, submissionId, type SendDetail } from './send';
import { ENGLISH } from './testing';
import type { PageUploads } from './uploads';
import { placeOf } from './validation';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);
const ID = '00112233-4455-4677-8899-aabbccddeeff';

const NAME: ModelField = { key: 'k-name', type: 'text', alias: 'name', settings: {}, required: true, label: 'Name' };
const NOTE: ModelField = { key: 'k-note', type: 'text', alias: 'note', settings: {}, label: 'Note' };
const LATER: ModelField = { key: 'k-later', type: 'text', alias: 'later', settings: {}, label: 'Later' };

const MODEL: FormModel = {
  alias: 'contact',
  revision: 7,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: {
    key: 'f',
    settings: {},
    pages: [
      { key: 'p1', groups: [{ key: 'g1', fields: [NAME, NOTE] }] },
      { key: 'p2', groups: [{ key: 'g2', fields: [LATER] }] },
    ],
  },
  fields: Object.fromEntries(
    [NAME, NOTE, LATER].map((each) => [each.key, { kind: 'text', canBeRequired: true, builtIn: true }]),
  ),
  consents: {},
  ...ENGLISH,
};

const notice = (code: string, line: string): string =>
  `<div class="jfb-notice jfb-notice-error" data-jfb-notice="${code}" role="alert" tabindex="-1"` +
  ` aria-labelledby="jfb-1-notice-${code}"><h2 id="jfb-1-notice-${code}">Your answers weren't sent</h2>${line}</div>`;

const SEND_PARTS =
  notice(
    'rate_limited',
    '<p data-jfb-seconds="Wait {0} seconds, then send it again." data-jfb-minutes="Wait {0} minutes, then send it again."></p>',
  ) +
  notice('server_error', '<p>Something went wrong on our side.</p>') +
  notice('timeout', '<p>Check your connection.</p>') +
  '<div class="jfb-outcome" data-jfb-outcome tabindex="-1">' +
  '<p class="jfb-reference" data-jfb-reference>Your reference is <strong></strong></p>' +
  '<p data-jfb-countdown data-jfb-one="Taking you on in {0} second." data-jfb-other="Taking you on in {0} seconds."></p>' +
  '<div class="jfb-navigation"><a href="#" data-jfb-continue>Go now</a>' +
  '<button type="button" data-jfb-stay>Stay here</button></div></div>';

const TEMPLATES =
  '<template data-jfb-templates data-jfb-sending="Sending your answers" data-jfb-thanks="Thank you.">' +
  '<div class="jfb-error-summary" data-jfb-summary role="alert" tabindex="-1" aria-labelledby="s"><h2 id="s"></h2>' +
  '<ul role="list"><li><a href="#"></a></li></ul></div><p class="jfb-error" data-jfb-error></p>' +
  `${SEND_PARTS}</template>`;

interface Sent {
  readonly path: string;
  readonly body: Record<string, unknown>;
}

type Answer = (sent: Sent) => Response | Promise<Response>;

const json = (status: number, body: unknown, headers: Record<string, string> = {}): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });

const STORED = (): Response =>
  json(200, { receipt: { reference: 'REQ-1' }, outcome: { kind: 'message', html: '<h2>Thanks, Ana</h2>' } });

/** The endpoints, which hand out `c1`, `c2`, and so on, unless a test answers otherwise. */
function serve(entries: Answer, challenge?: Answer) {
  const sent: Sent[] = [];
  let issued = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init: RequestInit) => {
      const each: Sent = {
        path: url,
        body: JSON.parse(init.body as string) as Record<string, unknown>,
      };
      sent.push(each);
      if (url.endsWith('/challenge')) {
        issued += 1;
        return challenge === undefined ? json(200, { challenge: `c${String(issued)}` }) : challenge(each);
      }
      return entries(each);
    }),
  );
  return {
    sent,
    entries: (): Sent[] => sent.filter((each) => each.path.endsWith('/entries')),
    challenges: (): Sent[] => sent.filter((each) => each.path.endsWith('/challenge')),
  };
}

interface Options {
  readonly attributes?: string;
  readonly api?: string;
  readonly action?: string;
  readonly templates?: string;
  readonly model?: FormModel;
  readonly files?: PageUploads;
}

function wake({
  attributes = '',
  api = 'data-jfb-api="/jfb/api/v1"',
  action = 'action="/contact/#jfb-1"',
  templates = TEMPLATES,
  model = MODEL,
  files,
}: Options = {}) {
  document.body.innerHTML =
    `<form id="jfb-1" class="jfb-form" lang="en-US" dir="ltr" data-jfb-form="f" data-jfb-contract="1" ${api} ${action} ${attributes}>` +
    templates +
    '<section data-jfb-page="p1"><div data-jfb-group="g1">' +
    '<div class="jfb-field" data-jfb-field="k-name" data-jfb-alias="name" data-jfb-type="text"><label for="n">Name</label><input id="n" name="name"></div>' +
    '<div class="jfb-field" data-jfb-field="k-note" data-jfb-alias="note" data-jfb-type="text"><label for="m">Note</label><input id="m" name="note"></div>' +
    '</div></section>' +
    '<section data-jfb-page="p2" hidden><div data-jfb-group="g2">' +
    '<div class="jfb-field" data-jfb-field="k-later" data-jfb-alias="later" data-jfb-type="text"><label for="l">Later</label><input id="l" name="later"></div>' +
    '</div></section>' +
    '<div class="jfb-navigation"><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button>' +
    '<button type="submit" name="jfb-nav" value="back" data-jfb-nav="back" id="back">Back</button></div>' +
    '<div class="jfb-hp" aria-hidden="true"><label for="h">Leave this field empty</label><input id="h" name="jfb-hp-1"></div>' +
    '<div data-jfb-live aria-live="polite"></div></form>';
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  const posted: string[] = [];
  form.requestSubmit = (button?: HTMLElement | null) => {
    posted.push(button instanceof HTMLButtonElement ? button.value : '');
  };
  const events: { type: string; detail: SendDetail }[] = [];
  for (const type of ['jfb:submitting', 'jfb:submitted', 'jfb:error']) {
    form.addEventListener(type, (event) => events.push({ type, detail: (event as CustomEvent<SendDetail>).detail }));
  }
  const invalid: InvalidDetail[] = [];
  form.addEventListener('jfb:invalid', (event) => invalid.push((event as CustomEvent<InvalidDetail>).detail));
  const gone: string[] = [];
  const deliver = senderOf(form, model, {
    id: ID,
    go: (address) => gone.push(address),
    ...(files === undefined ? {} : { files }),
  });
  pageErrorsOf(form, model, engineOf(model, null), () => NOW, deliver);
  const value = (id: string): HTMLInputElement => document.getElementById(id) as HTMLInputElement;
  value('n').value = 'Ana';
  const press = (): void => {
    const button = document.getElementById('send');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button }));
  };
  return { form, posted, events, invalid, gone, press, value };
}

const until = (check: () => void): Promise<void> => vi.waitFor(check, { timeout: 4000, interval: 2 });

beforeAll(async () => {
  await Promise.all([loadFormulas(), loadTexts(), wordsFor('en-US')]);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('a submission id', () => {
  it("comes from the browser's random numbers as a version 4 UUID, on an origin without crypto.randomUUID", () => {
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array) => bytes.fill(255) });

    expect(submissionId()).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff');
  });

  it('differs from one page to the next', () => {
    expect(submissionId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(submissionId()).not.toBe(submissionId());
  });
});

describe('the body of a send', () => {
  it("holds each answer by its alias, each part as alias.part, a group's rows, and the query and context values", () => {
    const post = {
      fields: new Map([
        ['name', { values: ['Ana'] }],
        ['agree', { values: ['true'], parts: new Map([['wording', ['hash']]]) }],
      ]),
      rows: new Map([
        ['people', [new Map([['who', { values: ['Ben'] }]])]],
        ['none', []],
      ]),
      context: new Map([['campaign', 'spring']]),
    } as unknown as ReadPost;

    expect(
      bodyOf(MODEL, post, { from: 'mail' }, ID, { content: 'page', challenge: 'c', honeypot: 'x', placement: 'p' }),
    ).toEqual({
      submissionId: ID,
      revision: 7,
      culture: 'en-US',
      contentKey: 'page',
      values: {
        name: ['Ana'],
        agree: ['true'],
        'agree.wording': ['hash'],
        people: [{ who: ['Ben'] }],
      },
      query: { from: 'mail' },
      context: { campaign: 'spring' },
      challenge: 'c',
      honeypot: 'x',
      placement: 'p',
    });
  });
});

describe('where a server error is', () => {
  it.each([
    ['name', { field: 'k-name', row: null }],
    ['name.row-1', { field: 'k-name', row: null, gridRow: 'row-1' }],
  ])('reads the path %s', (path, place) => {
    expect(placeOf(MODEL, { code: 'required', path })).toEqual({ code: 'required', ...place });
  });

  it('reads a row of a repeatable group, its check and its rule', () => {
    const child: ModelField = { key: 'k-who', type: 'text', alias: 'who', settings: {} };
    const group: ModelField = { key: 'k-people', type: 'repeater', alias: 'people', settings: {}, fields: [child] };
    const model: FormModel = {
      ...MODEL,
      form: { ...MODEL.form, pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [group] }] }] },
    };

    expect(placeOf(model, { code: 'check', path: 'people[2].who', check: 1, rule: 'r' })).toEqual({
      field: 'k-who',
      row: 2,
      code: 'check',
      check: 1,
      rule: 'r',
    });
  });

  it('is nowhere for a path of no field', () => {
    expect(placeOf(MODEL, { code: 'required', path: 'nobody' })).toBeNull();
  });
});

describe('a send that passes', () => {
  it('goes to the entries endpoint with the challenge the page asked for when it woke', async () => {
    const server = serve(STORED);
    const { posted, press, value } = wake({ attributes: 'data-jfb-content="page-key" data-jfb-placement="sealed"' });
    value('h').value = 'bot';

    expect(server.challenges()).toHaveLength(1);
    expect(server.challenges()[0]?.path).toBe('/jfb/api/v1/forms/f/challenge');
    expect(server.challenges()[0]?.body).toEqual({ contentKey: 'page-key' });
    press();
    await until(() => expect(server.entries()).toHaveLength(1));

    expect(server.entries()[0]?.path).toBe('/jfb/api/v1/forms/f/entries');
    expect(server.entries()[0]?.body).toMatchObject({
      submissionId: ID,
      revision: 7,
      culture: 'en-US',
      contentKey: 'page-key',
      values: { name: ['Ana'] },
      challenge: 'c1',
      honeypot: 'bot',
      placement: 'sealed',
    });
    expect(server.challenges()).toHaveLength(1);
    expect(posted).toEqual([]);
  });

  it('leaves out what the page does not hold', async () => {
    const server = serve(STORED);
    const { press } = wake();
    press();
    await until(() => expect(server.entries()).toHaveLength(1));

    const body = server.entries()[0]?.body ?? {};
    expect(Object.keys(body)).not.toContain('honeypot');
    expect(Object.keys(body)).not.toContain('placement');
    expect(Object.keys(body)).not.toContain('contentKey');
  });

  it('is busy on its way: a second press sends nothing, the button keeps its focus and the live region says so', async () => {
    let answer: (response: Response) => void = () => undefined;
    const server = serve(() => new Promise<Response>((resolve) => (answer = resolve)));
    const { form, press, events } = wake();
    const button = document.getElementById('send') as HTMLButtonElement;
    const live = form.querySelector('[data-jfb-live]');
    button.focus();

    press();
    await until(() => expect(server.entries()).toHaveLength(1));
    expect(button.hasAttribute('data-jfb-busy')).toBe(true);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.disabled).toBe(false);
    expect(document.activeElement).toBe(button);
    expect(live?.textContent).toBe('Sending your answers');
    expect(events.map((each) => each.type)).toEqual(['jfb:submitting']);

    press();
    press();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(server.entries()).toHaveLength(1);

    answer(json(500, {}));
    await until(() => expect(button.hasAttribute('data-jfb-busy')).toBe(false));
    expect(button.hasAttribute('aria-disabled')).toBe(false);
    expect(live?.textContent).toBe('');
  });

  const askedTwice = (): Answer => {
    let asked = 0;
    return () => {
      asked += 1;
      return asked === 1 ? json(503, {}) : json(200, { challenge: 'second' });
    };
  };

  it('goes by post when the first asking failed, as a challenge made now would count as filled in no time', async () => {
    const server = serve(STORED, askedTwice());
    const { press, posted } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.challenges()).toHaveLength(2);
    expect(server.entries()).toEqual([]);
  });

  it('sends with a challenge made at the send on a form with no address to post to', async () => {
    const server = serve(STORED, askedTwice());
    const { press, posted } = wake({ action: '' });
    press();
    await until(() => expect(server.entries()).toHaveLength(1));

    expect(server.entries()[0]?.body['challenge']).toBe('second');
    expect(posted).toEqual([]);
  });

  it('keeps every answer offline, with no challenge at all, as no post would', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new TypeError('offline'))),
    );
    const { form, press, posted } = wake();
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="timeout"]')).not.toBeNull());

    expect(posted).toEqual([]);
  });

  it('holds back any other button while it is on its way, whose post would leave a stored entry behind', async () => {
    let answer: (response: Response) => void = () => undefined;
    const server = serve(() => new Promise<Response>((resolve) => (answer = resolve)));
    const { form, press, posted } = wake();
    press();
    await until(() => expect(server.entries()).toHaveLength(1));

    const back = new SubmitEvent('submit', {
      bubbles: true,
      cancelable: true,
      submitter: document.getElementById('back'),
    });
    form.dispatchEvent(back);

    expect(back.defaultPrevented).toBe(true);
    expect(posted).toEqual([]);
    answer(STORED());
    await until(() => expect(form.isConnected).toBe(false));
  });

  it('renews a challenge that has expired, with the one before it, and sends once more', async () => {
    let tries = 0;
    const server = serve(() => {
      tries += 1;
      return tries === 1 ? json(401, { code: 'challenge_required' }) : STORED();
    });
    const { form, press, posted } = wake({ attributes: 'data-jfb-content="page-key"' });
    press();
    await until(() => expect(server.entries()).toHaveLength(2));

    expect(server.challenges()[1]?.body).toEqual({ previous: 'c1', contentKey: 'page-key' });
    expect(server.entries()[1]?.body).toMatchObject({ challenge: 'c2', submissionId: ID });
    await until(() => expect(form.isConnected).toBe(false));
    expect(posted).toEqual([]);
  });

  it('goes the way without JavaScript when a renewed challenge is refused too', async () => {
    const server = serve(() => json(401, { code: 'challenge_required' }));
    const { press, posted } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.entries()).toHaveLength(2);
  });
});

describe('an entry that is stored', () => {
  it("shows its outcome in the form's place, focused, with the reference and without the countdown", async () => {
    serve(STORED);
    const { form, press, events } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const root = document.getElementById('jfb-1');
    const outcome = root?.querySelector<HTMLElement>('[data-jfb-outcome]');
    expect(root?.localName).toBe('div');
    expect(root?.className).toBe('jfb-form');
    expect(root?.getAttribute('data-jfb-form')).toBe('f');
    expect(root?.getAttribute('lang')).toBe('en-US');
    expect(document.activeElement).toBe(outcome);
    expect(outcome?.querySelector('h2')?.textContent).toBe('Thanks, Ana');
    expect(outcome?.querySelector('strong')?.textContent).toBe('REQ-1');
    expect(outcome?.querySelector('[data-jfb-countdown]')).toBeNull();
    expect(outcome?.querySelector('[data-jfb-continue]')).toBeNull();
    expect(outcome?.querySelector('[data-jfb-stay]')).toBeNull();
    expect(events.map((each) => each.type)).toEqual(['jfb:submitting', 'jfb:submitted']);
    expect(JSON.stringify(events)).not.toContain('Ana');
    expect(events[1]?.detail).toEqual({ key: 'f', alias: 'contact' });
  });

  it("says the page's thank-you for a form with no message, and no reference line without a reference", async () => {
    serve(() => json(200, { receipt: {}, outcome: { kind: 'message' } }));
    const { form, press } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const outcome = document.querySelector('[data-jfb-outcome]');
    expect(outcome?.querySelector('p')?.textContent).toBe('Thank you.');
    expect(outcome?.querySelector('[data-jfb-reference]')).toBeNull();
  });

  it('goes at once to a redirect', async () => {
    serve(() => json(200, { receipt: {}, outcome: { kind: 'redirect', url: '/thanks' } }));
    const { press, gone } = wake();
    press();
    await until(() => expect(gone).toHaveLength(1));

    expect(gone[0]).toBe(new URL('/thanks', document.baseURI).href);
  });

  it('goes nowhere for an address that is not of the web', async () => {
    serve(() => json(200, { receipt: {}, outcome: { kind: 'redirect', url: 'javascript:alert(1)' } }));
    const { form, press, gone } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(gone).toEqual([]);
    expect(document.querySelector('[data-jfb-outcome]')?.textContent).toContain('Thank you.');
  });

  it('counts down to a redirect after a message, each second worded, and goes when it ends', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve(() =>
      json(200, { receipt: {}, outcome: { kind: 'message', html: '<p>Hi</p>', url: '/on', delaySeconds: 12 } }),
    );
    const { form, press, gone } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    const line = document.querySelector('[data-jfb-countdown]');
    expect(line?.textContent).toBe('Taking you on in 12 seconds.');
    expect(document.querySelector('[data-jfb-continue]')?.getAttribute('href')).toBe(
      new URL('/on', document.baseURI).href,
    );
    await vi.advanceTimersByTimeAsync(11_000);
    expect(line?.textContent).toBe('Taking you on in 1 second.');
    expect(gone).toEqual([]);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(gone).toHaveLength(1);
  });

  it('gives a shorter delay ten seconds, the time to reach "Stay here"', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve(() =>
      json(200, { receipt: {}, outcome: { kind: 'message', html: '<p>Hi</p>', url: '/on', delaySeconds: 1 } }),
    );
    const { form, press, gone } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(document.querySelector('[data-jfb-countdown]')?.textContent).toBe('Taking you on in 10 seconds.');
    await vi.advanceTimersByTimeAsync(9_000);
    expect(gone).toEqual([]);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(gone).toHaveLength(1);
  });

  it('waits at "Go now" for a redirect after a message with no delay, as after a post', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve(() =>
      json(200, {
        receipt: { reference: 'REQ-1' },
        outcome: { kind: 'message', html: '<p>Hi</p>', url: '/on', delaySeconds: 0 },
      }),
    );
    const { form, press, gone } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));
    await vi.advanceTimersByTimeAsync(60_000);

    const outcome = document.querySelector('[data-jfb-outcome]');
    expect(outcome?.querySelector('p')?.textContent).toBe('Hi');
    expect(outcome?.querySelector('strong')?.textContent).toBe('REQ-1');
    expect(outcome?.querySelector('[data-jfb-continue]')?.getAttribute('href')).toBe(
      new URL('/on', document.baseURI).href,
    );
    expect(outcome?.querySelector('[data-jfb-countdown]')).toBeNull();
    expect(outcome?.querySelector('[data-jfb-stay]')).toBeNull();
    expect(gone).toEqual([]);
  });

  it('stays for "Stay here", which stops the countdown and leaves the focus on "Go now"', async () => {
    serve(() =>
      json(200, { receipt: {}, outcome: { kind: 'message', html: '<p>Hi</p>', url: '/on', delaySeconds: 1 } }),
    );
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { form, press, gone } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    document.querySelector<HTMLElement>('[data-jfb-stay]')?.click();

    expect(document.querySelector('[data-jfb-countdown]')).toBeNull();
    expect(document.querySelector('[data-jfb-stay]')).toBeNull();
    expect(document.activeElement).toBe(document.querySelector('[data-jfb-continue]'));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(gone).toEqual([]);
  });
});

describe('a send that did not go through', () => {
  it.each([
    ['a 429', () => json(429, { code: 'rate_limited' }, { 'Retry-After': '30' }), 'rate_limited', 'Wait 30 seconds'],
    ['a long 429', () => json(429, {}, { 'Retry-After': '300' }), 'rate_limited', 'Wait 5 minutes'],
    ['a 429 with no time', () => json(429, {}), 'rate_limited', 'Wait 2 minutes'],
    ['a 500', () => json(500, {}), 'server_error', 'went wrong'],
    ['a 503 that is no JSON', () => new Response('down', { status: 503 }), 'server_error', 'went wrong'],
    ['a 504', () => json(504, {}), 'timeout', 'connection'],
    ['the code timeout', () => json(408, { code: 'timeout' }), 'timeout', 'connection'],
    ['no answer', () => Promise.reject(new TypeError('offline')), 'timeout', 'connection'],
  ] as [string, Answer, string, string][])(
    'shows %s above the buttons, focused, with every answer kept',
    async (_name, answer, code, words) => {
      serve(answer);
      const { form, press, posted, events, value } = wake();
      value('m').value = 'A note';
      press();
      await until(() => expect(form.querySelector('[data-jfb-notice]')).not.toBeNull());

      const shown = form.querySelector<HTMLElement>('[data-jfb-notice]');
      expect(shown?.getAttribute('data-jfb-notice')).toBe(code);
      expect(shown?.textContent).toContain(words);
      expect(document.activeElement).toBe(shown);
      expect(shown?.nextElementSibling?.className).toBe('jfb-navigation');
      expect(value('n').value).toBe('Ana');
      expect(value('m').value).toBe('A note');
      expect(posted).toEqual([]);
      expect(events.at(-1)).toEqual({ type: 'jfb:error', detail: { key: 'f', alias: 'contact', code } });
    },
  );

  it('sends again with the same id, and the notice goes', async () => {
    let tries = 0;
    const server = serve(() => {
      tries += 1;
      return tries === 1 ? json(500, {}) : STORED();
    });
    const { form, press } = wake();
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice]')).not.toBeNull());
    press();
    await until(() => expect(server.entries()).toHaveLength(2));

    expect(form.querySelector('[data-jfb-notice]')).toBeNull();
    expect(server.entries()[1]?.body['submissionId']).toBe(ID);
    expect(server.entries()[1]?.body['challenge']).toBe(server.entries()[0]?.body['challenge']);
  });

  it('shows one notice, however many sends failed', async () => {
    let tries = 0;
    const server = serve(() => {
      tries += 1;
      return json(500, {});
    });
    const { form, press } = wake();
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice]')).not.toBeNull());
    press();
    await until(() => expect(server.entries()).toHaveLength(2));
    await until(() => expect(document.activeElement).toBe(form.querySelector('[data-jfb-notice]')));

    expect(form.querySelectorAll('[data-jfb-notice]')).toHaveLength(1);
    expect(tries).toBe(2);
  });

  it("goes the way without JavaScript when the theme's view writes no such notice", async () => {
    serve(() => json(500, {}));
    const { press, posted } = wake({ templates: TEMPLATES.replace('data-jfb-notice="server_error"', '') });
    press();
    await until(() => expect(posted).toEqual(['submit']));
  });
});

describe("the server's errors of a send", () => {
  const refused =
    (...errors: { code: string; path: string }[]): Answer =>
    () =>
      json(422, { code: 'validation_failed', errors });

  it("show as the page's own: at their fields, in the summary, which takes the focus, and in jfb:invalid", async () => {
    serve(refused({ code: 'taken', path: 'note' }));
    const { form, press, posted, invalid } = wake();
    press();
    await until(() => expect(form.querySelector(':scope > [data-jfb-summary]')).not.toBeNull());

    const summary = form.querySelector(':scope > [data-jfb-summary]');
    expect(document.activeElement).toBe(summary);
    expect(summary?.querySelector('a')?.getAttribute('href')).toBe('#jfb-1-note');
    expect(form.querySelector('[data-jfb-field="k-note"] [data-jfb-error]')).not.toBeNull();
    expect(posted).toEqual([]);
    expect(invalid).toEqual([
      { key: 'f', alias: 'contact', scope: 'send', errors: [{ field: 'note', kind: 'taken' }] },
    ]);
  });

  it('go the way without JavaScript when one is on another page', async () => {
    serve(refused({ code: 'required', path: 'later' }));
    const { press, posted, invalid } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(invalid).toEqual([]);
  });

  it('go the way without JavaScript when none is at a field', async () => {
    serve(refused({ code: 'required', path: 'nobody' }));
    const { press, posted } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));
  });
});

describe("an entry already stored under the page's id, with other answers", () => {
  const DUPLICATE = (): Response => json(409, { code: 'duplicate_submission' });

  it("shows that entry's outcome from its receipt, and never posts a second one", async () => {
    const server = serve(DUPLICATE);
    const receipts: Sent[] = [];
    const entries = globalThis.fetch;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init: RequestInit) => {
        if (!url.endsWith('/receipt')) return entries(url, init);
        receipts.push({ path: url, body: JSON.parse(init.body as string) as Record<string, unknown> });
        return json(200, { receipt: { reference: 'REQ-7' }, outcome: { kind: 'message', html: '<h2>Stored</h2>' } });
      }),
    );
    const { form, press, posted, events } = wake({ attributes: 'data-jfb-placement="sealed"' });
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(server.entries()).toHaveLength(1);
    expect(receipts[0]?.body).toEqual({ submissionId: ID, challenge: 'c1', placement: 'sealed' });
    expect(document.querySelector('[data-jfb-outcome] h2')?.textContent).toBe('Stored');
    expect(document.querySelector('[data-jfb-outcome] strong')?.textContent).toBe('REQ-7');
    expect(posted).toEqual([]);
    expect(events.map((each) => each.type)).toEqual(['jfb:submitting', 'jfb:submitted']);
  });

  it('asks with the challenge before a renewal too, which the entry may name', async () => {
    let tries = 0;
    serve(() => {
      tries += 1;
      return tries === 1 ? json(401, { code: 'challenge_required' }) : DUPLICATE();
    });
    const asked: unknown[] = [];
    const entries = globalThis.fetch;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init: RequestInit) => {
        if (!url.endsWith('/receipt')) return entries(url, init);
        const body = JSON.parse(init.body as string) as { challenge: unknown };
        asked.push(body.challenge);
        return body.challenge === 'c2'
          ? json(200, { receipt: {}, outcome: { kind: 'message', html: '<h2>Stored</h2>' } })
          : json(404, { code: 'not_found' });
      }),
    );
    const { form, press } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(asked).toEqual(['c1', 'c2']);
    expect(document.querySelector('[data-jfb-outcome] h2')?.textContent).toBe('Stored');
  });

  it("says the page's thank-you when no receipt comes, and still never posts", async () => {
    serve(DUPLICATE);
    const entries = globalThis.fetch;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init: RequestInit) =>
        url.endsWith('/receipt') ? Promise.reject(new TypeError('offline')) : entries(url, init),
      ),
    );
    const { form, press, posted } = wake();
    press();
    await until(() => expect(form.isConnected).toBe(false));

    expect(document.querySelector('[data-jfb-outcome] p')?.textContent).toBe('Thank you.');
    expect(posted).toEqual([]);
  });
});

describe('a form with no address to post to, as the headless element writes', () => {
  it.each([
    ['form_changed', 409],
    ['form_closed', 403],
    ['invalid_request', 400],
  ])('shows the notice of a failed send for %s', async (code, status) => {
    serve(() => json(status, { code }));
    const { form, press, posted, events } = wake({ action: '' });
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="server_error"]')).not.toBeNull());

    expect(document.activeElement).toBe(form.querySelector('[data-jfb-notice]'));
    expect(posted).toEqual([]);
    expect(events.at(-1)?.type).toBe('jfb:error');
  });

  it('shows it for errors of another page', async () => {
    serve(() => json(422, { code: 'validation_failed', errors: [{ code: 'required', path: 'later' }] }));
    const { form, press, posted } = wake({ action: '' });
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="server_error"]')).not.toBeNull());

    expect(posted).toEqual([]);
  });
});

describe('a form whose files the page takes', () => {
  const STORES: FormModel = {
    ...MODEL,
    fields: { ...MODEL.fields, 'k-note': { kind: 'files', canBeRequired: true, builtIn: true } },
  };
  const uploadsOf = (settled: Promise<boolean>): PageUploads => ({ settled: () => settled, uploaded: () => new Map() });

  it('goes by post with a ranking below a parent, whose options only a post writes anew', async () => {
    const server = serve(STORED);
    const ranking = { key: 'k-rank', type: 'ranking', alias: 'rank', settings: {}, options: { parent: 'k-name' } };
    const { press, posted } = wake({
      model: {
        ...MODEL,
        form: {
          ...MODEL.form,
          pages: MODEL.form.pages.map((page) => ({
            ...page,
            groups: page.groups.map((group) => ({ ...group, fields: [...group.fields, ranking] })),
          })),
        },
      },
    });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.entries()).toEqual([]);
  });

  it('sends on the page, with the tokens its inputs hold, whatever the encoding a post would use', async () => {
    const server = serve(STORED);
    const { press, value, posted } = wake({
      attributes: 'enctype="multipart/form-data"',
      model: STORES,
      files: uploadsOf(Promise.resolve(true)),
    });
    value('m').value = 'token-1';
    press();
    await until(() => expect(server.entries()).toHaveLength(1));

    expect(server.entries()[0]?.body['values']).toMatchObject({ note: ['token-1'] });
    expect(posted).toEqual([]);
  });

  it('waits for what is on its way, busy as ever, and takes a second press for nothing', async () => {
    const server = serve(STORED);
    let finish = (): void => undefined;
    const settled = new Promise<boolean>((resolve) => {
      finish = () => resolve(true);
    });
    const { form, press, value } = wake({ model: STORES, files: uploadsOf(settled) });
    press();
    await until(() => expect(form.querySelector('#send')?.getAttribute('aria-disabled')).toBe('true'));
    press();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(server.entries(), 'nothing goes while a file is on its way').toEqual([]);

    value('m').value = 'token-2';
    finish();
    await until(() => expect(server.entries()).toHaveLength(1));
    expect(server.entries()[0]?.body['values'], 'and it reads the page once they are there').toMatchObject({
      note: ['token-2'],
    });
  });
});

describe('a form with a file that failed, or a signature that could not go', () => {
  const STORES: FormModel = {
    ...MODEL,
    fields: { ...MODEL.fields, 'k-note': { kind: 'files', canBeRequired: true, builtIn: true } },
  };

  it('stops at the file, sends nothing and shows no notice, ready to be pressed again', async () => {
    const server = serve(STORED);
    let answer = false;
    const { form, press, posted } = wake({
      model: STORES,
      files: { settled: () => Promise.resolve(answer), uploaded: () => new Map() },
    });
    press();
    await until(() => expect(form.querySelector('#send')?.hasAttribute('aria-disabled')).toBe(false));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(server.entries()).toEqual([]);
    expect(posted).toEqual([]);
    expect(form.querySelector('[data-jfb-notice]')).toBeNull();

    answer = true;
    press();
    await until(() => expect(server.entries()).toHaveLength(1));
  });

  it('leaves what the file that failed said for a screen reader, and takes away only what it said itself', async () => {
    serve(STORED);
    const { form, press } = wake({
      model: STORES,
      files: {
        settled: () => {
          const live = form.querySelector('[data-jfb-live]');
          if (live !== null) live.textContent = 'plan.pdf: It stopped.';
          return Promise.resolve(false);
        },
        uploaded: () => new Map(),
      },
    });
    press();
    await until(() => expect(form.querySelector('[data-jfb-live]')?.textContent).toBe('plan.pdf: It stopped.'));
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(form.querySelector('[data-jfb-live]')?.textContent).toBe('plan.pdf: It stopped.');
  });

  it.each([
    [429, 'rate_limited', '30'],
    [503, 'server_error', null],
    [0, 'timeout', null],
  ])('a refusal of %i shows the notice %s, and sends nothing', async (status, code, retryAfter) => {
    const server = serve(STORED);
    const { form, press } = wake({
      model: STORES,
      files: { settled: () => Promise.reject({ status, retryAfter }), uploaded: () => new Map() },
    });
    press();
    await until(() => expect(form.querySelector(`[data-jfb-notice="${code}"]`)).not.toBeNull());

    expect(server.entries()).toEqual([]);
  });

  it("words the wait a signature's refusal names", async () => {
    serve(STORED);
    const { form, press } = wake({
      model: STORES,
      files: { settled: () => Promise.reject({ status: 429, retryAfter: '30' }), uploaded: () => new Map() },
    });
    press();
    await until(() =>
      expect(form.querySelector('[data-jfb-notice="rate_limited"] p')?.textContent).toBe(
        'Wait 30 seconds, then send it again.',
      ),
    );
  });

  it('a failure that names no status is a send that got no answer', async () => {
    serve(STORED);
    const { form, press } = wake({
      model: STORES,
      files: { settled: () => Promise.reject(new Error('no picture')), uploaded: () => new Map() },
    });
    press();
    await until(() => expect(form.querySelector('[data-jfb-notice="timeout"]')).not.toBeNull());
  });
});

describe('what the page leaves to the post without JavaScript', () => {
  it.each([
    ['form_changed', 409],
    ['form_closed', 403],
    ['captcha_failed', 400],
    ['invalid_request', 400],
  ])('%s', async (code, status) => {
    const server = serve(() => json(status, { code }));
    const { press, posted, events } = wake();
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.entries()).toHaveLength(1);
    expect(events.map((each) => each.type)).toEqual(['jfb:submitting']);
  });

  it('a form that stores files, which asks for no challenge', async () => {
    const server = serve(STORED);
    const { press, posted } = wake({ attributes: 'enctype="multipart/form-data"' });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.sent).toEqual([]);
  });

  it('a form with a field that stores files, on an element that writes no encoding, whose files would be lost', async () => {
    const server = serve(STORED);
    const model: FormModel = {
      ...MODEL,
      fields: { ...MODEL.fields, 'k-note': { kind: 'files', canBeRequired: true, builtIn: true } },
    };
    const { press, posted } = wake({ model });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.sent).toEqual([]);
  });

  it("a theme's view that writes none of the send's parts", async () => {
    const server = serve(STORED);
    const { press, posted } = wake({ templates: '<template data-jfb-templates></template>' });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.sent).toEqual([]);
  });

  it('a form without the address of the API', async () => {
    const server = serve(STORED);
    const { press, posted } = wake({ api: '' });
    press();
    await until(() => expect(posted).toEqual(['submit']));

    expect(server.sent).toEqual([]);
  });
});
