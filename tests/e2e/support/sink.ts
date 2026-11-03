import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Locator } from '@playwright/test';
import { repoRoot } from './paths.ts';

/**
 * The server's sink in a theme, as the element fixtures hold it: a form with a field of every type in a group of two
 * columns, so fields drawn as a fieldset (a signature, a scale) stand beside fields with a label (a slider, a list).
 */
export function sinkMarkup(name: 'sink' | 'sink-bootstrap5'): string {
  const fixture = JSON.parse(
    readFileSync(join(repoRoot, 'tests', 'fixtures', 'rendering', 'element-en.json'), 'utf8'),
  ) as { cases: { name: string; html: string }[] };
  const html = fixture.cases.find((each) => each.name === name)?.html;
  if (!html) throw new Error(`The element fixtures have no ${name}.`);
  return html;
}

/**
 * Where the parts of fields side by side in the sink sit, read in one frame: the tops of the slider's and the
 * signature's help, which share a row, and the heights of the country list and the file's drop frame beside it, the
 * frame as the runtime leaves it.
 */
export async function sideBySide(form: Locator) {
  return form.evaluate((element) => {
    for (const zone of element.querySelectorAll('.jfb-upload')) zone.setAttribute('data-jfb-enhanced', '');
    const box = (selector: string) => {
      const found = element.querySelector(selector);
      if (!found) throw new Error(`The sink has no ${selector}.`);
      return found.getBoundingClientRect();
    };
    return {
      sliderHelp: Math.round(box('.jfb-field-slider > .jfb-help').top),
      signatureHelp: Math.round(box('.jfb-field-signature > fieldset > .jfb-help').top),
      countryTop: Math.round(box('.jfb-field-country > .jfb-help').top),
      fileTop: Math.round(box('.jfb-field-file > .jfb-help').top),
      country: Math.round(box('.jfb-field-country select').height),
      file: Math.round(box('.jfb-field-file .jfb-upload').height),
    };
  });
}
