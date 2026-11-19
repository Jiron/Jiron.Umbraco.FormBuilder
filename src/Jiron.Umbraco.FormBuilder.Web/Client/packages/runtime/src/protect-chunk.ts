/**
 * What protects a form on the page (ROADMAP 7.2c, ADR-0141, ADR-0142, ADR-0157), in one file with no imports, which starts
 * itself as the worker that solves the puzzle.
 *
 * The puzzle: a number whose SHA-256 with the challenge's nonce starts with so many zero bits. A page that cannot start a
 * worker, as one whose runtime comes from another origin, looks in slices between its other work. The hash is written here for
 * what it is asked, one block of ASCII (a nonce of 32 characters, a colon and a number of at most 20 digits), because a
 * browser's own is only for secure pages and takes far longer per hash.
 *
 * The captcha: nothing of its provider loads before consent, in the mode the site chose, and consent lasts the page, as nothing
 * is stored. Every mode comes to `consent.grant('captcha')`: the notice's button or a consent field in `explicit`, the
 * visitor's choice for the site's category in `cookiebot`, the site's consent manager in `cmp`, and the form itself in
 * `implicit`. Once granted the script loads, once per page, and shows its check in the form's place for it; a score-based
 * provider shows none and is asked at the send. Withdrawn, the check goes.
 */
import type { MessageWords } from '@jfb/logic';
import { proto } from '@jfb/logic/proto';

/** A challenge's puzzle. */
export interface Puzzle {
  readonly nonce: string;
  readonly bits: number;
}

const ROUND: number[] = [];

for (let candidate = 2; ROUND.length < 64; candidate++) {
  let prime = true;
  for (let divisor = 2; divisor * divisor <= candidate; divisor++) if (candidate % divisor === 0) prime = false;
  if (prime) ROUND.push(((candidate ** (1 / 3) % 1) * 2 ** 32) | 0);
}
const START = [2, 3, 5, 7, 11, 13, 17, 19].map((prime) => ((prime ** (1 / 2) % 1) * 2 ** 32) | 0);
const first = START[0] as number;

const rotate = (word: number, by: number): number => (word >>> by) | (word << (32 - by));
const words = new Int32Array(64);

/** The first 32 bits of the SHA-256 of an ASCII text of at most 55 characters, as an unsigned number. */
export function firstBits(text: string): number {
  words.fill(0, 0, 16);
  for (let place = 0; place < text.length; place++) {
    words[place >> 2] = (words[place >> 2] as number) | (text.charCodeAt(place) << (24 - (place % 4) * 8));
  }
  words[text.length >> 2] = (words[text.length >> 2] as number) | (0x80 << (24 - (text.length % 4) * 8));
  words[15] = text.length * 8;
  for (let at = 16; at < 64; at++) {
    const one = words[at - 15] as number;
    const two = words[at - 2] as number;
    words[at] =
      (words[at - 16] as number) +
      (rotate(one, 7) ^ rotate(one, 18) ^ (one >>> 3)) +
      (words[at - 7] as number) +
      (rotate(two, 17) ^ rotate(two, 19) ^ (two >>> 10));
  }
  let [a, b, c, d, e, f, g, h] = START as [number, number, number, number, number, number, number, number];
  for (let at = 0; at < 64; at++) {
    const one =
      (h +
        (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) +
        ((e & f) ^ (~e & g)) +
        (ROUND[at] as number) +
        (words[at] as number)) |
      0;
    const two = ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
    h = g;
    g = f;
    f = e;
    e = (d + one) | 0;
    d = c;
    c = b;
    b = a;
    a = (one + two) | 0;
  }
  return (a + first) >>> 0;
}

/** The first number from `from` on, among `count`, that solves the puzzle; null for none. */
export function search({ nonce, bits }: Puzzle, from: number, count: number): number | null {
  for (let number = from; number < from + count; number++) {
    if (firstBits(`${nonce}:${String(number)}`) >>> (32 - bits) === 0) return number;
  }
  return null;
}

/** The answer to a puzzle, in digits. */
export const solve = (puzzle: Puzzle): Promise<string> =>
  new Promise((resolve) => {
    const inSlices = (from = 0): void => {
      const found = search(puzzle, from, 20_000);
      if (found === null) setTimeout(() => inSlices(from + 20_000));
      else resolve(String(found));
    };
    try {
      const worker = new Worker(import.meta.url, { type: 'module' });
      worker.onmessage = (event: MessageEvent<number>) => {
        worker.terminate();
        resolve(String(event.data));
      };
      worker.onerror = () => inSlices();
      worker.postMessage(puzzle);
    } catch {
      inSlices();
    }
  });

// As a worker: there is no document, and the page posts the puzzle.
if (typeof document === 'undefined' && typeof postMessage === 'function') {
  addEventListener('message', (event) => {
    postMessage(search((event as MessageEvent<Puzzle>).data, 0, 2 ** 40));
  });
}

/** What the challenge tells a page of a form's captcha. */
export interface CaptchaTold {
  readonly provider: string;
  readonly siteKey: string;
  readonly scriptAddress: string;
  /** The global object a site's own provider's script defines (ADR-0234); none for the built-in providers. */
  readonly global?: string;
  readonly action?: string;
  readonly consent: string;
  readonly category?: string;
}

/** A form's captcha, as a send needs it. */
export interface Captcha {
  /** The token to send; null when the visitor has yet to agree or to pass the check, or the provider failed. */
  token(): Promise<string | null | undefined>;
  /**
   * Says what the visitor still has to do and takes the focus there. False when nothing they can do brings a token, as the
   * provider's script did not load or a score was too low: such a send goes the way that keeps its answers.
   */
  ask(): boolean;
  /** A send that took the token was refused, so the provider asks again. */
  spent(): void;
}

/** What the providers' scripts offer. */
interface Provider {
  ready?(run: () => void): void;
  render(place: HTMLElement, options: Readonly<Record<string, unknown>>): unknown;
  reset(widget?: unknown): void;
  execute?(key: string, options: { action?: string | undefined }): Promise<string>;
}

interface ProviderWindow {
  grecaptcha?: Provider;
  hcaptcha?: Provider;
  Cookiebot?: { consent?: Readonly<Record<string, boolean>>; renew?(): void };
}

const scripts = new Map<string, Promise<void>>();

/**
 * Gives a form its captcha.
 * @param agreeing The control of a consent field that says it agrees to the check, as the form's model names it.
 */
export function enhanceCaptcha(
  form: HTMLFormElement,
  culture: string,
  told: CaptchaTold,
  texts: MessageWords,
  agreeing: HTMLInputElement | null,
): Captcha {
  const document = proto(form).ownerDocument;
  const view = document.defaultView as unknown as ProviderWindow & Window;
  const consent = view.JironFormBuilder?.consent;
  const provider = (): Provider | undefined =>
    told.global !== undefined
      ? (view as unknown as Readonly<Record<string, Provider | undefined>>)[told.global]
      : told.provider === 'hcaptcha'
        ? view.hcaptcha
        : view.grecaptcha;
  const scored = told.action !== undefined;
  const ticked = told.consent === 'explicit' ? agreeing : null;
  const agreed = (): boolean =>
    told.consent === 'implicit' ||
    (told.consent === 'cookiebot'
      ? view.Cookiebot?.consent?.[told.category ?? ''] === true
      : ticked?.checked === true || consent?.has('captcha') === true);
  const nav = proto(form).querySelector('.jfb-navigation');
  const box = proto(form).querySelector<HTMLElement>('.jfb-captcha') ?? document.createElement('div');
  if (!box.isConnected) {
    box.className = 'jfb-captcha';
    nav?.before(box);
  }
  box.tabIndex = -1;

  const line = (text: string): HTMLElement => {
    const element = document.createElement('p');
    element.textContent = text;
    return element;
  };
  const button = (text: string, pressed: () => void): HTMLElement => {
    const element = document.createElement('button');
    element.type = 'button';
    element.className = proto(form).querySelector('[data-jfb-nav]')?.className ?? 'jfb-button';
    element.textContent = text;
    element.onclick = pressed;
    return element;
  };
  const load = (): HTMLElement => button(texts.captchaLoad, () => consent?.grant('captcha'));
  // A category visitors are asked about is named; necessary, which Cookiebot gives on every page it runs on (ADR-0199),
  // is not, and the notice says only that the cookie settings load the check.
  const category = texts.captchaCategories[told.category ?? ''];
  /** What the place says while the visitor has yet to agree; a consent field says it itself. */
  const notice = (): HTMLElement[] =>
    ticked !== null
      ? []
      : told.consent === 'cookiebot'
        ? [
            line(category ? texts.captchaCookies.replace('{category}', category) : texts.captchaManager),
            button(texts.captchaCookieSettings, () => view.Cookiebot?.renew?.()),
          ]
        : told.consent === 'cmp'
          ? [line(texts.captchaManager)]
          : [line(texts.captchaNotice), load()];

  /** The provider's script, once per page, and its readiness. */
  const ready = (): Promise<void> => {
    let loaded = scripts.get(told.scriptAddress);
    if (loaded === undefined) {
      loaded = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        const url = new URL(told.scriptAddress);
        url.searchParams.set('hl', culture);
        script.src = url.href;
        script.async = true;
        script.nonce = document.querySelector<HTMLScriptElement>('script[nonce]')?.nonce ?? '';
        script.onload = () => resolve();
        script.onerror = reject;
        document.head.append(script);
      });
      scripts.set(told.scriptAddress, loaded);
    }
    return loaded.then(
      () =>
        new Promise((resolve) => {
          const ready = provider()?.ready;
          if (ready === undefined) resolve();
          else ready(resolve);
        }),
    );
  };

  let token: string | null = null;
  let widget: unknown;
  let shown = false;
  let failed = false;
  const follow = (): void => {
    // A form that another came in place of.
    if (!proto(form).isConnected) return;
    if (!agreed()) {
      token = null;
      shown = false;
      failed = false;
      box.replaceChildren(...notice());
    } else if (!shown) {
      shown = true;
      failed = false;
      box.replaceChildren();
      void ready()
        .then(() => {
          if (scored || !shown) return;
          const holder = document.createElement('div');
          box.replaceChildren(holder);
          widget = provider()?.render(holder, {
            sitekey: told.siteKey,
            callback: (given: string) => {
              token = given;
              box.querySelector('.jfb-error')?.remove();
            },
            'expired-callback': () => {
              token = null;
            },
          });
        })
        .catch(() => {
          scripts.delete(told.scriptAddress);
          shown = false;
          failed = true;
          box.replaceChildren(line(texts.captchaFailed));
        });
    }
  };

  document.addEventListener('jfb:consent', follow);
  ticked?.addEventListener('change', follow);
  if (told.consent === 'cookiebot') {
    for (const type of ['CookiebotOnConsentReady', 'CookiebotOnAccept', 'CookiebotOnDecline'])
      view.addEventListener(type, follow);
  }
  follow();

  return {
    token: async () =>
      !scored || !agreed()
        ? token
        : ready()
            .then(() => provider()?.execute?.(told.siteKey, { action: told.action }) ?? null)
            .catch(() => null),
    ask() {
      if (failed || (scored && agreed())) return false;
      box.querySelector('.jfb-error')?.remove();
      const said = line(texts.captchaMissing);
      said.className = 'jfb-error';
      said.setAttribute('role', 'alert');
      box.prepend(said);
      // A place on a page the visitor is not on, or that a rule hid, comes to the buttons they are at.
      if (box.getClientRects().length === 0) nav?.before(box);
      const reachable = ticked !== null && !ticked.disabled && ticked.getClientRects().length > 0;
      if (ticked !== null && !reachable && !agreed()) box.append(load());
      (agreed() ? box : reachable ? ticked : (box.querySelector('button') ?? box)).focus();
      return true;
    },
    spent() {
      token = null;
      if (!scored && shown) provider()?.reset(widget);
    },
  };
}
