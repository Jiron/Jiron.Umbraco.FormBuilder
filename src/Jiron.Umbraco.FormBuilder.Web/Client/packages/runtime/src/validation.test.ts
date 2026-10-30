// Checks and messages on the page (ROADMAP 6.4d, ADR-0112) where no post of a browser without JavaScript reaches, so
// checks.fixture.test.ts cannot hold them to the server: the words of a culture's language, loaded once; the empty
// submission's link past what the page does not show; Next's page alone; an editor's empty message; an answer over its
// limits; and what only the server decides: files, calculations, options from a list or a source, a site's own type.
import { de } from '@jfb/logic/lang/de';
import { en } from '@jfb/logic/lang/en';
import { evaluateForm, logicFormOf } from '@jfb/logic';
import { afterEach, describe, expect, it } from 'vitest';
import { errorsOf } from './checks';
import { languageOf, wordsFor } from './messages';
import type { FormModel, ModelField, ModelPage } from './model';
import { readPage } from './page';
import { contextOf } from './rules';
import { ENGLISH } from './testing';
import { checkPage } from './validation';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);
const clock = (): number => NOW;

const field = (key: string, type: string, alias: string, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  alias,
  settings: {},
  ...more,
});

const KINDS: Readonly<Record<string, string | null>> = {
  text: 'text',
  number: 'number',
  dropdown: 'choice',
  repeater: null,
  heading: null,
  file: null,
  calculation: 'number',
};

function modelOf(pages: ModelPage[], own: readonly string[] = []): FormModel {
  const all = pages.flatMap((page) => page.groups.flatMap((group) => group.fields));
  return {
    alias: 'booking',
    revision: 1,
    culture: 'en-US',
    timeZone: 'UTC',
    open: true,
    form: { key: 'f', pages },
    fields: Object.fromEntries(
      [...all, ...all.flatMap((each) => each.fields ?? [])].map((each) => [
        each.key,
        {
          kind: KINDS[each.type] ?? null,
          canBeRequired: each.type !== 'repeater' && each.type !== 'heading',
          builtIn: !own.includes(each.key),
        },
      ]),
    ),
    consents: {},
    ...ENGLISH,
  };
}

const show = (field: string, op: string) => ({ effect: 'show', match: 'all', conditions: [{ field, op }] });

function formOf(html: string): HTMLFormElement {
  document.body.innerHTML = `<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">${html}</form>`;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
}

const text = (key: string, alias: string, label: string, value = '', more = ''): string => `
  <div class="jfb-field" data-jfb-field="${key}" ${more}>
    <label class="jfb-label" for="jfb-1-${alias}">${label}</label>
    <input class="jfb-control" id="jfb-1-${alias}" name="${alias}" value="${value}">
  </div>`;

afterEach(() => {
  document.body.innerHTML = '';
});

describe("a culture's words", () => {
  it("are its language's, else those of its more general culture, else English, as Core's", () => {
    expect(['de-DE', 'DE-at', 'de', 'en-GB', 'fr-FR', 'x-private-use', ''].map(languageOf)).toEqual([
      'de',
      'de',
      'de',
      'en',
      'en',
      'en',
      'en',
    ]);
  });

  it('load once for every culture of the language', async () => {
    const first = wordsFor('de-DE');
    expect(wordsFor('de-CH')).toBe(first);
    expect(await first).toBe(de);
    expect(await wordsFor('en-US')).toBe(en);
  });
});

describe('an empty submission', () => {
  const pages: ModelPage[] = [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          rules: [show('k-name', 'isNotEmpty')],
          fields: [field('k-hidden-group', 'text', 'inHiddenGroup')],
        },
        {
          key: 'g2',
          fields: [
            field('k-heading', 'heading', 'intro', { settings: { text: { '': 'Hi' }, level: 2 } }),
            field('k-shown-later', 'text', 'later', { rules: [show('k-name', 'isNotEmpty')] }),
            field('k-name', 'text', 'name'),
          ],
        },
      ],
    },
  ];

  it('links to the first field the page shows that takes an answer, past a hidden group, a hidden field and a heading', async () => {
    const form = formOf(`
      <section data-jfb-page="p1">
        <div data-jfb-group="g1" hidden data-jfb-hidden>${text('k-hidden-group', 'inHiddenGroup', 'In a hidden group')}</div>
        <div data-jfb-group="g2">
          <div class="jfb-field" data-jfb-field="k-heading"><h2>Hi</h2></div>
          ${text('k-shown-later', 'later', 'Later', '', 'hidden data-jfb-hidden')}
          ${text('k-name', 'name', 'Name')}
        </div>
      </section>`);
    expect(await checkPage(form, modelOf(pages), 'send', clock)).toEqual([
      { field: '', row: null, code: 'empty', target: 'jfb-1-name', message: en.empty, link: en.empty },
    ]);
  });

  it('links to the form on a page without a field that takes an answer', async () => {
    const intro: ModelPage[] = [
      { key: 'p1', groups: [{ key: 'g1', fields: [field('k-heading', 'heading', 'intro')] }] },
      { key: 'p2', groups: [{ key: 'g2', fields: [field('k-name', 'text', 'name', { required: true })] }] },
    ];
    const form = formOf(`
      <section data-jfb-page="p1"><div data-jfb-group="g1"><div data-jfb-field="k-heading"><h2>Hi</h2></div></div></section>
      <section data-jfb-page="p2" hidden data-jfb-hidden><div data-jfb-group="g2">${text('k-name', 'name', 'Name')}</div></section>`);
    expect((await checkPage(form, modelOf(intro), 'send', clock)).map((error) => error.target)).toEqual(['jfb-1']);
  });

  it("is no error of Next's, which checks the page's fields", async () => {
    const form = formOf(
      `<section data-jfb-page="p1"><div data-jfb-group="g2">${text('k-name', 'name', 'Name')}</div></section>`,
    );
    const required = [
      { key: 'p1', groups: [{ key: 'g2', fields: [field('k-name', 'text', 'name', { required: true })] }] },
    ];
    expect((await checkPage(form, modelOf(required), 'page', clock)).map((error) => error.code)).toEqual(['required']);
  });
});

describe("Next's check", () => {
  it("covers the page shown, its repeatable groups' rows too, and no other page", async () => {
    const pages: ModelPage[] = [
      {
        key: 'p1',
        groups: [
          {
            key: 'g1',
            fields: [
              field('k-guests', 'repeater', 'guests', {
                settings: { minRows: 1, maxRows: 2 },
                fields: [field('k-guest', 'text', 'guest', { required: true, label: 'Guest name' })],
              }),
              field('k-note', 'text', 'note'),
            ],
          },
        ],
      },
      { key: 'p2', groups: [{ key: 'g2', fields: [field('k-later', 'text', 'later', { required: true })] }] },
    ];
    const form = formOf(`
      <section data-jfb-page="p1"><div data-jfb-group="g1">
        <div data-jfb-field="k-guests"><input type="hidden" name="jfb-rows-guests" value="1">
          <fieldset data-jfb-row="0"><legend>Guest 1</legend>${text('k-guest', 'guests[0].guest', 'Guest name')}</fieldset>
        </div>
        ${text('k-note', 'note', 'Note', 'Late')}
      </div></section>
      <section data-jfb-page="p2" hidden data-jfb-hidden><div data-jfb-group="g2">${text('k-later', 'later', 'Later')}</div></section>`);
    const model = modelOf(pages);
    expect((await checkPage(form, model, 'page', clock)).map(({ target, link }) => ({ target, link }))).toEqual([
      { target: 'jfb-1-guests-0-guest', link: 'Guest name (Guest 1): Enter an answer' },
    ]);
    expect((await checkPage(form, model, 'send', clock)).map((error) => error.field)).toEqual(['k-guest', 'k-later']);
  });
});

describe('the messages', () => {
  it("say the default for an editor's empty message, with the label the page shows without its mark", async () => {
    const pages = [
      {
        key: 'p1',
        groups: [
          { key: 'g1', fields: [field('k-name', 'text', 'name', { required: true, messages: { required: '' } })] },
        ],
      },
    ];
    const form = formOf(`<section data-jfb-page="p1"><div data-jfb-group="g1">
      ${text('k-name', 'name', 'Your <em>full</em> name <span class="jfb-mark" data-jfb-mark="required">*</span>', ' ')}
    </div></section>`);
    expect(await checkPage(form, modelOf(pages), 'page', clock)).toEqual([
      {
        field: 'k-name',
        row: null,
        code: 'required',
        target: 'jfb-1-name',
        message: 'Enter an answer',
        link: 'Your full name: Enter an answer',
      },
    ]);
  });
});

describe('the checks', () => {
  const errorsFor = (pages: ModelPage[], html: string, own: readonly string[] = []) => {
    const form = formOf(`<section data-jfb-page="p1"><div data-jfb-group="g1">${html}</div></section>`);
    const model = modelOf(pages, own);
    const reading = readPage(form, model);
    const context = contextOf(model, reading, NOW, form.ownerDocument.URL);
    return errorsOf(
      model,
      reading,
      evaluateForm(logicFormOf(model.form, model.fields), reading.answers, context),
      context,
    );
  };
  const page = (...fields: ModelField[]): ModelPage[] => [{ key: 'p1', groups: [{ key: 'g1', fields }] }];

  it("give an answer over its type's limits that error alone", () => {
    const code = field('k-code', 'text', 'code', { validation: [{ kind: 'pattern', pattern: '[0-9]+' }] });
    expect(errorsFor(page(code), text('k-code', 'code', 'Code', 'x'.repeat(4001)))).toEqual([
      { field: 'k-code', row: null, code: 'maxLength' },
    ]);
    expect(errorsFor(page(code), text('k-code', 'code', 'Code', 'xyz'))).toEqual([
      { field: 'k-code', row: null, code: 'check', check: 0 },
    ]);
  });

  it('leave files, calculations and the values the server resolves to it, which stores, works out and fills them', () => {
    const fields = page(
      field('k-cv', 'file', 'cv', { required: true }),
      field('k-count', 'number', 'count'),
      field('k-total', 'calculation', 'total', { required: true, settings: { formula: 'count * 2' } }),
      field('k-source', 'hidden', 'source', { required: true }),
    );
    const html = `
      <div data-jfb-field="k-cv"><input type="file" name="cv"></div>
      ${text('k-count', 'count', 'Count', '3')}
      <div data-jfb-field="k-total"></div>
      <input type="hidden" name="source">`;
    expect(errorsFor(fields, html)).toEqual([]);
  });

  it("hold a choice to its own options and those its parent's answer offers, and leave a list's or a source's to the server", () => {
    const fields = page(
      field('k-country', 'dropdown', 'country', { options: { items: [{ value: 'de' }, { value: 'fr' }] } }),
      field('k-region', 'dropdown', 'region', {
        options: {
          items: [
            { value: 'bavaria', parentValues: ['de'] },
            { value: 'brittany', parentValues: ['fr'] },
          ],
          parent: 'k-country',
        },
      }),
      field('k-city', 'dropdown', 'city', { options: { fromSource: true } }),
    );
    const html = (country: string, region: string): string => `
      <div data-jfb-field="k-country"><select name="country"><option value="${country}" selected></option></select></div>
      <div data-jfb-field="k-region"><select name="region"><option value="${region}" selected></option></select></div>
      <div data-jfb-field="k-city"><select name="city"><option value="Atlantis" selected></option></select></div>`;
    expect(errorsFor(fields, html('de', 'bavaria'))).toEqual([]);
    expect(errorsFor(fields, html('de', 'brittany'))).toEqual([{ field: 'k-region', row: null, code: 'invalid' }]);
    expect(errorsFor(fields, html('es', 'bavaria')).map((error) => error.field)).toEqual(['k-country', 'k-region']);
  });

  it("check a site's own type for an answer only", () => {
    const colour = page(field('k-colour', 'colour', 'colour', { required: true, settings: { palette: 'warm' } }));
    expect(errorsFor(colour, text('k-colour', 'colour', 'Colour'), ['k-colour'])).toEqual([
      { field: 'k-colour', row: null, code: 'required' },
    ]);
    expect(errorsFor(colour, text('k-colour', 'colour', 'Colour', 'not a colour at all'), ['k-colour'])).toEqual([]);
  });
});
