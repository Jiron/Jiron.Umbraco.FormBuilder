// The form check's paths and where Fix it leads (ROADMAP 9.5f, ADR-0181): the server's JSON paths read step by step,
// each place of the definition the designer shows mapped to its element, item, rule, the palette or a view.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition } from '../definition';
import { bySeverity, countOf, fixOf, issuesOf, parsePath, severityOf, type CheckIssue } from './form-check';

const BACKSLASH = String.fromCharCode(0x5c);

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      rules: [{ key: 'pr', effect: 'show', conditions: [] }],
      groups: [
        {
          key: 'g1',
          rules: [{ key: 'gr', effect: 'hide', conditions: [] }],
          fields: [
            {
              key: 'email',
              type: 'email',
              alias: 'email',
              rules: [{ key: 'er', effect: 'require', conditions: [] }],
              validation: [{ kind: 'pattern', pattern: '.+' }, { kind: 'uniquePerForm' }, { kind: 'other' }],
            },
            {
              key: 'kids',
              type: 'repeater',
              alias: 'kids',
              fields: [{ key: 'kid', type: 'text', alias: 'kid' }],
            },
            { key: 'grid', type: 'grid', alias: 'grid', rows: [{ key: 'row1' }] },
          ],
        },
        { key: 'g2', fields: [] },
      ],
    },
    { key: 'p2', groups: [] },
  ],
  validationRules: [
    { key: 'v1', field: 'email', conditions: [] },
    { key: 'v2', field: 'row1', conditions: [] },
    { key: 'v3', field: 'gone', conditions: [] },
  ],
};

const F = '$.pages[0].groups[0].fields';

describe('a JSON path', () => {
  it('reads dotted names, bracketed names with their escapes, and indexes', () => {
    expect(parsePath('$')).toEqual([]);
    expect(parsePath(`${F}[1].fields[0].label['']`)).toEqual([
      'pages',
      0,
      'groups',
      0,
      'fields',
      1,
      'fields',
      0,
      'label',
      '',
    ]);
    expect(parsePath("$.pages[0].title['de-CH']")).toEqual(['pages', 0, 'title', 'de-CH']);
    expect(parsePath(`$.a['it${BACKSLASH}'s']`)).toEqual(['a', "it's"]);
    expect(parsePath(`$.a['${BACKSLASH}${BACKSLASH}']`)).toEqual(['a', BACKSLASH]);
    expect(parsePath(`$.a['${BACKSLASH}u0009x']`)).toEqual(['a', '\tx']);
    expect(parsePath('$._x1.y_2')).toEqual(['_x1', 'y_2']);
  });

  it('is nothing the server does not write', () => {
    for (const path of ['', 'pages', '$.', '$.1a', '$[x]', "$['open", "$['x'", '$[1', `$['${BACKSLASH}q']`, '$..a'])
      expect(parsePath(path), path).toBeNull();
  });
});

describe("a problem's Fix it", () => {
  const fix = (path: string) => fixOf(DEFINITION, path);

  it("leads to a field and the inspector's item its path names", () => {
    expect(fix(`${F}[0].label['']`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'text', property: 'label' },
    });
    expect(fix(`${F}[0].placeholder.de`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'text', property: 'placeholder' },
    });
    expect(fix(`${F}[0].autocomplete`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'plain', property: 'autocomplete' },
    });
    expect(fix(`${F}[0].formerAliases[0]`)).toEqual({ kind: 'element', key: 'email', item: { kind: 'alias' } });
    expect(fix(`${F}[0].options.items[2].image`)).toEqual({ kind: 'element', key: 'email', item: { kind: 'options' } });
    expect(fix(`${F}[0].messages.required['']`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'message', message: 'required' },
    });
    expect(fix(`${F}[0].settings.maxBytes`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'setting', name: 'maxBytes' },
    });
    expect(fix(`${F}[0].settings.address.street['']`)).toEqual({
      kind: 'element',
      key: 'email',
      item: { kind: 'setting', name: 'address', inner: 'street' },
    });
  });

  it("leads to a field's check by its kind, and to the field for a check the inspector doesn't edit", () => {
    expect(fix(`${F}[0].validation[0].pattern`)).toEqual({ kind: 'element', key: 'email', item: { kind: 'pattern' } });
    expect(fix(`${F}[0].validation[1]`)).toEqual({ kind: 'element', key: 'email', item: { kind: 'unique' } });
    expect(fix(`${F}[0].validation[2]`)).toEqual({ kind: 'element', key: 'email' });
    expect(fix(`${F}[0].validation[9]`)).toEqual({ kind: 'element', key: 'email' });
  });

  it("leads to the field itself for a place the inspector does not show, and to a repeatable group's child", () => {
    expect(fix(`${F}[0]`)).toEqual({ kind: 'element', key: 'email' });
    expect(fix(`${F}[0].type`)).toEqual({ kind: 'element', key: 'email' });
    expect(fix(`${F}[1].fields[0].label['']`)).toEqual({
      kind: 'element',
      key: 'kid',
      item: { kind: 'text', property: 'label' },
    });
    expect(fix(`${F}[1].fields`)).toEqual({ kind: 'element', key: 'kids' });
    expect(fix(`${F}[2].rows[0].label['']`)).toEqual({ kind: 'element', key: 'grid', item: { kind: 'rows' } });
  });

  it("opens the rules builder on an element's rule, and on a validation rule's field or grid", () => {
    expect(fix(`${F}[0].rules[0]`)).toEqual({ kind: 'rule', key: 'email', rule: 'er' });
    expect(fix(`${F}[0].rules[0].conditions[0].value`)).toEqual({ kind: 'rule', key: 'email', rule: 'er' });
    expect(fix('$.pages[0].groups[0].rules[0]')).toEqual({ kind: 'rule', key: 'g1', rule: 'gr' });
    expect(fix('$.pages[0].rules[0].conditions[1]')).toEqual({ kind: 'rule', key: 'p1', rule: 'pr' });
    expect(fix('$.validationRules[0].conditions[0].value')).toEqual({ kind: 'rule', key: 'email', rule: 'v1' });
    expect(fix('$.validationRules[1].message')).toEqual({ kind: 'rule', key: 'grid', rule: 'v2' });
    expect(fix('$.validationRules[2]')).toBeNull();
    // A rule gone leads to its element.
    expect(fix(`${F}[0].rules[5]`)).toEqual({ kind: 'element', key: 'email' });
  });

  it("leads to a group's and a page's items, and to the palette for a page or group without fields", () => {
    expect(fix("$.pages[0].groups[0].title['']")).toEqual({
      kind: 'element',
      key: 'g1',
      item: { kind: 'text', property: 'title' },
    });
    expect(fix('$.pages[0].groups[0].columns')).toEqual({ kind: 'element', key: 'g1', item: { kind: 'columns' } });
    expect(fix("$.pages[0].title['']")).toEqual({
      kind: 'element',
      key: 'p1',
      item: { kind: 'text', property: 'title' },
    });
    expect(fix('$.pages[0].columns')).toEqual({ kind: 'element', key: 'p1' });
    expect(fix('$.pages[0].groups[1].fields')).toEqual({ kind: 'palette', page: 'p1' });
    expect(fix('$.pages[1].groups')).toEqual({ kind: 'palette', page: 'p2' });
    expect(fix('$.pages')).toEqual({ kind: 'palette', page: 'p1' });
    expect(fixOf({ pages: [] }, '$.pages')).toEqual({ kind: 'palette', page: null });
  });

  it("leads to the view where the form's settings, outcomes and actions are edited", () => {
    expect(fix('$.settings.appearance.theme')).toEqual({ kind: 'view', view: 'settings' });
    expect(fix('$.outcomes[1].rules[0]')).toEqual({ kind: 'view', view: 'settings' });
    expect(fix('$.actions[0].settings.replyToField')).toEqual({ kind: 'view', view: 'actions' });
  });

  it('leads nowhere for the whole form, a place the form no longer has, or a path the server does not write', () => {
    for (const path of ['$', '$.name', '$.pages[7]', `${F}[9].label`, '$.pages[0].groups[9]', 'nonsense', '$.pages.x'])
      expect(fix(path), path).toBeNull();
  });
});

describe("the check's problems", () => {
  const issue = (severity: CheckIssue['severity'], code: string): CheckIssue => ({
    severity,
    code,
    path: '$',
    message: code,
  });

  it('count errors and warnings for the badge, and hints not', () => {
    const issues = [issue('error', 'a'), issue('warning', 'b'), issue('hint', 'c'), issue('warning', 'd')];
    expect(countOf(issues)).toBe(3);
    expect(countOf([issue('hint', 'c')])).toBe(0);
    const found = bySeverity(issues);
    expect(found.error.map((one) => one.code)).toEqual(['a']);
    expect(found.warning.map((one) => one.code)).toEqual(['b', 'd']);
    expect(found.hint.map((one) => one.code)).toEqual(['c']);
  });

  it('read their weight in any letter case, one this version does not know as a warning', () => {
    expect(['error', 'Warning', 'HINT', 'notice'].map(severityOf)).toEqual(['error', 'warning', 'hint', 'warning']);
    expect(issuesOf([{ severity: 'Error', code: 'x', path: '$.pages', message: 'M' }])).toEqual([
      { severity: 'error', code: 'x', path: '$.pages', message: 'M' },
    ]);
  });
});
