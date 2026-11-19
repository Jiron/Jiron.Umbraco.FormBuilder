// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The website code's lint rule against reading a form's members plainly: it finds a read, a write, a call, a
// destructuring and an `in` on whatever the types call a form, a branch of `??` or `?:` among them, and lets the
// reads through proto(form) and every other element's members be.
import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { afterAll, describe, it } from 'vitest';
import { formMembers } from './form-members.mjs';

RuleTester.describe = describe;
RuleTester.it = it;
RuleTester.afterAll = afterAll;

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser,
    parserOptions: { projectService: { allowDefaultProject: ['*.ts'] }, tsconfigRootDir: import.meta.dirname },
  },
});
const HEAD =
  'declare const form: HTMLFormElement; declare const row: Element | undefined; declare const div: HTMLDivElement;\n';
const plain = (code) => ({ code: HEAD + code, errors: [{ messageId: 'plain' }] });

tester.run('form-members', formMembers, {
  valid: [
    // Through proto, as the rule asks, and the form itself handed on, compared or kept.
    'proto(form).querySelector("x"); proto(form).dataset["jfbApi"]; proto(form).id = "a"; "x" in proto(form);',
    'const seen = new WeakSet<HTMLFormElement>(); seen.add(form); if (form === document.forms[0]) send(form);',
    // Any other element's members, and an Element that the types do not call a form.
    'div.querySelector("x"); div.dataset["a"]; row?.closest("form"); const { id } = div;',
    'declare function proto<T extends object>(target: T): T; declare function send(form: HTMLFormElement): void;',
  ].map((code) => HEAD + code),
  invalid: [
    plain('form.querySelector("x");'),
    plain('form.dataset["jfbApi"];'),
    plain('form.id;'),
    plain('form.noValidate = true;'),
    plain('form?.reset();'),
    plain('const { id } = form;'),
    plain('let id; ({ id } = form);'),
    plain('"requestSubmit" in form;'),
    // What the types fold away: a branch that may be the form.
    plain('(row ?? form).querySelector("x");'),
    plain('(row ? row : form).querySelector("x");'),
    // A form under another type: a union, a type parameter, a property, a narrowing.
    plain('declare const either: HTMLFormElement | HTMLDivElement; either.id;'),
    plain('function f<T extends HTMLFormElement>(t: T) { return t.action; }'),
    plain('declare const swapped: { form: HTMLFormElement }; swapped.form.dispatchEvent(new Event("x"));'),
    plain('declare const target: EventTarget; if (target instanceof HTMLFormElement) target.matches("x");'),
    // A view held in a variable is a form again: the view is written where it is read.
    plain('declare function proto<T extends object>(target: T): T; const view = proto(form); view.id;'),
  ],
});
