/**
 * Sending on the page (ROADMAP 6.4i, ADR-0120): a send that passes the page's checks goes to the entries endpoint as
 * JSON, the answers as the page posts them, with a submission id of the browser's own random numbers that every retry
 * keeps (ADR-0088), the challenge the page asked for when it woke (ADR-0138), renewed when it has expired, the
 * honeypot's value (ADR-0139) and the placement's token (ADR-0129). While it is on its way, a second press sends nothing, and the primary button is busy (ADR-0022): it
 * keeps its place and its focus, and the live region says so. The entry's outcome takes the form's place, and a
 * redirect the page's; the server's errors show as the page's own; a refused or failed send (`rate_limited`,
 * `server_error`, `timeout`) puts the theme's notice just above the buttons, with every answer kept. Any other answer,
 * a form whose files the page cannot take (6.4m and 6.4n: only on one page) and a view that writes none of the
 * send's parts go the way without JavaScript, which the server answers in full; a form with no address to post to (ADR-0128) shows the notice of a
 * failed send instead. An entry already stored under the page's id is never sent again: its `409` shows that entry's
 * outcome. The notices, the outcome and their words are the server's render, in the theme's views and the page's
 * language, in the form's templates.
 */
import type { PostAnswer, ReadPost } from '@jfb/logic';
import type { PageErrors, ServerProblem } from './errors';
import { swapForm } from './changed';
import { parseSanitised } from './html';
import type { PageUploads, UploadedFile } from './uploads';
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

/** What the entries and receipt endpoints answer, as far as the page reads it. */
interface Answer {
  readonly outcome?: Outcome;
  readonly receipt?: { readonly reference?: string };
  readonly errors?: ServerProblem[];
  readonly code?: unknown;
}

const BUSY = 'data-jfb-busy';
const DISABLED = 'aria-disabled';
/** How long a send waits for its answer: longer than the endpoint's own limit of 30 seconds. */
const WAIT = 40_000;
/** The form's attributes its outcome keeps, as the server's render of an outcome writes them. */
const KEPT = ['class', 'id', 'data-jfb-form', 'data-jfb-contract', 'lang', 'dir'];
/** The fewest seconds a redirect after a message counts down: a shorter time leaves none to reach "Stay here" (WCAG 2.2.1). */
const LEAST_COUNTDOWN = 10;

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

/** A post's answers as the entries endpoint takes them: each answer's texts by its alias, each part's by `alias.part`, a consent's wording among them, and a repeatable group's rows as objects of their children's. */
export function valuesOf(post: ReadPost): Record<string, unknown> {
  const put = (into: Record<string, unknown>, answers: ReadonlyMap<string, PostAnswer>): Record<string, unknown> => {
    for (const [alias, answer] of answers) {
      into[alias] = answer.values;
      for (const [part, texts] of answer.parts ?? []) into[`${alias}.${part}`] = texts;
    }
    return into;
  };
  const values = put({}, post.fields);
  for (const [alias, rows] of post.rows) if (rows.length > 0) values[alias] = rows.map((row) => put({}, row));
  return values;
}

/**
 * The entries endpoint's body for a post (ADR-0088): the answers, and the query and context values, so the server reads
 * what the post without JavaScript would give it.
 */
export function bodyOf(
  model: FormModel,
  post: ReadPost,
  query: Readonly<Record<string, string | readonly string[]>>,
  id: string,
  extra: { content?: string; challenge?: string; honeypot?: string; placement?: string } = {},
): object {
  const values = valuesOf(post);
  return {
    submissionId: id,
    revision: model.revision,
    culture: model.culture,
    contentKey: extra.content,
    values,
    query,
    context: Object.fromEntries(post.context),
    challenge: extra.challenge,
    honeypot: extra.honeypot,
    placement: extra.placement,
  };
}

/** The code of a send the page answers with its notice: too many sends, the server's error or no answer in time. */
function noticeOf(status: number, code: unknown): string | null {
  if (status === 0 || status === 504 || code === 'timeout') return 'timeout';
  if (status === 429) return 'rate_limited';
  return status >= 500 ? 'server_error' : null;
}

/** The notice's wait after a `429`, in the larger unit from two minutes on, as a post words it. */
function wordWait(notice: Element, retryAfter: string | null): void {
  const line = notice.querySelector<HTMLElement>('[data-jfb-seconds]');
  if (line === null) return;
  const seconds = Math.max(2, Number(retryAfter) || 120);
  line.textContent =
    seconds < 120
      ? (line.dataset['jfbSeconds'] ?? '').replace('{0}', String(seconds))
      : (line.dataset['jfbMinutes'] ?? '').replace('{0}', String(Math.ceil(seconds / 60)));
}

/** What a sender starts from; each has its default but in a test, and in a form that came in another's place. */
export interface SenderOptions {
  /** The submission id: a form that came in another's place keeps it (ADR-0004). */
  readonly id?: string;
  /** The challenge, which that form keeps too, as no entry used it and its first time still counts. */
  readonly challenge?: string | null;
  /** Goes to an address. */
  readonly go?: (address: string) => void;
  /** Gives a form that came in this one's place its logic. */
  readonly rewake?: (form: HTMLFormElement, model: FormModel, carried: Carried) => void;
  /** The form's uploads, when the page takes its files; a send waits for them (6.4m). */
  readonly files?: PageUploads;
  /** The challenge the page holds, shared with its uploads, whose tokens name it (ADR-0138). */
  readonly challenges?: Challenges;
}

/** What a form that came in another's place keeps. */
export interface Carried {
  readonly id: string;
  readonly challenge: string | null;
  readonly files?: ReadonlyMap<string, readonly UploadedFile[]>;
}

/** The challenge a form holds: asked for when it wakes, asked again when that failed, renewed when it has expired. */
export interface Challenges {
  current(): Promise<string | null>;
  /** Asks again, without the one before it: a first asking that failed. */
  again(): Promise<string | null>;
  /** Asks again with the one it holds, whose first time a renewal carries over. */
  renew(): Promise<string | null>;
}

/** A form's challenge: asked for now, unless it came with one. */
export function challengesOf(form: HTMLFormElement, api: string, given?: string | null): Challenges {
  const content = form.dataset['jfbContent'] || undefined;
  const ask = (previous?: string): Promise<string | null> =>
    fetch(`${api}/forms/${form.dataset['jfbForm'] ?? ''}/challenge`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ previous, contentKey: content }),
      signal: AbortSignal.timeout(WAIT),
    })
      .then((response) => (response.ok ? (response.json() as Promise<{ challenge?: string }>) : null))
      .then((answer) => answer?.challenge ?? null)
      .catch(() => null);
  let held = given === undefined ? ask() : Promise.resolve(given);
  return {
    current: () => held,
    again: () => (held = ask()),
    renew: async () => {
      const last = await held;
      held = ask(last ?? undefined);
      return held;
    },
  };
}

/** A form's sender: one submission id for every send of the page, which makes a retry safe. */
export function senderOf(form: HTMLFormElement, model: FormModel, options: SenderOptions = {}): Deliver {
  const id = options.id ?? submissionId();
  const go = options.go ?? ((address: string) => form.ownerDocument.defaultView?.location.assign(address));
  const templates = form.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]');
  const api = form.dataset['jfbApi'];
  const key = form.dataset['jfbForm'] ?? '';
  const content = form.dataset['jfbContent'] || undefined;
  const sends = templates?.dataset['jfbSending'] !== undefined && api !== undefined;
  const take =
    sends &&
    (options.files !== undefined ||
      (form.enctype !== 'multipart/form-data' && !Object.values(model.fields).some((field) => field.kind === 'files')));
  const posts = form.hasAttribute('action');
  const post = (path: string, body: object): Promise<Response> =>
    fetch(`${api}/forms/${key}/${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(WAIT),
    });
  const challenges =
    !take || api === undefined ? null : (options.challenges ?? challengesOf(form, api, options.challenge));
  /** The challenges the page's sends went with, one of which its entry names. */
  const held = new Set<string>();
  let notice: Element | null | undefined;
  return async (button, errors) => {
    if (templates === null || challenges === null) return false;
    const primary = form.querySelector<HTMLElement>('[data-jfb-nav="submit"]') ?? button;
    const live = form.querySelector<HTMLElement>(':scope > [data-jfb-live]');
    const detail: SendDetail = { key, alias: model.alias };
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
    let retryAfter: string | null = null;
    let answer: Answer = {};
    let handedOver = false;
    let swapped: Awaited<ReturnType<typeof swapForm>> = null;
    try {
      // A file that failed has the focus by now, and the answers stay on the page.
      if ((await options.files?.settled()) === false) return true;
      const reading = readPage(form, model);
      const query = contextOf(model, reading, Date.now(), form.ownerDocument.URL).query;
      const honeypot = form.querySelector<HTMLInputElement>(':scope > .jfb-hp input')?.value;
      const placement = form.dataset['jfbPlacement'] || undefined;
      const send = async (): Promise<Response> => {
        const current = await challenges.current();
        if (current !== null) held.add(current);
        return post(
          'entries',
          bodyOf(model, reading.post, query, id, {
            ...(content === undefined ? {} : { content }),
            ...(current === null ? {} : { challenge: current }),
            ...(honeypot ? { honeypot } : {}),
            ...(placement === undefined ? {} : { placement }),
          }),
        );
      };
      // The page asks again when its first asking failed. A challenge made now says the visitor filled the form in no
      // time, which the score holds against them (ADR-0139), so a form that can post goes that way, where no time counts.
      if ((await challenges.current()) === null) {
        const asked = await challenges.again();
        handedOver = posts && asked !== null;
      }
      if (!handedOver) {
        let response = await send();
        // A slow visitor's challenge has expired: the renewal carries its first time over.
        if (response.status === 401) {
          await challenges.renew();
          response = await send();
        }
        status = response.status;
        retryAfter = response.headers.get('Retry-After');
        answer = ((await response.json().catch(() => null)) as Answer | null) ?? {};
        // Published again meanwhile: the form as it is now comes in this one's place.
        if (status === 409 && answer.code === 'form_changed' && posts && api !== undefined) {
          swapped = await swapForm(form, model, api);
        }
        // The page's id holds an entry with other answers: one whose answer was lost, whatever was changed since.
        if (status === 409 && answer.code === 'duplicate_submission') {
          answer = {};
          status = 200;
          for (const each of held) {
            const stored = await post('receipt', { submissionId: id, challenge: each, placement }).catch(() => null);
            if (stored?.ok) {
              answer = (await stored.json().catch(() => ({}))) as Answer;
              break;
            }
          }
        }
      }
    } catch (failed) {
      // No answer came in time, or none at all, or a signature's upload was refused, which says how.
      const refused = failed as { status?: number; retryAfter?: string | null };
      if (refused.status !== undefined) {
        status = refused.status;
        retryAfter = refused.retryAfter ?? null;
      }
    } finally {
      primary.removeAttribute(BUSY);
      primary.removeAttribute(DISABLED);
      // What a file that failed said stays to be read.
      if (live !== null && live.textContent === templates.dataset['jfbSending']) live.textContent = '';
    }
    if (handedOver) return false;
    if (swapped !== null) {
      swapped.form.dispatchEvent(
        new CustomEvent<SendDetail>('jfb:error', {
          bubbles: true,
          detail: { key, alias: swapped.model.alias, code: 'form_changed' },
        }),
      );
      const files = options.files?.uploaded();
      options.rewake?.(swapped.form, swapped.model, {
        id,
        challenge: await challenges.current(),
        ...(files === undefined ? {} : { files }),
      });
      return true;
    }
    // A stored entry is never sent again, whatever its answer holds.
    if (status === 200) {
      event('jfb:submitted');
      showOutcome(form, templates, go, answer.outcome ?? {}, answer.receipt?.reference);
      return true;
    }
    if (status === 422 && Array.isArray(answer.errors) && (await errors.server(answer.errors))) return true;
    const code = noticeOf(status, answer.code) ?? (posts ? null : 'server_error');
    if (code === null) return false;
    event('jfb:error', code);
    notice = templates.content.querySelector(`[data-jfb-notice="${code}"]`)?.cloneNode(true) as Element | undefined;
    if (!(notice instanceof HTMLElement)) return false;
    wordWait(notice, retryAfter);
    (primary.parentElement ?? primary).before(notice);
    notice.focus();
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
 * none) and the reference; a redirect after it counts down, at least for ten seconds, with "Go now" and "Stay here"
 * (WCAG 2.2.1), or with no delay of its own waits at "Go now", as after a post; a redirect without one goes at once.
 */
function showOutcome(
  form: HTMLFormElement,
  templates: HTMLTemplateElement,
  go: (address: string) => void,
  outcome: Outcome,
  reference?: string,
): void {
  const document = form.ownerDocument;
  const url = addressOf(outcome.url, document);
  const delay = outcome.delaySeconds ?? 0;
  if (url !== null && outcome.kind === 'redirect') return go(url);
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
  const stay = shown.querySelector('[data-jfb-stay]');
  if (url !== null && link !== null) link.href = url;
  if (url === null || link === null) link?.parentElement?.remove();
  if (url === null || link === null || count === null || delay <= 0) {
    count?.remove();
    stay?.remove();
  } else {
    let left = Math.max(delay, LEAST_COUNTDOWN);
    let timer: ReturnType<typeof setTimeout> | undefined;
    const tick = (): void => {
      if (left <= 0) return go(url);
      count.textContent = (count.dataset[left === 1 ? 'jfbOne' : 'jfbOther'] ?? '').replace('{0}', String(left));
      left -= 1;
      timer = setTimeout(tick, 1000);
    };
    tick();
    stay?.addEventListener('click', () => {
      clearTimeout(timer);
      count.remove();
      stay.remove();
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
