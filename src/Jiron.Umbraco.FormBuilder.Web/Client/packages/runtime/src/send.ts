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
import type { Context, PostAnswer, ReadPost } from '@jfb/logic';
import type { PageErrors, ServerProblem } from './errors';
import { hostOf, markupOf, swapForm } from './changed';
import { parseSanitised } from './html';
import { wordsFor } from './messages';
import type { Places } from './places';
import type { Captcha, CaptchaTold, Puzzle } from './protect-chunk';
import { optionsByPost, type PageUploads, type UploadedFile } from './uploads';
import type { FormModel } from './model';
import { readPage } from './page';
import { contextOf } from './rules';

/** What the send events tell a site: the form, and for `jfb:error` the send's code; never an answer. */
export interface SendDetail {
  readonly key: string;
  readonly alias: string;
  readonly code?: string;
}

/**
 * Asks the server to check one page before the runtime moves on (ROADMAP 6.5d, ADR-0155): `on` when it passes, `answered` when
 * the page shows why not, `post` to leave the press to the post.
 */
export type CheckPage = (
  button: HTMLButtonElement,
  errors: PageErrors,
  page: string,
) => Promise<'on' | 'answered' | 'post'>;

/** Sends a form whose answers pass the page's checks: true when the page answered the press, false to post it. */
export interface Deliver {
  (button: HTMLButtonElement, errors: PageErrors): Promise<boolean>;
  /** A form that sends on the page checks its pages this way. */
  readonly checkPage?: CheckPage;
}

/** The entries endpoint's outcome of an entry, as far as the page shows it (ADR-0088). */
interface Outcome {
  readonly kind?: string;
  readonly html?: string;
  readonly url?: string;
  readonly delaySeconds?: number;
  /** The token that the render endpoint takes for the copy of the answers (ADR-0155). */
  readonly copy?: string;
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
/** How long a puzzle takes before the page says it is looking for the answer. */
const SLOW = 1_500;
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
  extra: {
    content?: string;
    challenge?: string;
    honeypot?: string;
    placement?: string;
    instance?: string;
    captchaToken?: string;
    proofOfWork?: string;
  } = {},
): object {
  const values = valuesOf(post);
  return {
    submissionId: id,
    instance: extra.instance,
    revision: model.revision,
    culture: model.culture,
    contentKey: extra.content,
    values,
    query,
    context: Object.fromEntries(post.context),
    challenge: extra.challenge,
    captchaToken: extra.captchaToken,
    proofOfWork: extra.proofOfWork,
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
  /** The form's captcha, if the challenge named one by now (ADR-0157). */
  readonly captcha?: () => Promise<Captcha | undefined> | undefined;
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
  /** Asks only to hear what the answer tells now, and keeps the one it holds. */
  listen(): void;
  /** The puzzle of the one it holds, which a send solves (ADR-0141). */
  puzzle(): Puzzle | undefined;
}

/** What the challenge's answer holds besides the challenge: what cached markup cannot carry (ADR-0138, ADR-0156). */
export interface Told {
  readonly places?: Places;
  readonly member?: Context['member'];
  readonly captcha?: CaptchaTold;
}

/**
 * A form's challenge: asked for now, unless it came with one. `hear` takes what each answer tells; a form that came with
 * its challenge asks once all the same to hear it, and keeps the one it came with, which its files name.
 */
export function challengesOf(
  form: HTMLFormElement,
  api: string,
  given?: string | null,
  hear?: (told: Told) => void,
): Challenges {
  const content = form.dataset['jfbContent'] || undefined;
  let puzzle: Puzzle | undefined;
  const asked = (previous?: string): Promise<(Told & { challenge?: string; proofOfWork?: Puzzle }) | null> =>
    fetch(`${api}/forms/${form.dataset['jfbForm'] ?? ''}/challenge`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ previous, contentKey: content }),
      signal: AbortSignal.timeout(WAIT),
    })
      .then((response) =>
        response.ok ? (response.json() as Promise<Told & { challenge?: string; proofOfWork?: Puzzle }>) : null,
      )
      .then((answer) => {
        if (answer !== null) hear?.(answer);
        return answer;
      })
      .catch(() => null);
  const ask = (previous?: string): Promise<string | null> =>
    asked(previous).then((answer) => {
      puzzle = answer?.proofOfWork;
      return answer?.challenge ?? null;
    });
  let held = given === undefined ? ask() : Promise.resolve(given);
  const listen = (): void => {
    if (hear !== undefined) void asked();
  };
  if (given !== undefined) listen();
  return {
    listen,
    puzzle: () => puzzle,
    current: () => held,
    again: () => (held = ask()),
    renew: async () => {
      const last = await held;
      held = ask(last ?? undefined);
      return held;
    },
  };
}

/** What a third party or a worker is given to answer, which may never come. */
const within = <T>(work: Promise<T> | T): Promise<T | null> =>
  Promise.race([work, new Promise<null>((resolve) => setTimeout(resolve, WAIT, null))]);

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
    !optionsByPost(model) &&
    (options.files !== undefined ||
      (form.enctype !== 'multipart/form-data' && !Object.values(model.fields).some((field) => field.kind === 'files')));
  const posts = form.hasAttribute('action');
  /** Whether the form as it is now can come in this one's place: one with an address, or in the element, which writes it. */
  const comesAnew = posts || hostOf(form) !== null;
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

  /** The theme's notice of a refused or failed asking, just above the buttons, which takes the focus. */
  const showNotice = (primary: Element, code: string, retryAfter: string | null): boolean => {
    notice = templates?.content.querySelector(`[data-jfb-notice="${code}"]`)?.cloneNode(true) as Element | undefined;
    if (!(notice instanceof HTMLElement)) return false;
    wordWait(notice, retryAfter);
    (primary.parentElement ?? primary).before(notice);
    notice.focus();
    return true;
  };

  /** The answers the server refused on the page; one that a full option caused has made the page's numbers old. */
  const refused = (errors: PageErrors, answer: Answer): Promise<boolean> => {
    if (answer.errors?.some((each) => each.code === 'full')) challenges?.listen();
    return errors.server(answer.errors ?? []);
  };

  /** The form as it is now has come in this one's place, and takes over with what this one holds. */
  const handOver = async (swapped: NonNullable<Awaited<ReturnType<typeof swapForm>>>): Promise<void> => {
    swapped.form.dispatchEvent(
      new CustomEvent<SendDetail>('jfb:error', {
        bubbles: true,
        detail: { key, alias: swapped.model.alias, code: 'form_changed' },
      }),
    );
    const files = options.files?.uploaded();
    options.rewake?.(swapped.form, swapped.model, {
      id,
      challenge: (await challenges?.current()) ?? null,
      ...(files === undefined ? {} : { files }),
    });
  };

  const checkPage: CheckPage = async (pressed, errors, page) => {
    if (challenges === null) return 'post';
    notice?.remove();
    // Enter in a field presses the hidden button, which stands for this one.
    const button = form.querySelector<HTMLElement>('[data-jfb-nav="next"]') ?? pressed;
    button.setAttribute(BUSY, '');
    button.setAttribute(DISABLED, 'true');
    let status = 0;
    let retryAfter: string | null = null;
    let answer: Answer = {};
    let swapped: Awaited<ReturnType<typeof swapForm>> = null;
    try {
      // A file that failed on another page has its say when the visitor gets there, or at the send.
      const here = form.querySelector(`[data-jfb-page="${page}"]`) ?? undefined;
      if ((await options.files?.settled(here)) === false) return 'answered';
      const reading = readPage(form, model);
      const query = contextOf(model, reading, Date.now(), form.ownerDocument.URL).query;
      const honeypot = form.querySelector<HTMLInputElement>(':scope > .jfb-hp input')?.value;
      // As a send's: a challenge made now says the form was filled in no time, so a form that can post goes that way.
      if ((await challenges.current()) === null && (await challenges.again()) !== null && posts) return 'post';
      const ask = async (): Promise<Response> => {
        const challenge = (await challenges.current()) ?? undefined;
        return post('validate', {
          ...bodyOf(model, reading.post, query, id, {
            ...(content === undefined ? {} : { content }),
            ...(challenge === undefined ? {} : { challenge }),
            ...(honeypot ? { honeypot } : {}),
          }),
          submissionId: undefined,
          pageKey: page,
        });
      };
      let response = await ask();
      if (response.status === 401) {
        await challenges.renew();
        response = await ask();
      }
      status = response.status;
      retryAfter = response.headers.get('Retry-After');
      answer = ((await response.json().catch(() => null)) as Answer | null) ?? {};
      if (status === 409 && answer.code === 'form_changed' && comesAnew && api !== undefined) {
        swapped = await swapForm(form, model, api);
      }
    } catch (failed) {
      const refused = failed as { status?: number; retryAfter?: string | null };
      if (refused.status !== undefined) {
        status = refused.status;
        retryAfter = refused.retryAfter ?? null;
      }
    } finally {
      button.removeAttribute(BUSY);
      button.removeAttribute(DISABLED);
    }
    if (swapped !== null) {
      await handOver(swapped);
      return 'answered';
    }
    if (status === 204) return 'on';
    if (status === 422 && Array.isArray(answer.errors) && (await refused(errors, answer))) return 'answered';
    const code = noticeOf(status, answer.code) ?? (posts ? null : 'server_error');
    return code !== null && showNotice(button, code, retryAfter) ? 'answered' : 'post';
  };

  const deliver = async (button: HTMLButtonElement, errors: PageErrors): Promise<boolean> => {
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
    let guard: Captcha | undefined;
    let captchaToken: string | null | undefined;
    /** The answer to the puzzle of the challenge held, which a slow device says it is looking for. */
    const solved = async (): Promise<string | undefined> => {
      const puzzle = challenges.puzzle();
      if (puzzle === undefined) return undefined;
      const hint = setTimeout(() => {
        void wordsFor(model.culture).then((words) => {
          if (live !== null && primary.hasAttribute(BUSY)) live.textContent = words.proofHint;
        });
      }, SLOW);
      try {
        return (await within(import('./protect-chunk').then((chunk) => chunk.solve(puzzle)))) ?? undefined;
      } catch {
        return undefined;
      } finally {
        clearTimeout(hint);
        if (live !== null) live.textContent = templates.dataset['jfbSending'] ?? '';
      }
    };
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
        const proofOfWork = await solved();
        return post(
          'entries',
          bodyOf(model, reading.post, query, id, {
            ...(content === undefined ? {} : { content }),
            ...(current === null ? {} : { challenge: current }),
            ...(proofOfWork === undefined ? {} : { proofOfWork }),
            ...(captchaToken === undefined || captchaToken === null ? {} : { captchaToken }),
            ...(honeypot ? { honeypot } : {}),
            ...(placement === undefined ? {} : { placement }),
            // A view's own id names no form the server wrote, so no copy can be asked for it.
            ...(/^jfb-\d+$/.test(form.id) ? { instance: form.id } : {}),
          }),
        );
      };
      // The page asks again when its first asking failed. A challenge made now says the visitor filled the form in no
      // time, which the score holds against them (ADR-0139), so a form that can post goes that way, where no time counts.
      if ((await challenges.current()) === null) {
        const asked = await challenges.again();
        // Not one a post could not pass: it would land in Spam as a visitor with no script.
        handedOver = posts && asked !== null && challenges.puzzle() === undefined && options.captcha?.() === undefined;
      }
      if (!handedOver) {
        guard = await options.captcha?.();
        captchaToken = await within(guard?.token());
        if (captchaToken === null && guard?.ask() === true) return true;
        // A provider no one can pass costs a check in Spam, never the answers.
        handedOver = captchaToken === null && posts;
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
        if (status === 409 && answer.code === 'form_changed' && comesAnew && api !== undefined) {
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
    // The provider's token counts once, whatever the answer was.
    if (status !== 200) guard?.spent();
    if (swapped !== null) {
      await handOver(swapped);
      return true;
    }
    if (status === 403 && answer.code === 'captcha_failed' && guard?.ask() === true) return true;
    // A stored entry is never sent again, whatever its answer holds.
    if (status === 200) {
      event('jfb:submitted');
      showOutcome(form, model, templates, go, answer.outcome ?? {}, answer.receipt?.reference);
      return true;
    }
    if (status === 422 && Array.isArray(answer.errors) && (await refused(errors, answer))) return true;
    const code = noticeOf(status, answer.code) ?? (posts ? null : 'server_error');
    if (code === null) return false;
    event('jfb:error', code);
    return showNotice(primary, code, retryAfter);
  };
  return take ? Object.assign(deliver, { checkPage }) : deliver;
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
  model: FormModel,
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
  const api = form.dataset['jfbApi'];
  if (outcome.copy !== undefined && api !== undefined)
    showCopy(form, model, place, api, outcome.copy).catch(() => undefined);
}

/** The copy of the answers below the outcome (ADR-0123, ADR-0155), as the server writes it for a post; without it the outcome stands alone. */
async function showCopy(
  form: HTMLFormElement,
  model: FormModel,
  place: Element,
  api: string,
  token: string,
): Promise<void> {
  const rendered = await markupOf(form, model, api, token);
  const copy = rendered?.querySelector('[data-jfb-outcome]');
  while (copy?.nextElementSibling) place.append(copy.nextElementSibling);
  const print = place.querySelector<HTMLButtonElement>('[data-jfb-print]');
  if (print !== null) {
    print.hidden = false;
    print.addEventListener('click', () => form.ownerDocument.defaultView?.print());
  }
  // The form again is this very address, which a browser only scrolls to: it takes the page anew, as after a post.
  place.querySelector('[data-jfb-copy] a')?.addEventListener('click', (event) => {
    event.preventDefault();
    const location = form.ownerDocument.defaultView?.location;
    location?.assign((event.currentTarget as HTMLAnchorElement).href);
    location?.reload();
  });
}
