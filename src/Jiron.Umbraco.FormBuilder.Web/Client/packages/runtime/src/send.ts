/**
 * Sending on the page (ROADMAP 6.4i, ADR-0120): a send that passes the page's checks goes to the entries endpoint as
 * JSON, the answers as the page posts them, with a submission id of the browser's own random numbers that every retry
 * keeps (ADR-0088). While it is on its way, a second press sends nothing, and the primary button is busy (ADR-0022): it
 * keeps its place and its focus, and the live region says so. The entry's outcome takes the form's place, and a
 * redirect the page's; the server's errors show as the page's own; a refused or failed send (`rate_limited`,
 * `server_error`, `timeout`) puts the theme's notice just above the buttons, with every answer kept. Any other answer,
 * and a form that stores files until 6.4l and 6.4m send them, goes the way without JavaScript, which the server answers
 * in full. The notices, the outcome and their words are the server's render, in the theme's views and the page's
 * language, in the form's templates.
 */
import type { PostAnswer, ReadPost } from '@jfb/logic';
import type { PageErrors, ServerProblem } from './errors';
import { parseSanitised } from './html';
import type { FormModel } from './model';
import { readPage } from './page';
import { contextOf } from './rules';

/** What the send events tell a site: the form, and for `jfb:error` the send's code; never an answer. */
export interface SendDetail {
  readonly key: string;
  readonly alias: string;
  readonly code?: string;
}

/** Sends a form whose answers pass the page's checks: true when the page answered the press, false to post it. */
export type Deliver = (button: HTMLButtonElement, errors: PageErrors) => Promise<boolean>;

/** The entries endpoint's outcome of an entry, as far as the page shows it (ADR-0088). */
interface Outcome {
  readonly kind?: string;
  readonly html?: string;
  readonly url?: string;
  readonly delaySeconds?: number;
}

const BUSY = 'data-jfb-busy';
const DISABLED = 'aria-disabled';
/** How long a send waits for its answer: longer than the endpoint's own limit of 30 seconds. */
const WAIT = 40_000;
/** The form's attributes its outcome keeps, as the server's render of an outcome writes them. */
const KEPT = ['class', 'id', 'data-jfb-form', 'data-jfb-contract', 'lang', 'dir'];

/** A version 4 UUID from the browser's random numbers, which every origin has; `crypto.randomUUID()` needs a secure one. */
export function submissionId(
  random: (bytes: Uint8Array<ArrayBuffer>) => Uint8Array = (bytes) => crypto.getRandomValues(bytes),
): string {
  const bytes = random(new Uint8Array(16));
  bytes[6] = ((bytes[6] ?? 0) & 15) | 64;
  bytes[8] = ((bytes[8] ?? 0) & 63) | 128;
  const hex = [...bytes].map((byte) => byte.toString(16).padStart(2, '0')).join('');
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
}

/**
 * The entries endpoint's body for a post (ADR-0088): each answer's texts by its alias and each part's by
 * `alias.part`, a consent's wording among them, a repeatable group's rows as objects of their children's, and the
 * query and context values, so the server reads what the post without JavaScript would give it.
 */
export function bodyOf(
  model: FormModel,
  post: ReadPost,
  query: Readonly<Record<string, string | readonly string[]>>,
  id: string,
  content?: string,
): object {
  const put = (into: Record<string, unknown>, answers: ReadonlyMap<string, PostAnswer>): Record<string, unknown> => {
    for (const [alias, answer] of answers) {
      into[alias] = answer.values;
      for (const [part, texts] of answer.parts ?? []) into[`${alias}.${part}`] = texts;
    }
    return into;
  };
  const values = put({}, post.fields);
  for (const [alias, rows] of post.rows) if (rows.length > 0) values[alias] = rows.map((row) => put({}, row));
  return {
    submissionId: id,
    revision: model.revision,
    culture: model.culture,
    contentKey: content,
    values,
    query,
    context: Object.fromEntries(post.context),
  };
}

/** The code of a send the page answers with its notice: too many sends, the server's error or no answer in time. */
function noticeOf(status: number, code: unknown): string | null {
  if (status === 0 || status === 504 || code === 'timeout') return 'timeout';
  if (status === 429) return 'rate_limited';
  return status >= 500 ? 'server_error' : null;
}

/** A form's sender: one submission id for every send of the page, which makes a retry safe. */
export function senderOf(form: HTMLFormElement, model: FormModel, id: string = submissionId()): Deliver {
  const templates = form.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]');
  const api = form.dataset['jfbApi'];
  let notice: Element | null | undefined;
  return async (button, errors) => {
    if (templates === null || api === undefined || form.enctype === 'multipart/form-data') return false;
    const primary = form.querySelector<HTMLElement>('[data-jfb-nav="submit"]') ?? button;
    const live = form.querySelector<HTMLElement>(':scope > [data-jfb-live]');
    const detail: SendDetail = { key: form.dataset['jfbForm'] ?? '', alias: model.alias };
    const event = (type: string, code?: string): void => {
      form.dispatchEvent(
        new CustomEvent<SendDetail>(type, { bubbles: true, detail: code ? { ...detail, code } : detail }),
      );
    };
    notice?.remove();
    primary.setAttribute(BUSY, '');
    primary.setAttribute(DISABLED, 'true');
    if (live !== null) live.textContent = templates.dataset['jfbSending'] ?? '';
    event('jfb:submitting');
    let status = 0;
    let answer: { outcome?: Outcome; receipt?: { reference?: string }; errors?: ServerProblem[]; code?: unknown } = {};
    try {
      const reading = readPage(form, model);
      const query = contextOf(model, reading, Date.now(), form.ownerDocument.URL).query;
      const response = await fetch(`${api}/forms/${detail.key}/entries`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(bodyOf(model, reading.post, query, id, form.dataset['jfbContent'])),
        signal: AbortSignal.timeout(WAIT),
      });
      status = response.status;
      answer = ((await response.json().catch(() => null)) as typeof answer | null) ?? {};
    } catch {
      // No answer came in time, or none at all.
    } finally {
      primary.removeAttribute(BUSY);
      primary.removeAttribute(DISABLED);
      if (live !== null) live.textContent = '';
    }
    // A stored entry is never sent again, whatever its answer holds.
    if (status === 200) {
      event('jfb:submitted');
      showOutcome(form, templates, answer.outcome ?? {}, answer.receipt?.reference);
      return true;
    }
    if (status === 422 && Array.isArray(answer.errors)) return errors.server(answer.errors);
    const code = noticeOf(status, answer.code);
    if (code === null) return false;
    event('jfb:error', code);
    notice = templates.content.querySelector(`[data-jfb-notice="${code}"]`)?.cloneNode(true) as Element | undefined;
    if (notice instanceof HTMLElement) {
      (primary.parentElement ?? primary).before(notice);
      notice.focus();
    }
    return true;
  };
}

/** An address the page may go to: the web's, as the document resolves it; null for any other. */
function addressOf(url: string | undefined, document: Document): string | null {
  if (url === undefined) return null;
  try {
    const address = new URL(url, document.baseURI);
    return address.protocol === 'https:' || address.protocol === 'http:' ? address.href : null;
  } catch {
    return null;
  }
}

/**
 * The outcome in the form's place, which takes the focus: the message (the page's own thank-you when the form has
 * none) and the reference; a redirect after it counts down, with "Go now" and "Stay here" (WCAG 2.2.1); a redirect
 * without one goes at once.
 */
function showOutcome(
  form: HTMLFormElement,
  templates: HTMLTemplateElement,
  outcome: Outcome,
  reference?: string,
): void {
  const document = form.ownerDocument;
  const go = (address: string): void => document.defaultView?.location.assign(address);
  const url = addressOf(outcome.url, document);
  const delay = outcome.delaySeconds ?? 0;
  if (url !== null && (outcome.kind === 'redirect' || delay <= 0)) return go(url);
  const shown = templates.content.querySelector('[data-jfb-outcome]')?.cloneNode(true);
  if (!(shown instanceof HTMLElement)) return;
  shown.prepend(
    outcome.html
      ? parseSanitised(document, outcome.html)
      : Object.assign(document.createElement('p'), { textContent: templates.dataset['jfbThanks'] ?? '' }),
  );
  const line = shown.querySelector('[data-jfb-reference]');
  const strong = line?.querySelector('strong');
  if (reference && strong) strong.textContent = reference;
  else line?.remove();
  const count = shown.querySelector<HTMLElement>('[data-jfb-countdown]');
  const link = shown.querySelector<HTMLAnchorElement>('a[data-jfb-continue]');
  if (url === null || count === null || link === null) {
    count?.remove();
    link?.parentElement?.remove();
  } else {
    link.href = url;
    let left = delay;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const tick = (): void => {
      if (left <= 0) return go(url);
      count.textContent = (count.dataset[left === 1 ? 'jfbOne' : 'jfbOther'] ?? '').replace('{0}', String(left));
      left -= 1;
      timer = setTimeout(tick, 1000);
    };
    tick();
    shown.querySelector('[data-jfb-stay]')?.addEventListener('click', (event) => {
      clearTimeout(timer);
      count.remove();
      (event.currentTarget as Element).remove();
      link.focus();
    });
  }
  const place = document.createElement('div');
  for (const name of KEPT) {
    const value = form.getAttribute(name);
    if (value !== null) place.setAttribute(name, value);
  }
  place.append(shown);
  form.replaceWith(place);
  shown.focus();
}
