// Answer recall on the page (ROADMAP 6.4g, ADR-0115): the visitor texts that recall answers follow them as the visitor
// types, as the server renders them for a page's answers: a label, a help text, a placeholder, a heading, a text block,
// a group's description and a later page's title; a field the rules hide recalls nothing, nor a row's child they hide
// in its row; a value is escaped for a rich text; a text with no `field.*` tag and a field after its text are left as
// the server wrote them. The server's own pages hold the texts to the server's in recall.fixture.test.ts.
import type { RecallField, RecallForm } from '@jfb/logic';
import { afterEach, describe, expect, it } from 'vitest';
import { engineOf, recallsAnswers } from './engine';
import type { FormModel, ModelField, ModelText } from './model';
import { readPage } from './page';
import { textsOf } from './recall';
import { rulesOf } from './rules';
import { ENGLISH } from './testing';
import { checkPage } from './validation';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);
const CONTEXT = {
  now: '2026-10-30T09:15:00Z',
  timeZone: 'Europe/London',
  culture: 'en-US',
  member: { signedIn: false, groups: [] },
  query: {},
  hidden: [],
} as const;

const field = (key: string, type: string, alias: string | null, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  ...(alias === null ? {} : { alias }),
  settings: {},
  ...more,
});

const PLAN = {
  items: [
    { value: 's', label: 'Small room' },
    { value: 'l', label: 'Large room' },
  ],
};
const AGE = {
  items: [
    { value: 'under16', label: 'Under 16' },
    { value: 'adult', label: '16 or over' },
  ],
};
const FIELDS: ModelField[] = [
  field('k-name', 'text', 'name', { label: 'Your name' }),
  field('k-plan', 'radio', 'plan', { label: 'Plan', options: PLAN }),
  field('k-note', 'text', 'note', {
    label: 'Note',
    rules: [{ effect: 'show', match: 'all', conditions: [{ field: 'k-plan', op: 'equals', value: 'l' }] }],
  }),
  field('k-guests', 'repeater', 'guests', {
    label: 'Guests',
    fields: [
      field('k-guest', 'text', 'guest', { label: 'Guest' }),
      field('k-age', 'radio', 'age', {
        label: 'Age',
        options: AGE,
        rules: [{ effect: 'show', match: 'all', conditions: [{ field: 'k-guest', op: 'isNotEmpty' }] }],
      }),
    ],
  }),
  field('k-notes', 'textarea', 'notes', {
    label: 'Anything else, {{ field.name }}?',
    help: 'For the {{ field.plan | default: "room" }}: {{ field.note | default: "no note" }}',
    placeholder: '{{ field.name | default: "Your notes" }}',
  }),
  field('k-heading', 'heading', null, { settings: { level: 3, text: '{{ field.name }}' } }),
  field('k-block', 'paragraph', null, {
    settings: { html: '<p>Guests: <b>{{ field.guests.guest }}</b>, ages {{ field.guests.age }}</p>' },
  }),
  field('k-site', 'text', 'card', { label: '{{ site.name }} card' }),
  field('k-later', 'text', 'later', { label: 'Before {{ field.last }}' }),
  field('k-last', 'text', 'last', { label: 'Last' }),
];

const KINDS: Readonly<Record<string, string | null>> = { text: 'text', textarea: 'text', radio: 'choice' };

/** The recall form the endpoint gives: the texts' owners in their places, and the recalled fields with their aliases. */
function recallOf(fields: readonly ModelField[], recalled: ReadonlySet<string>): RecallForm {
  const recall = (each: ModelField): RecallField => ({
    id: each.key,
    type: each.type,
    holdsValue: Object.hasOwn(KINDS, each.type) || each.type === 'repeater',
    ...(recalled.has(each.key)
      ? {
          builtIn: true,
          kind: (KINDS[each.type] ?? null) as Exclude<RecallField['kind'], undefined>,
          alias: each.alias ?? null,
          label: each.label ?? null,
          settings: each.settings ?? {},
          ...(each.options?.items === undefined
            ? {}
            : { options: each.options.items.map((item) => ({ value: item.value, label: item.label ?? '' })) }),
        }
      : {}),
    ...(each.fields === undefined ? {} : { fields: each.fields.map(recall) }),
  });
  return {
    pages: [
      {
        id: 'p1',
        groups: [
          { id: 'g1', fields: fields.map(recall) },
          { id: 'g2', fields: [] },
        ],
      },
      { id: 'p2', groups: [] },
    ],
  };
}

const TEXTS: ModelText[] = [
  { owner: 'k-notes', text: 'label', context: 'text' },
  { owner: 'k-notes', text: 'help', context: 'text' },
  { owner: 'k-notes', text: 'placeholder', context: 'text' },
  { owner: 'k-heading', text: 'settings.text', context: 'text' },
  { owner: 'k-block', text: 'settings.html', context: 'html' },
  { owner: 'k-site', text: 'label', context: 'text' },
  { owner: 'k-later', text: 'label', context: 'text' },
  { owner: 'g2', text: 'description', context: 'html' },
  { owner: 'p2', text: 'title', context: 'text' },
];

function modelOf(texts: readonly ModelText[] = TEXTS): FormModel {
  const all = [...FIELDS, ...FIELDS.flatMap((each) => each.fields ?? [])];
  return {
    alias: 'booking',
    revision: 1,
    culture: 'en-US',
    timeZone: 'Europe/London',
    open: true,
    form: {
      key: 'f',
      pages: [
        {
          key: 'p1',
          groups: [
            { key: 'g1', fields: FIELDS },
            { key: 'g2', description: '<p>For <b>{{ field.name | default: "you" }}</b></p>', fields: [] },
          ],
        },
        { key: 'p2', title: 'Your {{ site.name }} booking, {{ field.name }}', groups: [] },
      ],
    },
    fields: Object.fromEntries(
      all.map((each) => [
        each.key,
        { kind: KINDS[each.type] ?? null, canBeRequired: Object.hasOwn(KINDS, each.type), builtIn: true },
      ]),
    ),
    consents: {},
    ...ENGLISH,
    mergeTags: {
      texts,
      values: { text: { 'site.name': 'Library' }, html: {} },
      recall: recallOf(FIELDS, new Set(['k-name', 'k-plan', 'k-note', 'k-guests', 'k-guest', 'k-age', 'k-last'])),
    },
  };
}

const hook = (owner: string, name: string): string => `data-jfb-text="${owner} ${name}"`;
const FORM = `
  <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
    <section data-jfb-page="p1">
      <div data-jfb-group="g1">
        <div data-jfb-field="k-name"><label for="name">Your name</label><input id="name" name="name"></div>
        <div data-jfb-field="k-plan"><fieldset><legend>Plan</legend>
          <input type="radio" id="plan-s" name="plan" value="s"><input type="radio" id="plan-l" name="plan" value="l">
        </fieldset></div>
        <div data-jfb-field="k-note" hidden data-jfb-hidden><input id="note" name="note" disabled></div>
        <div data-jfb-field="k-guests"><input type="hidden" name="jfb-rows-guests" value="2">
          <fieldset data-jfb-row="0"><legend>Guest 1</legend>
            <div data-jfb-field="k-guest"><input id="guest-0" name="guests[0].guest" value="Ben"></div>
            <div data-jfb-field="k-age"><input type="radio" id="age-0" name="guests[0].age" value="adult" checked></div>
          </fieldset>
          <fieldset data-jfb-row="1"><legend>Guest 2</legend>
            <div data-jfb-field="k-guest"><input id="guest-1" name="guests[1].guest"></div>
            <div data-jfb-field="k-age" hidden data-jfb-hidden>
              <input type="radio" id="age-1" name="guests[1].age" value="under16" checked disabled></div>
          </fieldset>
        </div>
        <div data-jfb-field="k-notes">
          <label for="notes"><span id="notes-label" ${hook('k-notes', 'label')}>Anything else, ?</span></label>
          <p id="notes-help" ${hook('k-notes', 'help')}>For the room: no note</p>
          <textarea id="notes" name="notes" placeholder="Your notes" ${hook('k-notes', 'placeholder')}></textarea>
        </div>
        <div data-jfb-field="k-heading"><h3 id="heading" ${hook('k-heading', 'settings.text')} hidden></h3></div>
        <div data-jfb-field="k-block"><div id="block" ${hook('k-block', 'settings.html')}><p>Guests: <b>Ben</b>, ages 16 or over</p></div></div>
        <div data-jfb-field="k-site"><label for="card"><span id="site" ${hook('k-site', 'label')}>Library card</span></label><input id="card" name="card"></div>
        <div data-jfb-field="k-later"><label for="later"><span id="later-label" ${hook('k-later', 'label')}>Before {{ field.last }}</span></label><input id="later" name="later"></div>
        <div data-jfb-field="k-last"><label for="last">Last</label><input id="last" name="last"></div>
      </div>
      <div data-jfb-group="g2"><div id="intro" ${hook('g2', 'description')}><p>For <b>you</b></p></div></div>
    </section>
    <section data-jfb-page="p2" hidden><h2 id="title" ${hook('p2', 'title')} hidden></h2></section>
  </form>`;

const formOf = (markup: string): HTMLFormElement => {
  document.body.innerHTML = markup;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
};
const byId = (id: string): HTMLElement => {
  const element = document.getElementById(id);
  if (element === null) throw new Error(`no #${id}`);
  return element;
};
const textOf = (id: string): string => byId(id).textContent ?? '';

async function type(id: string, value: string): Promise<void> {
  const input = byId(id) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await Promise.resolve();
}

async function choose(id: string): Promise<void> {
  const input = byId(id) as HTMLInputElement;
  input.checked = true;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  await Promise.resolve();
}

function attach(model: FormModel = modelOf()): HTMLFormElement {
  const form = formOf(FORM);
  rulesOf(form, model, engineOf(model, null, textsOf(model)), () => NOW);
  return form;
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('a text that recalls answers', () => {
  it('follows them as the visitor types: a label, a placeholder, a heading, a description and a later page title', async () => {
    attach();
    expect(textOf('notes-label')).toBe('Anything else, ?');
    expect(byId('heading').hidden, 'a heading that renders empty').toBe(true);
    await type('name', 'Anna');
    expect(textOf('notes-label')).toBe('Anything else, Anna?');
    expect(byId('notes').getAttribute('placeholder')).toBe('Anna');
    expect([textOf('heading'), byId('heading').hidden]).toEqual(['Anna', false]);
    expect(byId('intro').innerHTML).toBe('<p>For <b>Anna</b></p>');
    expect([textOf('title'), byId('title').hidden], "a later page's title, on a page not shown").toEqual([
      'Your Library booking, Anna',
      false,
    ]);
    await type('name', '');
    expect([textOf('heading'), byId('heading').hidden]).toEqual(['', true]);
    expect(byId('notes').getAttribute('placeholder'), "the placeholder's default").toBe('Your notes');
    expect(byId('intro').innerHTML).toBe('<p>For <b>you</b></p>');
  });

  it('recalls nothing of a field the rules hide, as the server does', async () => {
    attach();
    expect(textOf('notes-help')).toBe('For the room: no note');
    await choose('plan-l');
    await type('note', 'by the window');
    expect(textOf('notes-help')).toBe('For the Large room: by the window');
    await choose('plan-s');
    expect(textOf('notes-help'), 'the note is hidden again, its answer kept').toBe('For the Small room: no note');
    expect((byId('note') as HTMLInputElement).value).toBe('by the window');
  });

  it("recalls a row's child in the rows that show it only, and follows the rows' answers", async () => {
    attach();
    expect(byId('block').innerHTML).toBe('<p>Guests: <b>Ben</b>, ages 16 or over</p>');
    await type('guest-1', 'Cleo');
    expect(byId('block').innerHTML).toBe('<p>Guests: <b>Ben, Cleo</b>, ages 16 or over, Under 16</p>');
    await type('guest-0', '');
    expect(byId('block').innerHTML).toBe('<p>Guests: <b>Cleo</b>, ages Under 16</p>');
  });

  it('escapes a value in a rich text, so no answer becomes markup', async () => {
    attach();
    await type('name', '<img src=x onerror=alert(1)> & co');
    const intro = byId('intro');
    expect(intro.querySelectorAll('img')).toHaveLength(0);
    expect(intro.querySelector('b')?.textContent).toBe('<img src=x onerror=alert(1)> & co');
    expect(textOf('notes-label')).toBe('Anything else, <img src=x onerror=alert(1)> & co?');
  });

  it('leaves a text with no field tag, and a field after its text, as the server wrote them', async () => {
    const form = attach();
    await type('last', 'Zoe');
    await type('card', 'x');
    expect(textOf('site')).toBe('Library card');
    expect(textOf('later-label')).toBe('Before {{ field.last }}');
    const model = modelOf();
    const state = engineOf(model, null).decide(readPage(form, model).answers, CONTEXT);
    const texts = textsOf(model)(readPage(form, model), state);
    expect([...texts.keys()]).not.toContain('k-site label');
    expect(texts.has('k-later label'), 'a field after its text renders as it is written, which never changes').toBe(
      false,
    );
  });

  it("leaves out a text whose element the endpoint's form does not hold", () => {
    const model = modelOf([...TEXTS, { owner: 'k-gone', text: 'label', context: 'text' }]);
    const form = formOf(FORM);
    const reading = readPage(form, model);
    const state = engineOf(model, null).decide(reading.answers, CONTEXT);
    expect([...textsOf(model)(reading, state).keys()]).not.toContain('k-gone label');
  });

  it('renders nothing for a form without merge tags', () => {
    const model = { ...modelOf(), mergeTags: null };
    const form = formOf(FORM);
    const reading = readPage(form, model);
    expect(textsOf(model)(reading, engineOf(model, null).decide(reading.answers, CONTEXT)).size).toBe(0);
  });
});

describe('whether a form recalls answers', () => {
  it("is whether its recall form names a field, a repeatable group's child too", () => {
    const model = modelOf();
    expect(recallsAnswers(model)).toBe(true);
    expect(recallsAnswers({ ...model, mergeTags: null })).toBe(false);
    const named = (fields: RecallField[]): FormModel => ({
      ...model,
      mergeTags: { texts: TEXTS, values: {}, recall: { pages: [{ id: 'p1', groups: [{ id: 'g1', fields }] }] } },
    });
    expect(recallsAnswers(named([{ id: 'k-heading', type: 'heading', holdsValue: false }])), 'owners only').toBe(false);
    expect(
      recallsAnswers(
        named([
          {
            id: 'k-guests',
            type: 'repeater',
            holdsValue: true,
            fields: [{ id: 'k-guest', type: 'text', holdsValue: true, alias: 'guest' }],
          },
        ]),
      ),
    ).toBe(true);
  });
});

describe("the error summary's link", () => {
  const name = field('s-name', 'text', 'name', { label: 'Your name' });
  const notes = field('s-notes', 'textarea', 'notes', { label: 'Anything else, {{ field.name }}?', required: true });
  const agree = field('s-agree', 'checkbox', 'agree', {
    label: '{{ field.name }}',
    required: true,
    settings: { text: 'Yes, keep me posted' },
  });
  const model: FormModel = {
    ...modelOf(),
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [name, notes, agree] }] }] },
    fields: {
      's-name': { kind: 'text', canBeRequired: true, builtIn: true },
      's-notes': { kind: 'text', canBeRequired: true, builtIn: true },
      's-agree': { kind: 'choice', canBeRequired: true, builtIn: true },
    },
    mergeTags: {
      texts: [
        { owner: 's-notes', text: 'label', context: 'text' },
        { owner: 's-agree', text: 'label', context: 'text' },
      ],
      values: {},
      recall: {
        pages: [
          {
            id: 'p1',
            groups: [
              {
                id: 'g1',
                fields: [
                  { id: 's-name', type: 'text', holdsValue: true, builtIn: true, kind: 'text', alias: 'name' },
                  { id: 's-notes', type: 'textarea', holdsValue: true },
                  { id: 's-agree', type: 'checkbox', holdsValue: true },
                ],
              },
            ],
          },
        ],
      },
    },
  };
  const SUMMARY = `
    <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">
      <div class="jfb-field" data-jfb-field="s-name"><label class="jfb-label" for="jfb-1-name">Your name</label>
        <input class="jfb-control" id="jfb-1-name" name="name"></div>
      <div class="jfb-field" data-jfb-field="s-notes"><label class="jfb-label" for="jfb-1-notes"><span
        data-jfb-text="s-notes label">Anything else, ?</span> <span class="jfb-mark" aria-hidden="true">*</span></label>
        <textarea class="jfb-control" id="jfb-1-notes" name="notes" required></textarea></div>
      <div class="jfb-field" data-jfb-field="s-agree"><div class="jfb-choice">
        <input type="checkbox" id="jfb-1-agree" name="agree" value="true" required>
        <label class="jfb-choice-label" for="jfb-1-agree">Yes, keep me posted</label></div></div>
    </div></section></form>`;

  it('names a label that recalls answers as it renders for the answers checked, the page showing it or not', async () => {
    const form = formOf(SUMMARY);
    const engine = engineOf(model, null, textsOf(model));
    rulesOf(form, model, engine, () => NOW);
    await type('jfb-1-name', 'Anna');
    const links = (await checkPage(form, model, engine, 'send', () => NOW)).map((error) => error.link);
    expect(links).toHaveLength(2);
    expect(links[0]).toMatch(/^Anything else, Anna\? \S/);
    expect(links[1], "a box's field label, which the page does not show").toMatch(/^Anna: \S/);
    expect(links.join(' ')).not.toContain('{{');
  });

  it('names none while it renders empty', async () => {
    const form = formOf(SUMMARY);
    const engine = engineOf(model, null, textsOf(model));
    rulesOf(form, model, engine, () => NOW);
    await type('jfb-1-notes', 'A quiet room');
    const [agreeing, ...others] = await checkPage(form, model, engine, 'send', () => NOW);
    expect(others).toEqual([]);
    expect(agreeing?.field).toBe('s-agree');
    const message = agreeing?.message ?? '';
    expect(message.length).toBeGreaterThan(0);
    expect(agreeing?.link).toBe(message);
  });
});

describe("a text that recalls a grid or a site's own type", () => {
  const ROW = '3f2b6c1e-0000-4000-8000-0000000000a1';
  const grid = field('k-happy', 'grid', 'happy', {
    label: 'How happy are you?',
    settings: { multiple: false },
    rows: [{ key: ROW, label: 'Staff' }],
    options: {
      items: [
        { value: 'good', label: 'Good' },
        { value: 'poor', label: 'Poor' },
      ],
    },
  });
  const mood = field('k-mood', 'acme.mood', 'mood', { label: 'Mood' });
  const say = field('k-say', 'text', 'say', { label: 'Ratings: {{ field.happy }}; mood: {{ field.mood }}' });
  const model: FormModel = {
    ...modelOf(),
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [grid, mood, say] }] }] },
    fields: {
      'k-happy': { rowKind: 'choice', canBeRequired: true, builtIn: true },
      'k-mood': { kind: 'text', canBeRequired: true, builtIn: false },
      'k-say': { kind: 'text', canBeRequired: true, builtIn: true },
    },
    mergeTags: {
      texts: [{ owner: 'k-say', text: 'label', context: 'text' }],
      values: {},
      recall: {
        pages: [
          {
            id: 'p1',
            groups: [
              {
                id: 'g1',
                fields: [
                  {
                    id: 'k-happy',
                    type: 'grid',
                    holdsValue: true,
                    builtIn: true,
                    kind: null,
                    alias: 'happy',
                    label: 'How happy are you?',
                    settings: { multiple: false },
                    options: [
                      { value: 'good', label: 'Good' },
                      { value: 'poor', label: 'Poor' },
                    ],
                    rows: [{ id: ROW, label: 'Staff' }],
                  },
                  { id: 'k-mood', type: 'acme.mood', holdsValue: true, builtIn: false, kind: 'text', alias: 'mood' },
                  { id: 'k-say', type: 'text', holdsValue: true },
                ],
              },
            ],
          },
        ],
      },
    },
  };
  const GRID = `
    <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">
      <div data-jfb-field="k-happy"><fieldset><legend>How happy are you?</legend>
        <input type="radio" id="good" name="happy.${ROW}" value="good"><input type="radio" id="poor" name="happy.${ROW}" value="poor">
      </fieldset></div>
      <div data-jfb-field="k-mood"><input id="mood" name="mood"></div>
      <div data-jfb-field="k-say"><label for="say"><span id="say-label" data-jfb-text="k-say label">Ratings: ; mood: </span></label>
        <input id="say" name="say"></div>
    </div></section></form>`;

  it("recalls a grid's rows as its type shows them, and a site's own type's answer as it is typed", async () => {
    const form = formOf(GRID);
    rulesOf(form, model, engineOf(model, null, textsOf(model)), () => NOW);
    await choose('good');
    await type('mood', '  calm ');
    expect(textOf('say-label')).toBe('Ratings: Staff: Good; mood: calm');
  });
});
