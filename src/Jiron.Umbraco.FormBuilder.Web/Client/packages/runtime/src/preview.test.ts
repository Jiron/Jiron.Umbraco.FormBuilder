// The preview script (ADR-0203, ADR-0204): the document's ticket goes with its requests to the public API, and only
// those; a ticket with little left is renewed by asking the backoffice first, over the port it handed this document, once
// for requests at once, and a request goes with the one held when no answer comes; only the backoffice's own port counts;
// a ticket the backoffice hands over it unasked goes from then on; an upload aborted while it waits goes and is aborted as
// a browser aborts one; and the page turns light or dark. Once the public API took a submission, the backoffice hears
// which; a document of sample answers gives each file field a sample file of a kind it accepts once it has woken.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ASK_WAIT, DARK_CLASS, lend, SAMPLE_WAIT, sampleFile, type PreviewWindow } from './preview';
import { PORT_MESSAGE, RENEW_BEFORE, SCHEME_MESSAGE, SENT_MESSAGE, TICKET_MESSAGE } from './preview-protocol';

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const SITE = 'https://site.test';

interface FakeRequest {
  readyState: number;
  sends: number;
  aborted: boolean;
  readonly headers: Record<string, string>;
  readonly events: string[];
  open(method: string, address: string): void;
  send(body?: unknown): void;
  setRequestHeader(name: string, value: string): void;
  abort(): void;
}

interface Frame {
  readonly win: PreviewWindow;
  readonly sent: { address: string; authorization: string | null; accept: string | null }[];
  readonly Request: new () => FakeRequest;
  readonly parent: object;
  tell(source: unknown, data: unknown, ports?: MessagePort[]): void;
  clock: number;
  /** The window's fetch before the script ran. */
  original?: typeof fetch;
}

const channels: MessageChannel[] = [];

/** What a frame's page holds besides its meta element, how the server answers, and its DataTransfer. */
interface FrameOptions {
  readonly body?: string;
  readonly respond?: (address: string, method: string) => Response | undefined;
  readonly transfer?: typeof DataTransfer;
  /** Runs on the page before the script does. */
  readonly prepare?: (doc: Document) => void;
}

/** A frame with the preview's meta element, or without one; its clock starts at 0. */
function frameWith(
  meta: { content?: string; form?: string; api?: string; lifetime?: number; sample?: boolean } | null = {},
  options: FrameOptions = {},
): Frame {
  const doc = document.implementation.createHTMLDocument('preview');
  const base = doc.createElement('base');
  base.href = `${SITE}/umbraco/section/jfb-forms`;
  doc.head.append(base);
  if (meta !== null) {
    const element = doc.createElement('meta');
    element.name = 'jfb-preview';
    element.content = meta.content ?? 'first-ticket';
    if (meta.form !== '') element.dataset['form'] = meta.form ?? FORM;
    element.dataset['api'] = meta.api ?? '/jfb/api/v1';
    element.dataset['lifetime'] = String(meta.lifetime ?? 600);
    if (meta.sample) element.dataset['sample'] = 'true';
    doc.head.append(element);
  }
  doc.body.innerHTML = options.body ?? '';
  options.prepare?.(doc);
  const sent: Frame['sent'] = [];
  const listeners = new Set<(event: MessageEvent) => void>();
  // A request as the XMLHttpRequest standard has it: abort() before send() changes nothing and says nothing.
  class Request implements FakeRequest {
    static readonly OPENED = 1;
    readyState = 0;
    sends = 0;
    aborted = false;
    readonly headers: Record<string, string> = {};
    readonly events: string[] = [];
    open() {
      this.readyState = 1;
    }
    send() {
      this.sends++;
    }
    setRequestHeader(name: string, value: string) {
      this.headers[name] = value;
    }
    abort() {
      if (this.sends === 0) return;
      this.aborted = true;
      this.readyState = 0;
      this.events.push('abort');
    }
  }
  const parent = {};
  const frame: Frame = {
    sent,
    Request,
    parent,
    clock: 0,
    tell: (source, data, ports = []) => {
      for (const listener of [...listeners]) listener({ source, data, ports } as unknown as MessageEvent);
    },
    win: {
      document: doc,
      parent,
      fetch: (input: RequestInfo | URL, init?: RequestInit) => {
        const headers = new Headers(init?.headers);
        const address = input instanceof globalThis.Request ? input.url : String(input);
        sent.push({ address, authorization: headers.get('Authorization'), accept: headers.get('Accept') });
        const method = init?.method ?? (input instanceof globalThis.Request ? input.method : 'GET');
        return Promise.resolve(options.respond?.(address, method) ?? new Response(null, { status: 204 }));
      },
      XMLHttpRequest: Request as unknown as typeof XMLHttpRequest,
      ...(options.transfer ? { DataTransfer: options.transfer } : {}),
      addEventListener: (_type, listener) => listeners.add(listener),
      setTimeout: (handler, timeout) => setTimeout(handler, timeout) as unknown as number,
      clearTimeout: (id) => clearTimeout(id),
    },
  };
  frame.original = frame.win.fetch;
  lend(frame.win, () => frame.clock);
  return frame;
}

/** Hands the frame its port, as the backoffice does once the document has loaded; the backoffice's end. */
function portOf(frame: Frame): { port: MessagePort; asked: unknown[] } {
  const channel = new MessageChannel();
  channels.push(channel);
  const asked: unknown[] = [];
  channel.port1.onmessage = (event) => asked.push(event.data);
  frame.tell(frame.parent, { jfb: PORT_MESSAGE, form: FORM }, [channel.port2]);
  return { port: channel.port1, asked };
}

const answer = (ticket: string, lifetime = 600) => ({ jfb: TICKET_MESSAGE, form: FORM, ticket, lifetime });

/** The public API's answer to a submission or its receipt: the receipt names the submission. */
const receipt = (submissionId: unknown, status = 200) =>
  new Response(
    JSON.stringify({ receipt: { submissionId, reference: 'R-1', submittedUtc: '2026-11-07T09:00:00Z' }, outcome: {} }),
    {
      status,
      headers: { 'Content-Type': 'application/json' },
    },
  );

/** The value, which the test needs. */
function must<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('missing');
  return value;
}

/** A DataTransfer as a browser's: the files added, as its files. */
class FakeTransfer {
  readonly added: File[] = [];
  readonly items = { add: (file: File) => this.added.push(file) };
  get files(): FileList {
    return this.added as unknown as FileList;
  }
}

/** A file field as the page renders it: its zone, its chooser and the camera's. */
const fileField = (accept: string, id: string) =>
  `<div class="jfb-upload" id="${id}"><input type="file" accept="${accept}"><input type="file" class="jfb-upload-camera" accept="image/*"></div>`;

/** The runtime's word that it took the document's first form on, which bubbles to the document. */
const ready = (frame: Frame) =>
  frame.win.document.querySelector('form')?.dispatchEvent(new CustomEvent('jfb:ready', { bubbles: true }));

/** The files a chooser was given, and the changes it told of. */
function chooserOf(doc: Document, zone: string) {
  const chooser = doc.querySelector<HTMLInputElement>(`#${zone} input:not(.jfb-upload-camera)`);
  if (!chooser) throw new Error(`no chooser in ${zone}`);
  let given: FileList | null = null;
  const changes: Event[] = [];
  Object.defineProperty(chooser, 'files', {
    configurable: true,
    get: () => given,
    set: (files: FileList) => (given = files),
  });
  chooser.addEventListener('change', (event) => changes.push(event));
  return {
    get files() {
      return given as unknown as File[] | null;
    },
    changes,
  };
}

afterEach(() => {
  vi.useRealTimers();
  for (const channel of channels.splice(0)) {
    channel.port1.close();
    channel.port2.close();
  }
});

describe('the preview script', () => {
  it('lends the ticket to the requests to the public API, and to no other', async () => {
    const frame = frameWith();
    expect(frame.win.fetch).not.toBe(frame.original);
    await frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST', headers: { Accept: 'application/json' } });
    await frame.win.fetch(`${SITE}/jfb/api/v1/forms/x/options/y?culture=en`);
    await frame.win.fetch(new globalThis.Request(`${SITE}/jfb/api/v1/forms/x/validate`, { method: 'POST' }));
    await frame.win.fetch('/jfb/assets/runtime/chunks/fields.js');
    await frame.win.fetch('/jfb/api/v10/forms');
    await frame.win.fetch('https://elsewhere.test/jfb/api/v1/forms/x/challenge');
    expect(frame.sent.map((each) => each.authorization)).toEqual([
      'Jfb-Preview first-ticket',
      'Jfb-Preview first-ticket',
      'Jfb-Preview first-ticket',
      null,
      null,
      null,
    ]);
    // What the request said itself stays.
    expect(frame.sent[0]?.accept).toBe('application/json');
  });

  it('lends it to an upload before it is sent; one aborted meanwhile is aborted as a browser aborts it', async () => {
    const frame = frameWith();
    const upload = new frame.Request();
    upload.open('POST', '/jfb/api/v1/forms/x/uploads?fieldKey=y');
    upload.send(new FormData());
    await vi.waitFor(() => expect(upload.sends).toBe(1));
    expect(upload.headers).toEqual({ Authorization: 'Jfb-Preview first-ticket' });

    const elsewhere = new frame.Request();
    elsewhere.open('GET', '/media/photo.jpg');
    elsewhere.send();
    expect([elsewhere.sends, elsewhere.headers]).toEqual([1, {}]);

    // A request opened again elsewhere is no longer the API's, and its earlier send waits for nothing.
    const reused = new frame.Request();
    reused.open('POST', '/jfb/api/v1/forms/x/uploads');
    reused.send();
    reused.open('GET', '/media/photo.jpg');
    await Promise.resolve();
    await Promise.resolve();
    expect([reused.sends, reused.headers]).toEqual([0, {}]);

    // Aborted while it waits, it goes and is aborted at once: its listeners hear "abort", and nothing more goes.
    const aborted = new frame.Request();
    aborted.open('POST', '/jfb/api/v1/forms/x/uploads');
    aborted.send();
    aborted.abort();
    expect([aborted.sends, aborted.aborted, aborted.events, aborted.headers]).toEqual([1, true, ['abort'], {}]);
    await Promise.resolve();
    await Promise.resolve();
    expect(aborted.sends).toBe(1);
  });

  it('asks over its port for a new ticket once little is left, once for requests at once', async () => {
    const frame = frameWith();
    const { port, asked } = portOf(frame);
    frame.clock = 600_000 - RENEW_BEFORE + 1;
    const first = frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
    const second = frame.win.fetch('/jfb/api/v1/forms/x/validate', { method: 'POST' });
    await vi.waitFor(() => expect(asked).toEqual([{ jfb: TICKET_MESSAGE, form: FORM }]));

    // Only an answer for this form, with a ticket, counts.
    port.postMessage({ ...answer('another'), form: 'another-form' });
    port.postMessage(answer(''));
    await new Promise((done) => setTimeout(done, 20));
    expect(frame.sent).toEqual([]);
    port.postMessage(answer('second-ticket'));
    await Promise.all([first, second]);
    expect(frame.sent.map((each) => each.authorization)).toEqual([
      'Jfb-Preview second-ticket',
      'Jfb-Preview second-ticket',
    ]);

    // The new ticket counts from when it came: nothing is asked until little is left of it.
    frame.clock += 600_000 - RENEW_BEFORE - 1;
    await frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
    expect([asked.length, frame.sent.at(-1)?.authorization]).toEqual([1, 'Jfb-Preview second-ticket']);
  });

  it('takes its port from the backoffice alone, once, and asks over it as soon as it comes', async () => {
    const frame = frameWith({ lifetime: 60 });
    // From elsewhere, or for another form, a port is no port.
    const stranger = new MessageChannel();
    channels.push(stranger);
    const strangerAsked: unknown[] = [];
    stranger.port1.onmessage = (event) => strangerAsked.push(event.data);
    frame.tell({}, { jfb: PORT_MESSAGE, form: FORM }, [stranger.port2]);
    frame.tell(frame.parent, { jfb: PORT_MESSAGE, form: 'another-form' }, [stranger.port2]);

    const sending = frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
    const { port, asked } = portOf(frame);
    await vi.waitFor(() => expect(asked).toHaveLength(1));
    // A second port, the backoffice's or not, changes nothing: the next ask goes over the first.
    frame.tell(frame.parent, { jfb: PORT_MESSAGE, form: FORM }, [stranger.port2]);
    port.postMessage(answer('second-ticket', 60));
    await sending;
    const again = frame.win.fetch('/jfb/api/v1/forms/x/validate', { method: 'POST' });
    await vi.waitFor(() => expect(asked).toHaveLength(2));
    port.postMessage(answer('third-ticket'));
    await again;
    expect(frame.sent.map((each) => each.authorization)).toEqual([
      'Jfb-Preview second-ticket',
      'Jfb-Preview third-ticket',
    ]);
    expect(strangerAsked).toEqual([]);
  });

  it('sends with the ticket held when no new one comes in time, port or none', async () => {
    vi.useFakeTimers();
    const frame = frameWith({ lifetime: 60 });
    const sending = frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
    await vi.advanceTimersByTimeAsync(ASK_WAIT - 1);
    expect(frame.sent).toEqual([]);
    await vi.advanceTimersByTimeAsync(1);
    await sending;
    expect(frame.sent.map((each) => each.authorization)).toEqual(['Jfb-Preview first-ticket']);
  });

  it('shows the page light or dark when the backoffice says so over its port', async () => {
    const frame = frameWith();
    const root = frame.win.document.documentElement;
    const { port } = portOf(frame);
    port.postMessage({ jfb: SCHEME_MESSAGE, dark: true });
    await vi.waitFor(() => expect(root.classList.contains(DARK_CLASS)).toBe(true));
    // A message to the window is no message of the backoffice's.
    frame.tell(frame.parent, { jfb: SCHEME_MESSAGE, dark: false });
    port.postMessage({ jfb: SCHEME_MESSAGE, dark: 'no' });
    await new Promise((done) => setTimeout(done, 20));
    expect(root.classList.contains(DARK_CLASS)).toBe(true);
    port.postMessage({ jfb: SCHEME_MESSAGE, dark: false });
    await vi.waitFor(() => expect(root.classList.contains(DARK_CLASS)).toBe(false));
  });

  it('sends a ticket the backoffice hands over its port unasked from then on, which counts from when it came', async () => {
    const frame = frameWith();
    const { port, asked } = portOf(frame);
    frame.clock = 500_000;
    port.postMessage(answer('handed'));
    await new Promise((done) => setTimeout(done, 20));
    // Little would be left of the document's own ticket by now; the one handed has plenty.
    frame.clock = 600_000;
    await frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
    expect([asked, frame.sent.map((each) => each.authorization)]).toEqual([[], ['Jfb-Preview handed']]);
  });

  it('tells the backoffice which submission the public API took, from the receipt its answer names', async () => {
    const frame = frameWith(
      {},
      {
        respond: (address, method) => {
          const path = new URL(address, SITE).pathname;
          if (method !== 'POST') return receipt('from-a-read');
          if (path.endsWith('/entries')) return receipt(path.includes(FORM) ? 'sent-1' : 'other-form');
          if (path.endsWith('/receipt')) return receipt('sent-2');
          if (path.endsWith('/refused/entries')) return receipt('refused', 422);
          return receipt('not-a-send');
        },
      },
    );
    const { asked } = portOf(frame);
    await frame.win.fetch(`/jfb/api/v1/forms/${FORM}/entries`, { method: 'POST' });
    await frame.win.fetch(new globalThis.Request(`${SITE}/jfb/api/v1/forms/${FORM}/receipt`, { method: 'POST' }));
    await frame.win.fetch(`/jfb/api/v1/forms/another/entries`, { method: 'POST' });
    await frame.win.fetch(`/jfb/api/v1/forms/${FORM}/entries`);
    await frame.win.fetch(`/jfb/api/v1/forms/${FORM}/challenge`, { method: 'POST' });
    await vi.waitFor(() =>
      expect(asked).toEqual([
        { jfb: SENT_MESSAGE, form: FORM, submissionId: 'sent-1' },
        { jfb: SENT_MESSAGE, form: FORM, submissionId: 'sent-2' },
      ]),
    );

    // A refusal, an answer without a receipt, or one that is no JSON, tells nothing; the page still reads the answer.
    const quiet = frameWith(
      {},
      {
        respond: (_address, method) =>
          method === 'POST' ? (quietAnswers.shift() ?? new Response(null, { status: 204 })) : undefined,
      },
    );
    const quietAnswers = [receipt('refused', 422), receipt(''), new Response('not json', { status: 200 }), receipt(7)];
    const { asked: heard } = portOf(quiet);
    for (let each = 0; each < 4; each++) {
      const answered = await quiet.win.fetch(`/jfb/api/v1/forms/${FORM}/entries`, { method: 'POST' });
      expect(answered.bodyUsed).toBe(false);
    }
    await new Promise((done) => setTimeout(done, 20));
    expect(heard).toEqual([]);
  });

  it('makes a sample file of the first kind a field accepts, a PNG for one that names none', async () => {
    const pdf = sampleFile('.docx, .PDF,.png');
    expect([pdf?.name, pdf?.type]).toEqual(['sample.pdf', 'application/pdf']);
    expect(await pdf?.text()).toMatch(/^%PDF-1\.4\n[\s\S]*%%EOF\n$/);
    const png = sampleFile('');
    expect([png?.name, png?.type]).toEqual(['sample.png', 'image/png']);
    const bytes = new Uint8Array(await must(png).arrayBuffer());
    expect([...bytes.slice(0, 8)]).toEqual([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    const jpeg = sampleFile('.jpg,.jpeg,.png');
    expect([jpeg?.name, jpeg?.type]).toEqual(['sample.jpg', 'image/jpeg']);
    expect([...new Uint8Array(await must(jpeg).arrayBuffer()).slice(0, 3)]).toEqual([0xff, 0xd8, 0xff]);
    const gif = sampleFile('.gif');
    expect(await must(gif).slice(0, 6).text()).toBe('GIF89a');
    expect(sampleFile('.csv')?.type).toBe('text/csv');
    expect(sampleFile('.txt')?.type).toBe('text/plain');
    expect(sampleFile('.docx,.zip')).toBeNull();
  });

  it('gives each file field of a document of sample answers its sample file once the runtime took the form on', async () => {
    const body = `<form data-jfb-form>${fileField('.pdf', 'cv')}${fileField('.docx', 'letter')}</form>`;
    const woke: Event[] = [];
    let cv: ReturnType<typeof chooserOf> | undefined;
    let letter: ReturnType<typeof chooserOf> | undefined;
    const frame = frameWith(
      { sample: true },
      {
        body,
        transfer: FakeTransfer as unknown as typeof DataTransfer,
        prepare: (doc) => {
          doc.querySelector('form')?.addEventListener('focusin', (event) => woke.push(event));
          cv = chooserOf(doc, 'cv');
          letter = chooserOf(doc, 'letter');
        },
      },
    );
    // Nothing before the runtime, which loads after the script, took the form on; then the form wakes as a touch
    // wakes it, once, and the runtime takes the field on a moment later.
    expect(woke).toHaveLength(0);
    ready(frame);
    ready(frame);
    expect([woke.length, woke[0]?.bubbles]).toEqual([1, true]);
    expect(cv?.files).toBeNull();
    for (const zone of frame.win.document.querySelectorAll('.jfb-upload')) zone.setAttribute('data-jfb-enhanced', '');
    await vi.waitFor(() => expect(cv?.files?.map((each) => each.name)).toEqual(['sample.pdf']));
    expect([cv?.changes.length, cv?.changes[0]?.bubbles]).toEqual([1, true]);
    expect([letter?.files, letter?.changes]).toEqual([null, []]);
  });

  it('gives a field taken on already its file at once, one that does not wake in time none, and none without a DataTransfer', async () => {
    let taken: ReturnType<typeof chooserOf> | undefined;
    const early = frameWith(
      { sample: true },
      {
        body: `<form data-jfb-form>${fileField('.png', 'taken').replace('class="jfb-upload"', 'class="jfb-upload" data-jfb-enhanced')}</form>`,
        transfer: FakeTransfer as unknown as typeof DataTransfer,
        prepare: (doc) => (taken = chooserOf(doc, 'taken')),
      },
    );
    await new Promise((done) => setTimeout(done, 20));
    expect(taken?.files).toBeNull();
    ready(early);
    await vi.waitFor(() => expect(taken?.files?.map((each) => each.type)).toEqual(['image/png']));

    vi.useFakeTimers();
    let late: ReturnType<typeof chooserOf> | undefined;
    const slow = frameWith(
      { sample: true },
      {
        body: `<form data-jfb-form>${fileField('.png', 'late')}</form>`,
        transfer: FakeTransfer as unknown as typeof DataTransfer,
        prepare: (doc) => (late = chooserOf(doc, 'late')),
      },
    );
    ready(slow);
    await vi.advanceTimersByTimeAsync(SAMPLE_WAIT);
    slow.win.document.querySelector('.jfb-upload')?.setAttribute('data-jfb-enhanced', '');
    await vi.advanceTimersByTimeAsync(10);
    expect(late?.files).toBeNull();
    vi.useRealTimers();

    let plain: ReturnType<typeof chooserOf> | undefined;
    const without = frameWith(
      { sample: true },
      {
        body: `<form data-jfb-form>${fileField('.png', 'plain')}</form>`,
        prepare: (doc) => (plain = chooserOf(doc, 'plain')),
      },
    );
    ready(without);
    without.win.document.querySelector('.jfb-upload')?.setAttribute('data-jfb-enhanced', '');
    await new Promise((done) => setTimeout(done, 20));
    expect(plain?.files).toBeNull();

    // A document without sample answers gives none.
    let unasked: ReturnType<typeof chooserOf> | undefined;
    const plainDocument = frameWith(
      {},
      {
        body: `<form data-jfb-form>${fileField('.png', 'unasked').replace('class="jfb-upload"', 'class="jfb-upload" data-jfb-enhanced')}</form>`,
        transfer: FakeTransfer as unknown as typeof DataTransfer,
        prepare: (doc) => (unasked = chooserOf(doc, 'unasked')),
      },
    );
    ready(plainDocument);
    await new Promise((done) => setTimeout(done, 20));
    expect(unasked?.files).toBeNull();
  });

  it('leaves a document without its ticket as it is', () => {
    for (const meta of [null, { content: '' }, { form: '' }]) {
      const frame = frameWith(meta);
      expect(frame.win.fetch).toBe(frame.original);
      void frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
      expect(frame.sent.map((each) => each.authorization)).toEqual([null]);
    }
  });
});
