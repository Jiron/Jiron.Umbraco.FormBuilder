/**
 * Save and continue later (ROADMAP 8.6, ADR-0163), a chunk of its own that only a form offering it loads. The server writes the
 * control in a template, in the theme's classes and the visitor's language. A link's token comes from the address's fragment, leaves the
 * address at once, and stays in this tab's storage until the entry is stored, so a refresh finds it again: the only use of browser
 * storage, and a browser that refuses it only loses that. Files and consents are not restored, and the form asks for them as it would.
 */
import type { Captcha } from './protect-chunk';
import type { FormModel } from './model';
import { allFieldsOf, readPage } from './page';
import { contextOf } from './rules';
import { bodyOf, type Challenges } from './send';

/** Where a link's token stands in an address, and in a tab's storage. */
export const RESUME = 'jfb-resume';

type Values = Readonly<Record<string, readonly string[] | readonly Readonly<Record<string, readonly string[]>>[]>>;

export interface ResumeOptions {
  readonly api: string;
  readonly challenges: Challenges;
  readonly captcha: () => Promise<Captcha | undefined> | undefined;
  /** The token of the draft this form holds, which its send names, or null once it has none. */
  readonly held: (token: string | null) => void;
  /** Shows a page of the form that has several. */
  readonly reveal?: (element: Element) => void;
}

const storage = (): Storage | null => {
  try {
    return sessionStorage;
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

/** Gives the controls of a name their answers; a text no choice holds goes to the field's "Other". */
function fill(form: HTMLFormElement, name: string, texts: readonly string[]): void {
  const left = new Set(texts);
  for (const control of form.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
    `[name="${CSS.escape(name)}"]`,
  )) {
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
    }
    control.dispatchEvent(new Event('change', { bubbles: true }));
  }
  const [other] = left;
  if (other !== undefined && !name.endsWith('.other')) fill(form, `${name}.other`, [other]);
}

/** Gives a form a draft's answers, with the rows its repeatable groups need. */
export function restore(form: HTMLFormElement, model: FormModel, values: Values): void {
  for (const [name, answer] of Object.entries(values)) {
    if (typeof answer[0] === 'string') {
      fill(form, name, answer as readonly string[]);
      continue;
    }
    const key = allFieldsOf(model).find((field) => field.alias === name)?.key ?? '';
    const box = form.querySelector(`[data-jfb-field="${CSS.escape(key)}"] .jfb-repeater`);
    (answer as readonly Readonly<Record<string, readonly string[]>>[]).forEach((row, at) => {
      if (box !== null && box.querySelectorAll(':scope > [data-jfb-row]').length <= at)
        box.querySelector<HTMLButtonElement>(':scope > button[value^="add"]')?.click();
      for (const [child, texts] of Object.entries(row)) fill(form, `${name}[${String(at)}].${child}`, texts);
    });
  }
  form.dispatchEvent(new Event('input', { bubbles: true }));
}

/** Offers the control, and takes up a draft that the address or the tab names. */
export async function enhanceResume(form: HTMLFormElement, model: FormModel, options: ResumeOptions): Promise<void> {
  const key = form.dataset['jfbForm'] ?? '';
  const template = form.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-resume]');
  const words = template?.dataset ?? {};
  const nav = form.querySelector('.jfb-navigation');
  const live = form.querySelector<HTMLElement>(':scope > [data-jfb-live]');
  let held: string | null = null;
  const say = (text: string | undefined): void => {
    if (live !== null) live.textContent = text ?? '';
  };

  if (template !== undefined && template !== null && nav !== null) {
    const parts = template.content.cloneNode(true) as DocumentFragment;
    const open = parts.querySelector<HTMLButtonElement>('[data-jfb-save-open]');
    const panel = parts.querySelector<HTMLElement>('[data-jfb-save]');
    const email = parts.querySelector('input');
    const status = parts.querySelector<HTMLElement>('[role="status"]');
    const send = parts.querySelector<HTMLButtonElement>('[data-jfb-save-send]');
    const close = parts.querySelector<HTMLButtonElement>('[data-jfb-save-close]');
    const tell = (text: string | undefined): void => {
      if (status !== null) status.textContent = text ?? '';
    };
    const toggle = (shown: boolean): void => {
      if (panel !== null) panel.hidden = !shown;
      if (open !== null) open.hidden = shown;
      (shown ? email : open)?.focus();
    };
    const save = async (): Promise<void> => {
      if (email === null || send === null) return;
      if (!email.value || !email.checkValidity()) {
        tell(words['jfbInvalid']);
        email.focus();
        return;
      }
      send.disabled = true;
      tell(words['jfbSaving']);
      const reading = readPage(form, model);
      const query = contextOf(model, reading, Date.now(), form.ownerDocument.URL).query;
      const honeypot = form.querySelector<HTMLInputElement>(':scope > .jfb-hp input')?.value;
      const puzzle = options.challenges.puzzle();
      const guard = await options.captcha();
      const ask = async (): Promise<Response> => {
        const challenge = (await options.challenges.current()) ?? undefined;
        const proofOfWork = puzzle === undefined ? null : await import('./protect-chunk').then((chunk) => chunk.solve(puzzle));
        const captchaToken = (await guard?.token()) ?? undefined;
        const content = form.dataset['jfbContent'] || undefined;
        return fetch(`${options.api}/forms/${key}/drafts`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            ...bodyOf(model, reading.post, query, '', {
              ...(content === undefined ? {} : { content }),
              ...(challenge === undefined ? {} : { challenge }),
              ...(proofOfWork === null ? {} : { proofOfWork }),
              ...(captchaToken === undefined ? {} : { captchaToken }),
              ...(honeypot ? { honeypot } : {}),
            }),
            submissionId: undefined,
            resume: held ?? undefined,
            email: email.value,
            pageKey: form.querySelector<HTMLElement>('[data-jfb-page]:not([hidden])')?.dataset['jfbPage'],
          }),
        });
      };
      try {
        let response = await ask();
        if (response.status === 401) {
          await options.challenges.renew();
          response = await ask();
        }
        tell(words[response.ok ? 'jfbSent' : response.status === 422 ? 'jfbInvalid' : 'jfbFailed']);
      } catch {
        tell(words['jfbFailed']);
      } finally {
        send.disabled = false;
        guard?.spent();
      }
    };
    open?.addEventListener('click', () => {
      toggle(true);
    });
    close?.addEventListener('click', () => {
      toggle(false);
    });
    send?.addEventListener('click', () => void save());
    email?.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        void save();
      }
    });
    nav.after(parts);
  }

  const address = form.ownerDocument.location;
  const named = new RegExp(`[#&]${RESUME}=([^&]+)`).exec(address.hash)?.[1];
  const token = named ?? storage()?.getItem(`${RESUME}:${key}`);
  if (token === undefined || token === null) return;
  const answer = await fetch(`${options.api}/drafts/resume`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  }).catch(() => null);
  const found = (answer?.ok ? await answer.json() : null) as { form: string; values: Values; pageKey?: string } | null;
  // The link of another form on this page: that form takes it up.
  if (found !== null && found.form !== key) return;
  if (named !== undefined) history.replaceState(history.state, '', address.pathname + address.search);
  if (found === null) {
    keep(key, null);
    if (answer !== null) say(words['jfbExpired']);
    return;
  }
  keep(key, token);
  held = token;
  options.held(token);
  form.addEventListener('jfb:submitted', () => {
    keep(key, null);
  });
  restore(form, model, found.values);
  const page = found.pageKey === undefined ? null : form.querySelector(`[data-jfb-page="${found.pageKey}"]`);
  if (page !== null) options.reveal?.(page);
  say(words['jfbRestored']);
}
