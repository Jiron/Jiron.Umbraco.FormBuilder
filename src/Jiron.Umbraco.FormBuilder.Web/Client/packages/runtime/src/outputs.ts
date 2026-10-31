/**
 * The values the page shows beside its answers (ROADMAP 6.4f, ADR-0114), as the server's render writes them: each
 * calculation field's result in its `<output>`, from the state the rules decide (6.4c), and a slider's value beside its
 * thumb and in its `aria-valuetext` as it moves. A text is written only when it changes. A result's `<output>` keeps
 * quiet (`aria-live="off"`, as the views write it), a status that would speak on every key: once the visitor pauses,
 * the form's live region says the results on the current page that changed, in one line, each after its label (6.8b).
 * The runtime loads on the visitor's first act, so its first pass can already hold that act's result, which is said too.
 */
import { calculationText, sliderText, type FormState } from '@jfb/logic';
import type { FormModel, ModelField } from './model';
import { allFieldsOf, builtIn, rowOf } from './page';

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

/** The results changed since they were last said. */
const changed = new WeakSet<Element>();

/** The pause that says each form's changed results. */
const pauses = new WeakMap<HTMLFormElement, ReturnType<typeof setTimeout>>();

/** Shows each calculation's result as the state has it, a row's in its row; one hidden or without a value, nothing. */
export function showCalculations(form: HTMLFormElement, model: FormModel, state: FormState): void {
  const calculations = ofType(model, 'calculation');
  if (calculations.size === 0) return;
  let wrote = false;
  for (const element of form.querySelectorAll<HTMLElement>('[data-jfb-field]')) {
    const key = element.dataset['jfbField'] ?? '';
    const field = calculations.get(key);
    const output = field === undefined ? null : outputOf(element);
    if (field === undefined || output === null) continue;
    const row = rowOf(element, state);
    const results = row === null ? state.calculations : row.state?.calculations;
    const value = results !== undefined && Object.hasOwn(results, key) ? results[key] : null;
    if (write(output, value == null ? '' : calculationText(field.settings, value, model.formats))) {
      changed.add(output);
      wrote = true;
    }
  }
  if (!wrote) return;
  clearTimeout(pauses.get(form));
  const pause = setTimeout(() => {
    const line = [...form.querySelectorAll('output')]
      .filter((output) => changed.delete(output) && output.textContent && !output.closest('[hidden]'))
      .map((output) => [(output.labels[0]?.textContent ?? '').trim(), output.textContent].filter(Boolean).join(': '))
      .join('; ');
    const live = form.querySelector(':scope > [data-jfb-live]');
    if (live !== null && line !== '') live.textContent = line;
  }, PAUSE);
  pauses.set(form, pause);
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
