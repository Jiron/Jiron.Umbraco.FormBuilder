// The preview's frame (ADR-0203, ADR-0204). The preview's document alone loads this script, before the runtime; a
// visitor's page never does. It lends the ticket of the document's meta element to every request the document makes to
// the public API, as `Authorization: Jfb-Preview <ticket>`, and asks the backoffice for a new one first when less than
// two minutes of it are left, so a ticket is renewed only for a request a user made in the frame. It asks over the port
// the backoffice hands the document once it has loaded, which a page the frame goes on to never gets; over it the
// backoffice hands a new ticket unasked too, as when the test entries' actions are switched, and says when the page turns
// light or dark, which keeps the answers given. Once the public API took a submission, the script says which over the
// port, so the backoffice can say which test entry it made; once it refused the ticket, which no renewal brings back, the
// script says so, once, so the backoffice can load the preview again. A document of sample answers gets a sample file in each
// file field once the runtime, which loads after this script, has taken its form on, which goes up as a visitor's file
// does.
import { proto } from '@jfb/logic/proto';
import {
  EXPIRED_MESSAGE,
  isPortMessage,
  isSchemeMessage,
  isTicketAnswer,
  RENEW_BEFORE,
  SENT_MESSAGE,
  TICKET_MESSAGE,
  type ExpiredMessage,
  type SentMessage,
  type TicketAsk,
} from './preview-protocol';

/** The name of the meta element that holds the ticket. */
export const TICKET_META = 'jfb-preview';

/** The class of the document's root in the dark scheme (ADR-0106). */
export const DARK_CLASS = 'jfb-scheme-dark';

/** How long a request waits for a new ticket before it goes with the one held, in milliseconds. */
export const ASK_WAIT = 5_000;

/** How long a document of sample answers waits for a file field to wake, in milliseconds. */
export const SAMPLE_WAIT = 10_000;

/** The window the script works in: the frame's own, or a test's. */
export interface PreviewWindow {
  readonly document: Document;
  readonly parent: unknown;
  fetch: typeof fetch;
  readonly XMLHttpRequest: typeof XMLHttpRequest;
  readonly DataTransfer?: typeof DataTransfer;
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
  let answered: (() => void) | null = null;
  const portWaiters: ((port: MessagePort) => void)[] = [];

  // The backoffice's one message to the window: its port, which only this document gets, and only once. A ticket that
  // comes over it, asked for or not, is the one to send from then on.
  win.addEventListener('message', (event) => {
    const given = event.ports[0];
    if (port !== null || event.source !== win.parent || !isPortMessage(event.data, form) || !given) return;
    port = given;
    port.onmessage = (heard) => {
      if (isSchemeMessage(heard.data)) doc.documentElement.classList.toggle(DARK_CLASS, heard.data.dark);
      else if (isTicketAnswer(heard.data, form)) {
        ticket = heard.data.ticket;
        until = now() + heard.data.lifetime * 1000;
        answered?.();
      }
    };
    for (const waiter of portWaiters.splice(0)) waiter(given);
  });

  /** Whether an address is the public API's. */
  const ours = (address: string | URL): boolean => {
    const url = new URL(address, doc.baseURI);
    return url.origin === api.origin && (url.pathname === api.pathname || url.pathname.startsWith(root));
  };

  /** Whether a request sends the form, or asks for the receipt of a send, whose answer names the submission. */
  const sends = (address: string | URL, method: string): boolean => {
    const path = new URL(address, doc.baseURI).pathname;
    return (
      method.toUpperCase() === 'POST' &&
      (path === `${root}forms/${form}/entries` || path === `${root}forms/${form}/receipt`)
    );
  };

  /** Tells the backoffice which submission the public API took: the one its answer's receipt names. */
  const tell = (answer: Response): void => {
    if (!answer.ok) return;
    void answer
      .clone()
      .json()
      .then((body: unknown) => {
        const submissionId = (body as { receipt?: { submissionId?: unknown } | null } | null)?.receipt?.submissionId;
        if (typeof submissionId === 'string' && submissionId !== '')
          port?.postMessage({ jfb: SENT_MESSAGE, form, submissionId } satisfies SentMessage);
      })
      .catch(() => undefined);
  };

  /** Tells the backoffice, once, that the public API refused the ticket: `401 preview_ticket_invalid`. */
  let refused = false;
  const tellRefused = (answer: Response): void => {
    if (refused || answer.status !== 401) return;
    void answer
      .clone()
      .json()
      .then((body: unknown) => {
        if (refused || (body as { code?: unknown } | null)?.code !== 'preview_ticket_invalid') return;
        refused = true;
        port?.postMessage({ jfb: EXPIRED_MESSAGE, form } satisfies ExpiredMessage);
      })
      .catch(() => undefined);
  };

  /** The backoffice's new ticket, or the one held when none comes in time. */
  const ask = (): Promise<string> =>
    new Promise<string>((resolve) => {
      let timer = 0;
      const done = (): void => {
        win.clearTimeout(timer);
        answered = null;
        resolve(ticket);
      };
      timer = win.setTimeout(done, ASK_WAIT);
      answered = done;
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
    const answer = await fetchAlone(input, { ...init, headers });
    if (sends(address, init?.method ?? (input instanceof Request ? input.method : 'GET'))) tell(answer);
    tellRefused(answer);
    return answer;
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

  if (meta.dataset['sample'] === 'true') giveSampleFiles(win);
}

// A small file of each kind the script can make, which the checks of every kind take (ADR-0204): a 1 by 1 pixel image,
// a page of a PDF and a line of text.
const PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';
const GIF = 'R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
const JPEG =
  '/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=';
const PDF = [
  '%PDF-1.4',
  '1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj',
  '2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>> endobj',
  '3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 200 200]>> endobj',
  'trailer <</Root 1 0 R>>',
  '%%EOF',
  '',
].join('\n');
const SAMPLES = new Map<string, [type: string, bytes: () => BlobPart]>([
  ['png', ['image/png', () => decoded(PNG)]],
  ['gif', ['image/gif', () => decoded(GIF)]],
  ['jpg', ['image/jpeg', () => decoded(JPEG)]],
  ['jpeg', ['image/jpeg', () => decoded(JPEG)]],
  ['pdf', ['application/pdf', () => PDF]],
  ['txt', ['text/plain', () => 'A sample file from the preview.\n']],
  ['csv', ['text/csv', () => 'name,email\nAlex Berger,alex.berger@example.com\n']],
]);

const decoded = (base64: string): Uint8Array<ArrayBuffer> =>
  Uint8Array.from(atob(base64), (unit) => unit.charCodeAt(0));

/**
 * A sample file for a file field: of the first kind its field accepts that the script can make, a PNG for a field that
 * names none; null for a field that accepts none of them.
 */
export function sampleFile(accept: string): File | null {
  const kinds = accept === '' ? ['png'] : accept.split(',').map((each) => each.trim().replace(/^\./, '').toLowerCase());
  const kind = kinds.find((each) => SAMPLES.has(each));
  const sample = kind === undefined ? undefined : SAMPLES.get(kind);
  return sample === undefined ? null : new File([sample[1]()], `sample.${kind ?? ''}`, { type: sample[0] });
}

/**
 * Gives each file field of the document's forms a sample file. The runtime loads after this script and says when it
 * has taken a form on (`jfb:ready`); the form is then woken as a touch wakes it, once, and each field gets its file
 * once the runtime has taken that field on, as though the visitor chose it. A field that does not wake in time, or
 * accepts no kind the script makes, keeps none.
 */
function giveSampleFiles(win: PreviewWindow): void {
  const Transfer = win.DataTransfer;
  if (Transfer === undefined) return;
  const given = new WeakSet<EventTarget>();
  win.document.addEventListener('jfb:ready', (event) => {
    const form = event.target as HTMLFormElement | null;
    if (form === null || given.has(form) || !proto(form).matches('form[data-jfb-form]')) return;
    given.add(form);
    proto(form).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    for (const zone of proto(form).querySelectorAll<HTMLElement>('.jfb-upload')) {
      const chooser = zone.querySelector<HTMLInputElement>('input[type="file"]:not(.jfb-upload-camera)');
      const file = chooser === null ? null : sampleFile(chooser.accept);
      if (chooser === null || file === null) continue;
      void woken(win, zone).then((ready) => {
        if (!ready) return;
        const transfer = new Transfer();
        transfer.items.add(file);
        chooser.files = transfer.files;
        chooser.dispatchEvent(new Event('change', { bubbles: true }));
      });
    }
  });
}

/** Whether a file field is taken on by the runtime, now or before the wait runs out. */
function woken(win: PreviewWindow, zone: HTMLElement): Promise<boolean> {
  const done = (): boolean => zone.hasAttribute('data-jfb-enhanced');
  if (done()) return Promise.resolve(true);
  return new Promise((resolve) => {
    const watch = new MutationObserver(() => {
      if (!done()) return;
      watch.disconnect();
      win.clearTimeout(timer);
      resolve(true);
    });
    const timer = win.setTimeout(() => {
      watch.disconnect();
      resolve(done());
    }, SAMPLE_WAIT);
    watch.observe(zone, { attributes: true, attributeFilter: ['data-jfb-enhanced'] });
  });
}

if (typeof window !== 'undefined') lend(window as unknown as PreviewWindow);
