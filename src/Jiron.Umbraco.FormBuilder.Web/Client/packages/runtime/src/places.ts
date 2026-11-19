/**
 * What cached markup cannot carry (ROADMAP 7.1e, ADR-0156): the places left on a form's options, which the challenge's
 * answer tells a page, and which the page shows in each option's label: how many, or that it is full, which also disables
 * the option. An option the visitor has chosen stays theirs to change, and the server's `full` error says why when they
 * send it, as does the one the keyboard is on until it moves on; a full option that only a default chose is let go. An
 * option the answer does not name shows nothing; the server still refuses it once it is full.
 */
import { fill, own, plural } from '@jfb/logic';
import { proto } from '@jfb/logic/proto';
import type { FormModel } from './model';
import { formWords } from './messages';

/** The places left per option's value, per field's key, as the challenge tells them. */
export type Places = Readonly<Record<string, Readonly<Record<string, number>>>>;

/** Marks an option disabled for being full: one a rule keeps disabled, and one that a later answer may free. */
const FULL = 'data-jfb-full';
/** What a rule leaves disabled when it shows the field (ADR-0111). */
const DISABLED = 'data-jfb-disabled';

/** Shows the places left on the options in the root: a form, or a row just added to it. */
export async function showPlaces(
  root: ParentNode,
  places: Places,
  model: Pick<FormModel, 'culture' | 'texts'>,
): Promise<void> {
  const words = await formWords(model);
  const numbers = new Intl.NumberFormat(model.culture);
  for (const [key, options] of Object.entries(places)) {
    const field = `[data-jfb-field="${CSS.escape(key)}"]`;
    for (const control of proto(root).querySelectorAll<HTMLInputElement | HTMLOptionElement>(
      `${field} input[type="radio"], ${field} input[type="checkbox"], ${field} option`,
    )) {
      const left = own(options, control.value);
      if (left === undefined) continue;
      const text = left > 0 ? fill(plural(words.placesLeft, left), { count: numbers.format(left) }) : words.noPlaces;
      if (control instanceof HTMLOptionElement) {
        const label = (control.dataset['jfbLabel'] ??= control.textContent);
        control.textContent = `${label} (${text})`;
      } else {
        const label = control.labels?.[0];
        let shown = label?.querySelector('.jfb-places');
        if (label !== undefined && (shown === null || shown === undefined)) {
          shown = control.ownerDocument.createElement('span');
          shown.className = 'jfb-places';
          label.append(' ', shown);
        }
        if (shown) shown.textContent = text;
      }
      // An option the view disabled for another reason stays as it is.
      if (control.hasAttribute(DISABLED) && !control.hasAttribute(FULL)) continue;
      const isOption = control instanceof HTMLOptionElement;
      const chosen = isOption
        ? control.selected && !control.defaultSelected
        : control.checked && !control.defaultChecked;
      const full = left <= 0 && !chosen && control.ownerDocument.activeElement !== control;
      // One with places that was never full stays as a rule left it.
      if (!full && !control.hasAttribute(FULL)) continue;
      control.toggleAttribute(FULL, full);
      control.toggleAttribute(DISABLED, full);
      control.disabled = full;
      if (full && isOption) control.selected = false;
      else if (full && !isOption) control.checked = false;
    }
  }
}
