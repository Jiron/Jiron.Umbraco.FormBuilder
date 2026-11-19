/**
 * Save and continue later (ROADMAP 8.6b, ADR-0163), a chunk of its own that only a form offering it loads. The server
 * writes the control in a template, in the theme's classes and the form's language, and the notices of a link taken up
 * with the form's other notices. A link's token comes from the address's fragment and leaves the address at once; it
 * stays in this tab's storage until the entry is stored, so a refresh finds it again: the only use of browser storage
 * (section 4.10), and a browser that refuses it only loses that. A saving's answer names its draft the same way, so the
 * entry that follows in the same tab takes that draft away. Files, consents and sensitive answers do not come back,
 * and the form asks for them as it would.
 */
import { proto } from '@jfb/logic/proto';
import type { Captcha } from './protect-chunk';
import type { FormModel } from './model';
import { allFieldsOf, readPage } from './page';
import { valuesOf, type Challenges } from './send';

/** Where a link's token stands in an address, and in a tab's storage before the form's key. */
export const RESUME = 'jfb-resume';

/** The codes of the notices of a link taken up, as the server writes them. */
export const RESTORED = 'resume_restored';
export const EXPIRED = 'resume_expired';
export const SAVED = 'resume_saved';

const BUSY = 'data-jfb-busy';

/** On a form the server wrote from a post, which holds the posted answers. */
const POSTED = 'data-jfb-posted';

/** The hidden input that names the draft to a post without the runtime's own sending. */
const POST_NAME = 'jfb-resume';

/** The name an "Other" text had, kept on it while its choice is not chosen (the fields chunk's mark). */
const OTHER_NAME = 'data-jfb-name';

/** How long an answer waits for the options its field asks for, in milliseconds. */
const OPTIONS_WAIT = 10_000;

type Answers = Readonly<Record<string, readonly string[] | readonly Readonly<Record<string, readonly string[]>>[]>>;

/** What a link's token brought back: the form's answers so far and the page to go on from. */
interface Draft {
  readonly form: string;
  readonly pageKey?: string;
  readonly values: Answers;
}

/** A draft found, one gone (used, or its time over), or no answer at all. */
type Lookup = Draft | 'gone' | null;

/** The token of the draft a form holds, which its send and its next saving name; null while it holds none. */
interface Held {
  get(): string | null;
  set(token: string | null): void;
}

export interface ResumeOptions {
  readonly api: string;
  readonly challenges: Challenges;
  readonly captcha: () => Promise<Captcha | undefined> | undefined;
  /** The token of the draft this form holds, which its send names, or null once it has none. */
  readonly held: (token: string | null) => void;
  /** Shows a page of the form that has several. */
  readonly reveal?: (element: Element) => void;
  /** A form put in another's place, which carries that one's answers: its draft's are not taken up again. */
  readonly carried?: boolean;
}

const storage = (): Storage | null => {
  try {
    return sessionStorage;
  } catch {
    return null;
  }
};

const stored = (form: string): string | null => {
  try {
    return storage()?.getItem(`${RESUME}:${form}`) ?? null;
  } catch {
    return null;
  }
};

const keep = (form: string, token: string | null): void => {
  try {
    if (token === null) storage()?.removeItem(`${RESUME}:${form}`);
    else storage()?.setItem(`${RESUME}:${form}`, token);
  } catch {
    // The tab keeps the token in memory; only a refresh loses it.
  }
};

// The token a link brought, read once for every form of the page: the first read takes it out of the address.
let brought: { readonly token: string | null; claimed: boolean } | undefined;

/** The token the address brought, which only the first form that asks takes up when it is gone. */
function fromAddress(location: Location): { readonly token: string | null; claimed: boolean } {
  if (brought !== undefined) return brought;
  const parts = location.hash.replace(/^#/, '').split('&');
  const named = parts.find((part) => part.startsWith(`${RESUME}=`));
  brought = {
    token: named === undefined ? null : decodeURIComponent(named.slice(RESUME.length + 1)) || null,
    claimed: false,
  };
  if (named !== undefined) {
    const rest = parts.filter((part) => part !== named).join('&');
    history.replaceState(history.state, '', location.pathname + location.search + (rest ? `#${rest}` : ''));
  }
  return brought;
}

// One lookup a token, which every form of the page shares: the server says whose draft it is.
const lookups = new Map<string, Promise<Lookup>>();

function lookUp(api: string, token: string): Promise<Lookup> {
  let lookup = lookups.get(token);
  if (lookup === undefined) {
    lookup = fetch(`${api}/drafts/resume`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    })
      .then(async (answer): Promise<Lookup> => {
        if (answer.ok) return (await answer.json()) as Draft;
        return answer.status === 404 || answer.status === 410 ? 'gone' : null;
      })
      .catch(() => null);
    lookups.set(token, lookup);
  }
  return lookup;
}

type Control = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

const named = (form: HTMLFormElement, name: string): Control[] => [
  ...proto(form).querySelectorAll<Control>(`[name="${CSS.escape(name)}"]`),
];

/**
 * Gives the controls of a name their answers, and says which texts no control took. A text no choice holds goes to the
 * field's "Other": its choice posts no value of its own, and its text has a name only while that choice is chosen.
 */
function fill(form: HTMLFormElement, name: string, texts: readonly string[]): string[] {
  const left = new Set(texts);
  for (const control of named(form, name)) {
    if (control instanceof HTMLSelectElement) {
      for (const option of control.options) {
        option.selected = texts.includes(option.value);
        if (option.selected) left.delete(option.value);
      }
    } else if (control instanceof HTMLInputElement && (control.type === 'checkbox' || control.type === 'radio')) {
      control.checked = texts.includes(control.value);
      if (control.checked) left.delete(control.value);
    } else if (control.type !== 'hidden' && control.type !== 'file') {
      control.value = texts[0] ?? '';
      left.clear();
      // As typing does, so what follows the text, a counter or a box that grows, follows it.
      control.dispatchEvent(new Event('input', { bubbles: true }));
    }
    control.dispatchEvent(new Event('change', { bubbles: true }));
  }
  const [other] = left;
  if (other === undefined || name.endsWith('.other')) return [...left];
  const choice = named(form, name).find(
    (control): control is HTMLInputElement =>
      control instanceof HTMLInputElement &&
      (control.type === 'checkbox' || control.type === 'radio') &&
      control.value === '',
  );
  if (choice === undefined) return [...left];
  choice.checked = true;
  choice.dispatchEvent(new Event('change', { bubbles: true }));
  const text = proto(form).querySelector<HTMLInputElement>(`input[${OTHER_NAME}="${CSS.escape(`${name}.other`)}"]`);
  if (text !== null && !text.hasAttribute('name')) text.name = `${name}.other`;
  fill(form, `${name}.other`, [other]);
  return [];
}

/**
 * An answer to a field whose options follow other answers: the options come a moment after those answers do, so what
 * no control took yet is given again as they are written, until every text is taken or the wait is over.
 */
function fillAsOptionsCome(form: HTMLFormElement, field: Element, name: string, texts: readonly string[]): void {
  if (field.querySelector('template[data-jfb-cascade]') === null) return;
  const watch = new MutationObserver(() => {
    if (fill(form, name, texts).length === 0) stop();
  });
  const timer = setTimeout(() => stop(), OPTIONS_WAIT);
  const stop = (): void => {
    watch.disconnect();
    clearTimeout(timer);
  };
  watch.observe(field, { childList: true, subtree: true });
}

/** Empties the controls of a field the draft holds no answer of, unless the rules have turned them off. */
function clear(form: HTMLFormElement, name: string): void {
  const prefix = `${name}.`;
  for (const control of proto(form).querySelectorAll<Control>('input[name], select[name], textarea[name]')) {
    if ((control.name !== name && !control.name.startsWith(prefix)) || control.disabled) continue;
    if (control instanceof HTMLSelectElement) {
      const chosen = [...control.options].filter((option) => option.selected && option.value !== '');
      if (chosen.length === 0) continue;
      for (const option of control.options) option.selected = !control.multiple && option.value === '';
    } else if (control instanceof HTMLInputElement && (control.type === 'checkbox' || control.type === 'radio')) {
      if (!control.checked) continue;
      control.checked = false;
    } else if (control.type !== 'hidden' && control.type !== 'file') {
      if (control.value === '') continue;
      control.value = '';
      control.dispatchEvent(new Event('input', { bubbles: true }));
    } else continue;
    control.dispatchEvent(new Event('change', { bubbles: true }));
  }
}

/**
 * Gives a form a draft's answers, with the rows its repeatable groups need. A field the draft names without an answer
 * was left empty when it was saved: once the rules have followed the answers, what the page starts it with (a default,
 * a prefill) goes too, unless they turned the field off.
 */
export function restore(form: HTMLFormElement, model: FormModel, values: Answers): void {
  const fields = allFieldsOf(model);
  const elementOf = (alias: string, row?: number): Element | null => {
    const key = fields.find((field) => field.alias === alias)?.key ?? '';
    const found = [...proto(form).querySelectorAll(`[data-jfb-field="${CSS.escape(key)}"]`)];
    return (
      (row === undefined
        ? found[0]
        : found.find((each) => each.closest<HTMLElement>('[data-jfb-row]')?.dataset['jfbRow'] === String(row))) ?? null
    );
  };
  const give = (name: string, texts: readonly string[], element: Element | null): void => {
    const left = fill(form, name, texts);
    if (left.length > 0 && element !== null) fillAsOptionsCome(form, element, name, texts);
  };
  const empty: string[] = [];
  for (const [name, answer] of Object.entries(values)) {
    if (answer.length === 0) {
      empty.push(name);
      continue;
    }
    if (typeof answer[0] === 'string') {
      give(name, answer as readonly string[], elementOf(name.split('.')[0] ?? name));
      continue;
    }
    const box = elementOf(name)?.querySelector('.jfb-repeater') ?? null;
    (answer as readonly Readonly<Record<string, readonly string[]>>[]).forEach((row, at) => {
      if (box !== null && box.querySelectorAll(':scope > [data-jfb-row]').length <= at)
        box.querySelector<HTMLButtonElement>(':scope > button[value^="add"]')?.click();
      for (const [child, texts] of Object.entries(row))
        give(`${name}[${String(at)}].${child}`, texts, elementOf(child.split('.')[0] ?? child, at));
    });
  }
  proto(form).dispatchEvent(new Event('input', { bubbles: true }));
  if (empty.length === 0) return;
  setTimeout(() => {
    for (const name of empty) clear(form, name);
    proto(form).dispatchEvent(new Event('input', { bubbles: true }));
  });
}

/**
 * Shows a notice of the form's templates above its pages, as the form's other notices show, and puts the focus on it:
 * a moment later, after the focus a row added for the answers takes for itself.
 */
function showNotice(form: HTMLFormElement, code: string): void {
  const notice = proto(form)
    .querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]')
    ?.content.querySelector(`[data-jfb-notice="${code}"]`)
    ?.cloneNode(true);
  if (!(notice instanceof HTMLElement)) return;
  proto(form).insertBefore(notice, proto(form).querySelector(':scope > [data-jfb-page]'));
  notice.focus();
  setTimeout(() => {
    if (notice.isConnected) notice.focus();
  });
}

/** The last day a link works, in the form's language; the server's instant as it is where the language is unknown. */
function dayOf(instant: string, culture: string): string {
  const day = new Date(instant);
  if (Number.isNaN(day.getTime())) return '';
  try {
    return new Intl.DateTimeFormat(culture, { dateStyle: 'long' }).format(day);
  } catch {
    return day.toLocaleDateString();
  }
}

/** Puts the link button with the form's buttons, and shows the panel in their place when it is pressed. */
function offer(form: HTMLFormElement, model: FormModel, options: ResumeOptions, held: Held): void {
  const key = proto(form).dataset['jfbForm'] ?? '';
  const template = proto(form).querySelector<HTMLTemplateElement>(':scope > template[data-jfb-resume]');
  const nav = proto(form).querySelector<HTMLElement>(':scope > .jfb-navigation');
  if (template === null || nav === null) return;
  const words = template.dataset;
  const parts = template.content.cloneNode(true) as DocumentFragment;
  const open = parts.querySelector<HTMLButtonElement>('[data-jfb-save-open]');
  const panel = parts.querySelector<HTMLElement>('[data-jfb-save]');
  const email = panel?.querySelector<HTMLInputElement>('input[type="email"]');
  const error = panel?.querySelector<HTMLElement>(`#${CSS.escape(email?.getAttribute('aria-describedby') ?? '')}`);
  const send = panel?.querySelector<HTMLButtonElement>('[data-jfb-save-send]');
  const cancel = panel?.querySelector<HTMLButtonElement>('[data-jfb-save-close]');
  if (!open || !panel || !email || !error || !send || !cancel) return;
  const live = proto(form).querySelector<HTMLElement>(':scope > [data-jfb-live]');
  const say = (text: string | undefined): void => {
    if (live !== null) live.textContent = text ?? '';
  };
  const tell = (text: string | undefined): void => {
    error.textContent = text ?? '';
    error.hidden = !text;
  };
  const saved = (): Element | null => proto(form).querySelector(`:scope > [data-jfb-notice="${SAVED}"]`);
  // The panel takes the place of the buttons while it is open, and gives it back as it closes.
  const show = (shown: boolean): void => {
    panel.hidden = !shown;
    nav.hidden = shown;
    if (!shown) return open.focus();
    saved()?.remove();
    email.focus();
  };
  const refuse = (): void => {
    email.setAttribute('aria-invalid', 'true');
    tell(words['jfbInvalid']);
    email.focus();
  };
  email.addEventListener('input', () => {
    email.removeAttribute('aria-invalid');
    tell('');
  });

  const save = async (): Promise<void> => {
    if (send.hasAttribute(BUSY)) return;
    const address = email.value.trim();
    if (address === '' || !email.checkValidity()) return refuse();
    tell('');
    send.setAttribute(BUSY, '');
    send.setAttribute('aria-disabled', 'true');
    say(words['jfbSaving']);
    let guard: Captcha | undefined;
    try {
      if ((await options.challenges.current()) === null) await options.challenges.again();
      guard = await options.captcha();
      const captchaToken = await guard?.token();
      // The captcha says what the visitor still has to do, and takes the focus there.
      if (captchaToken === null && guard?.ask() === true) return say('');
      const reading = readPage(form, model);
      const honeypot = proto(form).querySelector<HTMLInputElement>(':scope > .jfb-hp input')?.value;
      const pageKey = proto(form).querySelector<HTMLElement>('[data-jfb-page]:not([hidden])')?.dataset['jfbPage'];
      const ask = async (): Promise<Response> => {
        const challenge = (await options.challenges.current()) ?? undefined;
        const puzzle = options.challenges.puzzle();
        const proofOfWork =
          puzzle === undefined ? undefined : await import('./protect-chunk').then((chunk) => chunk.solve(puzzle));
        return fetch(`${options.api}/forms/${key}/drafts`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            revision: model.revision,
            email: address,
            culture: model.culture,
            contentKey: proto(form).dataset['jfbContent'] || undefined,
            pageKey,
            values: valuesOf(reading.post),
            resume: held.get() ?? undefined,
            challenge,
            proofOfWork: proofOfWork ?? undefined,
            captchaToken: captchaToken ?? undefined,
            honeypot: honeypot || undefined,
          }),
        });
      };
      let response = await ask();
      if (response.status === 401) {
        await options.challenges.renew();
        response = await ask();
      }
      // The server has checked the captcha's token, which counts once: the form's send asks for a new one.
      guard?.spent();
      if (!response.ok) {
        say('');
        if (response.status === 422) return refuse();
        tell(words['jfbFailed']);
        return say(words['jfbFailed']);
      }
      const answer = (await response.json()) as { expiresUtc?: string; resume?: string };
      // The new draft takes the old one's place, whose link no longer works: the tab names the new one from now on, so
      // the entry sent from it takes the draft away, and a saving after it takes its place.
      keep(key, answer.resume ?? null);
      held.set(answer.resume ?? null);
      say('');
      panel.hidden = true;
      nav.hidden = false;
      const notice = proto(form)
        .querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]')
        ?.content.querySelector(`[data-jfb-notice="${SAVED}"]`)
        ?.cloneNode(true);
      if (notice instanceof HTMLElement) {
        const line = notice.querySelector<HTMLElement>('[data-jfb-saved]');
        if (line !== null)
          line.textContent = (line.dataset['jfbSaved'] ?? '')
            .split('{0}')
            .join(address)
            .split('{1}')
            .join(dayOf(answer.expiresUtc ?? '', model.culture));
        nav.before(notice);
        notice.focus();
      } else open.focus();
    } catch {
      say(words['jfbFailed']);
      tell(words['jfbFailed']);
    } finally {
      send.removeAttribute(BUSY);
      send.removeAttribute('aria-disabled');
    }
  };
  open.addEventListener('click', () => {
    show(true);
  });
  cancel.addEventListener('click', () => {
    show(false);
  });
  send.addEventListener('click', () => void save());
  // Enter in the address saves, and never sends the form; Escape closes the panel.
  panel.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      show(false);
    } else if (event.key === 'Enter' && event.target === email) {
      event.preventDefault();
      void save();
    }
  });
  panel.hidden = true;
  nav.append(open);
  nav.before(panel);
}
/** Takes up the draft a link or this tab names, if it is this form's: its answers, its page and the notice. */
async function takeUp(form: HTMLFormElement, model: FormModel, options: ResumeOptions, held: Held): Promise<void> {
  const key = proto(form).dataset['jfbForm'] ?? '';
  const address = fromAddress(proto(form).ownerDocument.location);
  const own = stored(key);
  const token = address.token ?? own;
  if (token === null) return;
  // A form the server wrote from a post, or one that came in another's place, holds its answers already: the draft is
  // only named, so the entry takes it away, and nothing is written over what the visitor gave since.
  if (proto(form).hasAttribute(POSTED) || options.carried === true) {
    keep(key, token);
    held.set(token);
    return;
  }
  const found = await lookUp(options.api, token);
  if (found === null) {
    // No answer: the tab keeps the token, so a refresh asks again.
    keep(key, token);
    return;
  }
  if (found === 'gone') {
    // A link says so, to the first form that asked. What the tab held goes quietly: its entry was sent, or its time
    // is over, and the visitor followed no link just now.
    const told = address.token === token && !address.claimed;
    if (address.token === token) address.claimed = true;
    if (token === own) keep(key, null);
    if (told) showNotice(form, EXPIRED);
    return;
  }
  // The link of another form on this page: that form takes it up.
  if (found.form !== key) {
    if (token === own) keep(key, null);
    return;
  }
  keep(key, token);
  held.set(token);
  restore(form, model, found.values);
  const page =
    found.pageKey === undefined ? null : proto(form).querySelector(`[data-jfb-page="${CSS.escape(found.pageKey)}"]`);
  if (page !== null) options.reveal?.(page);
  showNotice(form, RESTORED);
}

/**
 * Offers the control, and takes up a draft that the address or the tab names in the background, so the form is ready
 * while its answers are on their way.
 */
export function enhanceResume(form: HTMLFormElement, model: FormModel, options: ResumeOptions): void {
  const key = proto(form).dataset['jfbForm'] ?? '';
  let token: string | null = null;
  const held: Held = {
    get: () => token,
    set: (next) => {
      token = next;
      options.held(next);
      // A send that posts the form names the draft too, in a hidden input of the form's own.
      const named = proto(form).querySelector<HTMLInputElement>(`:scope > input[type="hidden"][name="${POST_NAME}"]`);
      if (next === null) return named?.remove();
      const input = named ?? proto(form).ownerDocument.createElement('input');
      input.type = 'hidden';
      input.name = POST_NAME;
      input.value = next;
      if (named === null) proto(form).append(input);
    },
  };
  // The entry is stored: the tab forgets its draft, which went with it.
  proto(form).addEventListener('jfb:submitted', () => {
    keep(key, null);
  });
  offer(form, model, options, held);
  void takeUp(form, model, options, held);
}
