// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The backoffice's lint rule against words written into its templates (ROADMAP 14.1): it finds letters between a Lit
// template's tags and in an attribute a person reads or hears, and nothing a template puts in, references, keys,
// addresses, styles, comments or attributes no one reads.
import { RuleTester } from 'eslint';
import { afterAll, describe, it } from 'vitest';
import { noHardCodedText } from './no-hard-coded-text.mjs';

RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.afterAll = afterAll;

const tester = new RuleTester({ languageOptions: { ecmaVersion: 2024, sourceType: 'module' } });
const words = (code) => ({ code, errors: [{ messageId: 'words' }] });

tester.run('no-hard-coded-text', noHardCodedText, {
  valid: [
    // What a template puts in, and words from the dictionaries.
    'html`<p>${this.localize.term("jfbDesigner_save")}</p>`',
    'html`<uui-button label=${label} look="primary" color="positive"></uui-button>`',
    'html`<span>${count} \u00b7 ${total}</span><span>(${n})</span>`',
    // References, keys, addresses, styles, comments and attributes no one reads.
    'html`<p>&nbsp;&times;&#8203;</p>`',
    'html`<kbd>${held}+Z</kbd>`',
    'html`<input placeholder="https://" class="code" type="text" />`',
    'html`<style>.label { color: red; }</style>`',
    'html`<!-- a note for the reader --><div class="item" id="box" data-kind="text" role="group"></div>`',
    // Another tag than html is not the backoffice's markup.
    'css`.label { content: "Word"; }`',
    'other`<p>Words</p>`',
  ],
  invalid: [
    words('html`<p>Save</p>`'),
    words('html`<p>${count} entries</p>`'),
    words('html`<uui-button label="Save"></uui-button>`'),
    words('html`<input placeholder="Your name" />`'),
    words('html`<button aria-label="Close"></button>`'),
    words('html`<uui-box headline="Sender"></uui-box>`'),
    words('html`<img alt="Logo" />`'),
    words('html`<span title="Speichern"></span>`'),
    words('html`<span .label=${"x"} title=\'Mehr\'></span>`'),
    words('this.html`<p>\u00c4pfel</p>`'),
    // A key outside <kbd>, and words after a closed style, still count.
    words('html`<kbd>Z</kbd> then Y`'),
    words('html`<style>.a {}</style><p>Words</p>`'),
  ],
});
