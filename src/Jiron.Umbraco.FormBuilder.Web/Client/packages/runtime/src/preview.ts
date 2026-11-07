// The preview's frame (ADR-0203). The preview's document alone loads this script, before the runtime; a visitor's page
// never does. It lends the ticket of the document's meta element to every request the document makes to the public
// API, as `Authorization: Jfb-Preview <ticket>`, and asks the backoffice for a new one first when less than two
// minutes of it are left, so a ticket is renewed only for a request a user made in the frame. It asks over the port the
// backoffice hands the document once it has loaded, which a page the frame goes on to never gets, and shows the page
// light or dark when the backoffice says so over it, which keeps the answers given.
import {
  isPortMessage,
  isSchemeMessage,
  isTicketAnswer,
  RENEW_BEFORE,
  TICKET_MESSAGE,
  type TicketAsk,
} from './preview-protocol';

/** The name of the meta element that holds the ticket. */
export const TICKET_META = 'jfb-preview';

/** The class of the document's root in the dark scheme (ADR-0106). */
export const DARK_CLASS = 'jfb-scheme-dark';

/** How long a request waits for a new ticket before it goes with the one held, in milliseconds. */
export const ASK_WAIT = 5_000;

/** The window the script works in: the frame's own, or a test's. */
export interface PreviewWindow {
  readonly document: Document;
  readonly parent: unknown;
  fetch: typeof fetch;
  readonly XMLHttpRequest: typeof XMLHttpRequest;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  setTimeout(handler: () => void, timeout: number): number;
  clearTimeout(id: number): void;
}

/** Lends the document's ticket to its requests to the public API; a document without one is left as it is. */
export function lend(win: PreviewWindow, now: () => number = Date.now): void {
  const doc = win.document;
  const meta = doc.querySelector<HTMLMetaElement>(`meta[name="${TICKET_META}"]`);
  const form = meta?.dataset['form'];
  if (!meta || !form || meta.content === '') return;
  const api = new URL(meta.dataset['api'] ?? '/', doc.baseURI);
  const root = api.pathname.endsWith('/') ? api.pathname : `${api.pathname}/`;
  let ticket = meta.content;
  let until = now() + Number(meta.dataset['lifetime'] ?? 0) * 1000;
  let asking: Promise<string> | null = null;
  let port: MessagePort | null = null;
  let answered: ((heard: MessageEvent) => void) | null = null;
  const portWaiters: ((port: MessagePort) => void)[] = [];

  // The backoffice's one message to the window: its port, which only this document gets, and only once.
  win.addEventListener('message', (event) => {
    const given = event.ports[0];
    if (port !== null || event.source !== win.parent || !isPortMessage(event.data, form) || !given) return;
    port = given;
    port.onmessage = (heard) => {
      if (isSchemeMessage(heard.data)) doc.documentElement.classList.toggle(DARK_CLASS, heard.data.dark);
      else answered?.(heard);
    };
    for (const waiter of portWaiters.splice(0)) waiter(given);
  });

  /** Whether an address is the public API's. */
  const ours = (address: string | URL): boolean => {
    const url = new URL(address, doc.baseURI);
    return url.origin === api.origin && (url.pathname === api.pathname || url.pathname.startsWith(root));
  };

  /** The backoffice's new ticket, or the one held when none comes in time. */
  const ask = (): Promise<string> =>
    new Promise<string>((resolve) => {
      let timer = 0;
      const done = (held: string) => {
        win.clearTimeout(timer);
        answered = null;
        resolve(held);
      };
      timer = win.setTimeout(() => done(ticket), ASK_WAIT);
      answered = (heard) => {
        if (!isTicketAnswer(heard.data, form)) return;
        ticket = heard.data.ticket;
        until = now() + heard.data.lifetime * 1000;
        done(ticket);
      };
      const send = (to: MessagePort) => to.postMessage({ jfb: TICKET_MESSAGE, form } satisfies TicketAsk);
      if (port) send(port);
      else portWaiters.push(send);
    });

  /** The ticket to send now: the one held, or a new one once little is left of it. */
  const current = (): Promise<string> => {
    if (until - now() > RENEW_BEFORE) return Promise.resolve(ticket);
    asking ??= ask().finally(() => (asking = null));
    return asking;
  };

  const fetchAlone = win.fetch.bind(win);
  win.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const address = input instanceof Request ? input.url : input;
    if (!ours(address)) return fetchAlone(input, init);
    const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
    headers.set('Authorization', `Jfb-Preview ${await current()}`);
    return fetchAlone(input, { ...init, headers });
  };

  // An upload's request is opened, then sent: one to the public API waits for the ticket before it goes. Aborted while
  // it waits, it goes and is aborted at once, so its listeners hear of it as a browser tells them, and the ticket it
  // waited for sends nothing.
  const requests = win.XMLHttpRequest.prototype;
  const openAlone = requests.open;
  const sendAlone = requests.send;
  const abortAlone = requests.abort;
  const toApi = new WeakSet<XMLHttpRequest>();
  const waiting = new WeakSet<XMLHttpRequest>();
  requests.open = function (this: XMLHttpRequest, ...args: [string, string | URL, ...unknown[]]) {
    // Opened again, it is another request: a send that waited for a ticket goes nowhere.
    waiting.delete(this);
    if (ours(args[1])) toApi.add(this);
    else toApi.delete(this);
    Reflect.apply(openAlone, this, args);
  };
  requests.send = function (this: XMLHttpRequest, body?: Document | XMLHttpRequestBodyInit | null) {
    if (!toApi.has(this)) {
      sendAlone.call(this, body);
      return;
    }
    waiting.add(this);
    void current().then((held) => {
      if (!waiting.delete(this)) return;
      this.setRequestHeader('Authorization', `Jfb-Preview ${held}`);
      sendAlone.call(this, body);
    });
  };
  requests.abort = function (this: XMLHttpRequest) {
    if (waiting.delete(this)) sendAlone.call(this, null);
    abortAlone.call(this);
  };
}

if (typeof window !== 'undefined') lend(window as unknown as PreviewWindow);
