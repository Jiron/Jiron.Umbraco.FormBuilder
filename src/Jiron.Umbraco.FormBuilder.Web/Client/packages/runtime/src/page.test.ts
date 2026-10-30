// Reading the page (ROADMAP 6.4b, ADR-0110): the controls as the browser posts them, by their names as the server
// reads a post (held to it by tests/vectors/posts.json), then through 6.4a's reading stage: what the rules compare.
import { afterEach, describe, expect, it } from 'vitest';
import type { FormModel, ModelField } from './model';
import { entriesOf, readPage } from './page';

const ROW_1 = '3f2b6c1e-0000-4000-8000-000000000001';
const ROW_2 = '3f2b6c1e-0000-4000-8000-000000000002';

const field = (key: string, type: string, alias: string | null, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  alias,
  ...more,
});

const fields: ModelField[] = [
  field('k-name', 'text', 'name'),
  field('k-age', 'number', 'age', { settings: { decimals: 0 } }),
  field('k-topics', 'checkboxes', 'topics', { settings: { other: false } }),
  field('k-full', 'name', 'fullName', { settings: { parts: ['first', 'last'] } }),
  field('k-updates', 'yesno', 'updates', { settings: { style: 'switch' } }),
  field('k-terms', 'consent', 'terms'),
  field('k-grid', 'grid', 'grid', { settings: { multiple: false }, rows: [{ key: ROW_1 }, { key: ROW_2 }] }),
  field('k-colour', 'colour', 'colour'),
  field('k-source', 'hidden', 'source'),
  field('k-ref', 'hidden', 'ref', { prefill: { query: 'ref' } }),
  field('k-hiddenByRule', 'text', 'nickname'),
  field('k-file', 'file', 'cv'),
  field('k-heading', 'heading', null, { settings: { level: 2, text: { '': 'Hi' } } }),
  field('k-guests', 'repeater', 'guests', {
    settings: { minRows: 1, maxRows: 2 },
    fields: [field('k-guest', 'text', 'guest'), field('k-vegan', 'yesno', 'vegan', { settings: { style: 'switch' } })],
  }),
];

const model: FormModel = {
  alias: 'party',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', pages: [{ key: 'p', groups: [{ key: 'g', fields }] }] },
  fields: Object.fromEntries(
    [...fields, ...fields.flatMap((each) => each.fields ?? [])].map((each) => [
      each.key,
      { canBeRequired: true, builtIn: each.type !== 'colour' },
    ]),
  ),
  consents: {},
};

const hash = 'c'.repeat(64);

/** The form as its views name the controls, with the answers given. */
function page(rows = 2): HTMLFormElement {
  document.body.innerHTML = `
    <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
      <input name="name" value="  Ada  ">
      <input name="age" value="007">
      <input type="checkbox" name="topics" value="a" checked>
      <input type="checkbox" name="topics" value="b">
      <input type="checkbox" name="topics" value="c" checked>
      <input name="fullName.first" value="Ada">
      <input name="fullName.last" value="Lovelace">
      <input type="checkbox" role="switch" name="updates" value="yes">
      <input type="checkbox" name="terms" value="true" checked>
      <input type="hidden" name="jfb-consent-terms" value="${hash}">
      <input type="radio" name="grid.${ROW_1}" value="good" checked>
      <input type="radio" name="grid.${ROW_2}" value="poor" checked>
      <select name="colour" multiple><option selected> red </option><option selected>blue</option><option>green</option></select>
      <input type="hidden" name="source" value="posted">
      <input type="hidden" name="ref" value="newsletter">
      <input name="nickname" value="Addie" disabled>
      <input type="file" name="cv">
      <input type="hidden" name="jfb-rows-guests" value="${rows}">
      <input name="guests[0].guest" value="Bob">
      <input type="checkbox" role="switch" name="guests[0].vegan" value="yes" checked>
      <input name="guests[1].guest" value="Cy">
      <input type="checkbox" role="switch" name="guests[1].vegan" value="yes">
      <input type="hidden" name="jfb-context-campaign" value="spring">
      <button type="submit" name="jfb-nav" value="submit">Send</button>
    </form>`;
  const form = document.querySelector('form');
  if (form === null) throw new Error('the page has no form');
  return form;
}

/** The control of that name, which the page has. */
function control(form: HTMLFormElement, name: string): HTMLInputElement {
  const input = form.querySelector<HTMLInputElement>(`[name="${name}"]`);
  if (input === null) throw new Error(`the page has no ${name}`);
  return input;
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('reading the page', () => {
  it('posts what a browser posts: the enabled controls with a name, no file and no button', () => {
    const names = entriesOf(page()).map(([name]) => name);
    expect(names).toContain('topics');
    expect(names).not.toContain('nickname');
    expect(names).not.toContain('cv');
    expect(names).not.toContain('jfb-nav');
    expect(names).not.toContain('updates');
    expect(
      entriesOf(page())
        .filter(([name]) => name === 'topics')
        .map(([, value]) => value),
    ).toEqual(['a', 'c']);
  });

  it('reads the post by its names as the server does', () => {
    const { post } = readPage(page(), model);
    expect(post.fields.get('name')).toEqual({ values: ['  Ada  '] });
    expect(post.fields.get('fullName')).toEqual({
      values: [],
      parts: [
        ['first', ['Ada']],
        ['last', ['Lovelace']],
      ],
    });
    expect(post.fields.get('updates')).toEqual({ values: ['no'] });
    expect(post.fields.get('terms')).toEqual({ values: ['true'], parts: [['wording', [hash]]] });
    expect(post.fields.has('nickname')).toBe(false);
    expect(post.context.get('campaign')).toBe('spring');
    expect(post.rows.get('guests')?.map((row) => Object.fromEntries(row))).toEqual([
      { guest: { values: ['Bob'] }, vegan: { values: ['yes'] } },
      { guest: { values: ['Cy'] }, vegan: { values: ['no'] } },
    ]);
  });

  it('reads each field through the reading stage, as Core reads a submission', () => {
    const { fields: read } = readPage(page(), model);
    expect(read.get('k-name')?.values).toEqual(['Ada']);
    expect(read.get('k-age')?.values).toEqual(['7']);
    expect(read.get('k-topics')?.values).toEqual(['a', 'c']);
    expect(read.get('k-full')?.values).toEqual(['{"first":"Ada","last":"Lovelace"}']);
    expect(read.get('k-updates')?.values).toEqual(['no']);
    expect(read.get('k-terms')?.reading?.values).toEqual([{ text: 'true', stored: hash }]);
    expect(read.get('k-grid')?.values).toEqual([]);
    expect(read.get('k-heading')?.values).toEqual([]);
  });

  it("gives the rules a site's own type's cleaned texts as they are, and reads nothing of it", () => {
    const colour = readPage(page(), model).fields.get('k-colour');
    expect(colour?.reading).toBeNull();
    expect(colour?.values).toEqual(['red', 'blue']);
  });

  it('never takes a value the server resolves from the page, unless the field is prefilled from the query', () => {
    const read = readPage(page(), model).fields;
    expect(read.get('k-source')?.values).toEqual([]);
    expect(read.get('k-ref')?.values).toEqual(['newsletter']);
  });

  it('reads an answer the type cannot read, or over its limits, as no value', () => {
    const form = page();
    control(form, 'age').value = 'twelve';
    control(form, 'name').value = 'x'.repeat(4001);
    const read = readPage(form, model).fields;
    expect(read.get('k-age')?.reading?.unreadable).toBe(true);
    expect(read.get('k-age')?.values).toEqual([]);
    expect(read.get('k-name')?.reading?.limit).toBe('maxLength');
    expect(read.get('k-name')?.values).toEqual([]);
  });

  it("reads a repeatable group's rows when their number keeps its limits", () => {
    const guests = readPage(page(), model).groups.get('k-guests');
    expect(guests?.count).toBe(2);
    expect(guests?.errors).toEqual([]);
    expect(guests?.rows.map((row) => [row.get('k-guest')?.values, row.get('k-vegan')?.values])).toEqual([
      [['Bob'], ['yes']],
      [['Cy'], ['no']],
    ]);
  });

  it('reads no row of a group whose number of rows breaks its limits, and says which', () => {
    const guests = readPage(page(3), model).groups.get('k-guests');
    expect(guests?.count).toBe(3);
    expect(guests?.errors).toEqual(['rowCount']);
    expect(guests?.rows).toEqual([]);
  });

  it('gives the rules each value by its field, a grid row by its row, and a group row by row', () => {
    const { answers } = readPage(page(), model);
    expect(answers.values).toEqual({
      'k-name': ['Ada'],
      'k-age': ['7'],
      'k-topics': ['a', 'c'],
      'k-full': ['{"first":"Ada","last":"Lovelace"}'],
      'k-updates': ['no'],
      'k-terms': ['true'],
      [ROW_1]: ['good'],
      [ROW_2]: ['poor'],
      'k-colour': ['red', 'blue'],
      'k-ref': ['newsletter'],
    });
    expect(answers.rows).toEqual({
      'k-guests': [
        { 'k-guest': ['Bob'], 'k-vegan': ['yes'] },
        { 'k-guest': ['Cy'], 'k-vegan': ['no'] },
      ],
    });
  });

  it('follows the controls as the visitor changes them', () => {
    const form = page();
    expect(readPage(form, model).fields.get('k-updates')?.values).toEqual(['no']);
    control(form, 'updates').checked = true;
    control(form, 'nickname').disabled = false;
    const read = readPage(form, model).fields;
    expect(read.get('k-updates')?.values).toEqual(['yes']);
    expect(read.get('k-hiddenByRule')?.values).toEqual(['Addie']);
  });
});
