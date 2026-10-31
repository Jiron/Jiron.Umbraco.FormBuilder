/**
 * The values the page shows beside its answers (ROADMAP 6.4f, ADR-0114), as the server's render writes them: each
 * calculation field's result in its `<output>`, from the state the rules decide (6.4c), and a slider's value beside its
 * thumb and in its `aria-valuetext` as it moves. A text is written only when it changes. A result's `<output>` keeps
 * quiet (`aria-live="off"`, as the views write it), a status that would speak on every key: once the visitor pauses,
 * the form's live region says the results that changed, in one line, each after its label (6.8b).
 */
import { calculationText, sliderText, type FormState } from '@jfb/logic';
import type { FormModel, ModelField } from './model';
import { allFieldsOf, builtIn } from './page';

/** The fields of a built-in type by key, a repeatable group's child fields too. */
const ofType = (model: FormModel, type: string): Map<string, ModelField> =>
  new Map(
    allFieldsOf(model).flatMap((field) => (field.type === type && builtIn(model, field) ? [[field.key, field]] : [])),
  );

/** A calculation's or a slider's `<output>`: such a field holds no other field. */
const outputOf = (field: HTMLElement): HTMLOutputElement | null => field.querySelector('output');

/** Writes a text where it differs; true when it did. */
const write = (element: Element, text: string): boolean =>
  element.textContent !== text && !!(element.textContent = text);

/** How long the visitor pauses before the results they changed are said. */
const PAUSE = 1000;

/** The results each form changed since it last said them, and the pause that says them; none before its first pass. */
const pending = new WeakMap<
  HTMLFormElement,
  { readonly changed: Set<Element>; pause?: ReturnType<typeof setTimeout> }
>();

/** Shows each calculation's result as the state has it, a row's in its row; one hidden or without a value, nothing. */
export function showCalculations(form: HTMLFormElement, model: FormModel, state: FormState): void {
  const calculations = ofType(model, 'calculation');
  if (calculations.size === 0) return;
  // The first pass follows what the server wrote, which nobody changed.
  const said = pending.get(form);
  const changed = said?.changed ?? new Set<Element>();
  if (said === undefined) pending.set(form, { changed: new Set() });
  for (const element of form.querySelectorAll<HTMLElement>('[data-jfb-field]')) {
    const key = element.dataset['jfbField'] ?? '';
    const field = calculations.get(key);
    const output = field === undefined ? null : outputOf(element);
    if (field === undefined || output === null) continue;
    let results: Readonly<Record<string, string | null>> | undefined = state.calculations;
    const row = element.parentElement?.closest<HTMLElement>('[data-jfb-row]');
    if (row != null) {
      const group = row.closest<HTMLElement>('[data-jfb-field]')?.dataset['jfbField'] ?? '';
      results = state.rows[group]?.[Number(row.dataset['jfbRow'])]?.calculations;
    }
    const value = results !== undefined && Object.hasOwn(results, key) ? results[key] : null;
    if (write(output, value == null ? '' : calculationText(field.settings, value, model.formats))) changed.add(output);
  }
  if (said === undefined || changed.size === 0) return;
  clearTimeout(said.pause);
  said.pause = setTimeout(() => {
    const line = [...form.querySelectorAll('output')]
      .filter((output) => changed.has(output) && output.textContent !== '')
      .map((output) => [(output.labels[0]?.textContent ?? '').trim(), output.textContent].filter(Boolean).join(': '))
      .join('; ');
    changed.clear();
    const live = form.querySelector(':scope > [data-jfb-live]');
    if (live !== null && line !== '') live.textContent = line;
  }, PAUSE);
}

/**
 * Keeps each slider's shown value and `aria-valuetext` with its thumb, as the server writes them for its value: the
 * text beside the thumb where the field shows one, and the `aria-valuetext` while the text says more than the number.
 */
export function followSliders(form: HTMLFormElement, model: FormModel): void {
  const sliders = ofType(model, 'slider');
  if (sliders.size === 0) return;
  const follow = (input: EventTarget | null): void => {
    if (!(input instanceof HTMLInputElement) || input.type !== 'range') return;
    const element = input.closest<HTMLElement>('[data-jfb-field]');
    const field = sliders.get(element?.dataset['jfbField'] ?? '');
    if (element == null || field === undefined) return;
    const text = sliderText(field.settings, input.value, model.formats);
    if (text === input.value) input.removeAttribute('aria-valuetext');
    else if (input.getAttribute('aria-valuetext') !== text) input.setAttribute('aria-valuetext', text);
    const output = outputOf(element);
    if (output !== null) write(output, text);
  };
  // A value the browser restored, going back to the page, shows as the visitor left it.
  for (const input of form.querySelectorAll('input[type="range"]')) follow(input);
  form.addEventListener('input', (event) => follow(event.target));
}
