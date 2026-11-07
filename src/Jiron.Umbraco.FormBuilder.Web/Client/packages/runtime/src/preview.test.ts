// The preview script (ADR-0203): the document's ticket goes with its requests to the public API, and only those; a
// ticket with little left is renewed by asking the backoffice first, over the port it handed this document, once for
// requests at once, and a request goes with the one held when no answer comes; only the backoffice's own port counts;
// an upload aborted while it waits goes and is aborted as a browser aborts one; and the page turns light or dark.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ASK_WAIT, DARK_CLASS, lend, type PreviewWindow } from './preview';
import { PORT_MESSAGE, RENEW_BEFORE, SCHEME_MESSAGE, TICKET_MESSAGE } from './preview-protocol';

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

/** A frame with the preview's meta element, or without one; its clock starts at 0. */
function frameWith(meta: { content?: string; form?: string; api?: string; lifetime?: number } | null = {}): Frame {
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
    doc.head.append(element);
  }
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
        sent.push({
          address: input instanceof globalThis.Request ? input.url : String(input),
          authorization: headers.get('Authorization'),
          accept: headers.get('Accept'),
        });
        return Promise.resolve(new Response(null, { status: 204 }));
      },
      XMLHttpRequest: Request as unknown as typeof XMLHttpRequest,
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

  it('leaves a document without its ticket as it is', () => {
    for (const meta of [null, { content: '' }, { form: '' }]) {
      const frame = frameWith(meta);
      expect(frame.win.fetch).toBe(frame.original);
      void frame.win.fetch('/jfb/api/v1/forms/x/challenge', { method: 'POST' });
      expect(frame.sent.map((each) => each.authorization)).toEqual([null]);
    }
  });
});
