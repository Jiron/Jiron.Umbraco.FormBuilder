// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/stylesheets.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { cssWords, lintStylesheet, themeScope } from '../lib/stylesheets.mjs';

describe('lintStylesheet', () => {
  test('finds selectors outside .jfb-form, also inside at-rules', () => {
    const css = '.jfb-form a { color: red }\nbody, .jfb-form p { margin: 0 }\n.jfb-formx { color: red }\n@media (min-width: 1px) { .x { color: red } .jfb-form .y { color: red } }';
    assert.deepEqual(lintStylesheet(css).unscoped, ['body', '.jfb-formx', '.x']);
  });

  test("finds selectors outside a theme's scope, which a plain .jfb-form is", () => {
    const scope = themeScope('default');
    assert.equal(scope, '.jfb-form:where(.jfb-theme-default)');
    const css = `${scope} { color: red; & .x { color: red } }\n.jfb-form .y { color: red }\n.jfb-form:where(.jfb-theme-defaults) { color: red }\n@container jfb-form (min-width: 1px) { ${scope} .z { color: red } }`;
    assert.deepEqual(lintStylesheet(css, scope).unscoped, ['.jfb-form .y', '.jfb-form:where(.jfb-theme-defaults)']);
  });

  test('reads a rule nested in a scoped rule as inside its scope, an ancestor before it too', () => {
    const css = '.jfb-form { .jfb-scheme-dark & { color: red } @media (min-width: 1px) { body & { color: red } } }';
    assert.deepEqual(lintStylesheet(css), { unscoped: [], physical: [] });
  });

  test('takes keyframes as they are, since they select nothing', () => {
    assert.deepEqual(lintStylesheet('@keyframes jfb-turn { to { rotate: 1turn } }'), { unscoped: [], physical: [] });
  });

  test('finds physical left and right, and four-value shorthands whose sides differ', () => {
    const css = `.jfb-form a {
      margin-left: 1px; float: left; text-align: right; padding: 0 1px 0 2px; border-radius: 1px 2px;
      inset-inline-start: 0; float: inline-start; padding: 1px 2px 3px; margin: 1px 2px 3px 2px; border-radius: 4px; --x-left: 1px;
    }`;
    assert.deepEqual(lintStylesheet(css).physical, ['margin-left: 1px', 'float: left', 'text-align: right', 'padding: 0 1px 0 2px', 'border-radius: 1px 2px']);
  });

  test('finds physical declarations in a nested and a minified stylesheet', () => {
    const css = '.jfb-form{& .a{right:0}@media (min-width:1px){& .b{border-left-width:1px;inset:0 1px 0 0}}}';
    assert.deepEqual(lintStylesheet(css).physical, ['right: 0', 'border-left-width: 1px', 'inset: 0 1px 0 0']);
  });

  test('reads a nested @starting-style as part of its rule', () => {
    const css = '.jfb-form .e { opacity: 1; @starting-style { opacity: 0; translate: 0 -4px; } }\n.jfb-form .f { color: red }';
    assert.deepEqual(lintStylesheet(css), { unscoped: [], physical: [] });
  });

  test('ignores comments', () => {
    assert.deepEqual(lintStylesheet('/* body { margin-left: 1px } */ .jfb-form { color: red }'), { unscoped: [], physical: [] });
  });
});

describe('cssWords', () => {
  test('splits on whitespace outside parentheses', () => {
    assert.deepEqual(cssWords(' 0 calc(1px + 2px)  var(--a, 1px 2px) '), ['0', 'calc(1px + 2px)', 'var(--a, 1px 2px)']);
  });
});
