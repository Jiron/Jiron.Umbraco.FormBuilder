/**
 * A form published again while its visitor filled it in (ROADMAP 6.4j, ADR-0004, ADR-0120): the page asks the `render`
 * endpoint for the form as it is now, in the page's theme, culture and placement, copies the answers over by each
 * control's name, leaves a consent whose wording changed unticked, puts the form in place of the old one and moves the
 * focus to the theme's notice, which says so. The page is never reloaded. The headless element writes the form as it is
 * now itself (ADR-0128). What this cannot carry over, as a form with rows or a ranking, a form without the buttons to move
 * with, a summary page that a post writes and a form that takes no entry now, goes the way without JavaScript, whose answer
 * does the same; in the element, which has no such way, the visitor reads the notice of a failed send.
 */
import { parseSanitised } from './html';
import { movesPages } from './uploads';
import type { FormModel } from './model';

/** How long the page waits for the form as it is now: the endpoint's own limit, and a little. */
const WAIT = 25_000;
/** The input that holds a consent's wording hash, before the field's alias (ADR-0015). */
const CONSENT = 'jfb-consent-';
/** The hidden inputs a page gives its form, which a render from elsewhere does not have (ADR-0104). */
const CARRIED = ['jfb-context-', 'jfb-query-'];
const CONTROLS = 'input[name], select[name], textarea[name]';
const NOT_ANSWERS = ['hidden', 'file', 'submit', 'button', 'reset', 'image'];

type Control = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

const answers = (form: Element): Control[] =>
  [...form.querySelectorAll<Control>(CONTROLS)].filter(
    (control) =>
      control.closest('.jfb-hp') === null &&
      !(control instanceof HTMLInputElement && NOT_ANSWERS.includes(control.type)),
  );

const hash = (form: Element, alias: string): string | undefined =>
  form.querySelector<HTMLInputElement>(`input[type="hidden"][name="${CSS.escape(CONSENT + alias)}"]`)?.value;

/**
 * Copies the old form's answers to the new one by each control's name, and the values the page gave it. A control the
 * new form lacks takes its answer with it, as its question is gone; the answers a default would fill in stay the
 * visitor's. Answers how many ticked consents have new wording, which stay unticked.
 */
export function copyAnswers(from: HTMLFormElement, to: HTMLFormElement): number {
  const old = new Map<string, Control[]>();
  for (const control of answers(from)) old.set(control.name, [...(old.get(control.name) ?? []), control]);
  const seen = new Map<string, number>();
  let unticked = 0;
  for (const control of answers(to)) {
    const same = old.get(control.name);
    if (same === undefined) continue;
    control.closest('[data-jfb-default]')?.removeAttribute('data-jfb-default');
    if (control instanceof HTMLSelectElement) {
      const chosen = new Set(
        same
          .flatMap((each) => (each instanceof HTMLSelectElement ? [...each.selectedOptions] : []))
          .map((o) => o.value),
      );
      for (const option of control.options) option.selected = chosen.has(option.value);
    } else if (control instanceof HTMLInputElement && (control.type === 'checkbox' || control.type === 'radio')) {
      const ticked = same.some(
        (each) => each instanceof HTMLInputElement && each.checked && each.value === control.value,
      );
      const before = hash(from, control.name);
      const changed = before !== undefined && before !== hash(to, control.name);
      control.checked = ticked && !changed;
      if (ticked && changed) unticked += 1;
    } else {
      const at = seen.get(control.name) ?? 0;
      seen.set(control.name, at + 1);
      const each = same[at];
      if (each !== undefined) control.value = each.value;
    }
  }
  for (const input of [...to.querySelectorAll<HTMLInputElement>('input[type="hidden"]')]) {
    if (CARRIED.some((prefix) => input.name.startsWith(prefix))) input.remove();
  }
  const live = to.querySelector(':scope > [data-jfb-live]');
  for (const input of from.querySelectorAll<HTMLInputElement>('input[type="hidden"]')) {
    if (CARRIED.some((prefix) => input.name.startsWith(prefix))) to.insertBefore(input.cloneNode(), live);
  }
  return unticked;
}

/**
 * The model data of the form as it is now. The frame reads a page's with its own check, which a value import would bring
 * the logic into the core with it; this one comes from the server of the same markup contract, so it only has to parse.
 */
function modelIn(next: HTMLFormElement, form: HTMLFormElement): FormModel | null {
  if (next.dataset['jfbContract'] !== form.dataset['jfbContract']) return null;
  try {
    return JSON.parse(
      next.querySelector('script[type="application/json"][data-jfb-model]')?.textContent ?? '',
    ) as FormModel;
  } catch {
    return null;
  }
}

/** The headless element a form is in, which writes the form as it is now itself (ADR-0128). */
export const hostOf = (form: Element): { fresh(): Promise<HTMLFormElement | null> } | null =>
  form.closest('jfb-form') as { fresh(): Promise<HTMLFormElement | null> } | null;

/** The form as it is now, with its model, or null for what the page cannot put in the old one's place. */
export async function markupOf(
  form: HTMLFormElement,
  model: FormModel,
  api: string,
  sent?: string,
): Promise<DocumentFragment | null> {
  const query = new URLSearchParams({
    instance: form.id,
    page: form.ownerDocument.location.pathname,
    culture: model.culture,
  });
  const content = form.dataset['jfbContent'];
  if (content) query.set('contentKey', content);
  const theme = [...form.classList].find((name) => name.startsWith('jfb-theme-'))?.slice('jfb-theme-'.length);
  if (theme) query.set('theme', theme);
  const placement = form.dataset['jfbPlacement'];
  if (placement) query.set('placement', placement);
  if (sent !== undefined) query.set('jfb-sent', sent);
  const response = await fetch(`${api}/forms/${form.dataset['jfbForm'] ?? ''}/render?${query.toString()}`, {
    signal: AbortSignal.timeout(WAIT),
  }).catch(() => null);
  return response?.ok ? parseSanitised(form.ownerDocument, await response.text()) : null;
}

/**
 * Puts the form as it is now in the old one's place, with the answers and the notice, which takes the focus; null for a
 * form this cannot carry over, which goes the way without JavaScript.
 */
export async function swapForm(
  form: HTMLFormElement,
  model: FormModel,
  api: string,
): Promise<{ readonly form: HTMLFormElement; readonly model: FormModel } | null> {
  // A summary page's rows and a view without the buttons to move with are for a post to write.
  const several = form.querySelectorAll('[data-jfb-page]').length > 1;
  if (
    form.querySelector('.jfb-repeater, .jfb-ranking') !== null ||
    (several && ((model.form.settings?.summaryPage === true && form.hasAttribute('action')) || !movesPages(form)))
  )
    return null;
  const host = hostOf(form);
  const next = host
    ? await host.fresh()
    : (await markupOf(form, model, api))?.querySelector<HTMLFormElement>('form[data-jfb-form]');
  const nextModel = next ? modelIn(next, form) : null;
  const template = next
    ?.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]')
    ?.content.querySelector('[data-jfb-notice="form_changed"]');
  if (!next || nextModel === null || !template) return null;
  const unticked = copyAnswers(form, next);
  // The visitor stays on the page they were on, if the form still has it: its render knows no answers, so its rules decide at the wake.
  const at = form.querySelector<HTMLElement>('[data-jfb-page]:not([hidden])')?.dataset['jfbPage'];
  const pages = [...next.querySelectorAll<HTMLElement>('[data-jfb-page]')];
  if (pages.some((page) => page.dataset['jfbPage'] === at)) {
    for (const page of pages) page.hidden = page.dataset['jfbPage'] !== at;
    const named = next.querySelector<HTMLInputElement>(':scope > input[name="jfb-page"]');
    if (named !== null && at !== undefined) named.value = at;
  }
  const notice = template.cloneNode(true) as HTMLElement;
  const line = notice.querySelector<HTMLElement>('[data-jfb-one]');
  if (line !== null && unticked > 0) {
    line.append(' ', line.dataset[unticked === 1 ? 'jfbOne' : 'jfbOther'] ?? '');
  }
  next.insertBefore(notice, next.querySelector(':scope > [data-jfb-page]'));
  next.setAttribute('data-jfb-woken', '');
  if (form.classList.contains('jfb-scheme-dark')) next.classList.add('jfb-scheme-dark');
  form.replaceWith(next);
  notice.focus();
  return { form: next, model: nextModel };
}
