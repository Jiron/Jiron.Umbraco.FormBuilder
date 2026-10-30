// Rules on the page (ROADMAP 6.4c, ADR-0111): the form engine over the page's answers, in the context of a render, and
// the state it decides shown in the hooks the views write: hidden elements and their disabled controls, `required`
// on the controls that follow their field, the marks in the state they name, and the note while an asterisk shows.
import { afterEach, describe, expect, it } from 'vitest';
import type { FormModel, ModelField, ModelPage } from './model';
import { readPage } from './page';
import { contextOf, rulesOf } from './rules';

const ROW_1 = '3f2b6c1e-0000-4000-8000-000000000011';
const ROW_2 = '3f2b6c1e-0000-4000-8000-000000000012';
const NOW = Date.UTC(2026, 9, 30, 9, 15, 42, 678);

const when = (field: string, op: string, value?: string) => ({
  match: 'all',
  conditions: [{ field, op, ...(value === undefined ? {} : { value }) }],
});
const show = (field: string, op: string, value?: string) => ({ effect: 'show', ...when(field, op, value) });
const hide = (field: string, op: string, value?: string) => ({ effect: 'hide', ...when(field, op, value) });
const require = (field: string, op: string, value?: string) => ({ effect: 'require', ...when(field, op, value) });

const field = (key: string, type: string, alias: string, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  alias,
  settings: {},
  ...more,
});

const KINDS: Readonly<Record<string, string | null>> = {
  radio: 'choice',
  dropdown: 'choice',
  text: 'text',
  textarea: 'text',
  name: 'text',
  repeater: null,
  grid: null,
};

function modelOf(pages: ModelPage[], more: Partial<FormModel['form']> = {}): FormModel {
  const all = pages.flatMap((page) => page.groups.flatMap((group) => group.fields));
  return {
    alias: 'evening',
    revision: 1,
    culture: 'en-GB',
    timeZone: 'Europe/London',
    open: true,
    form: { key: 'f', pages, ...more },
    fields: Object.fromEntries(
      [...all, ...all.flatMap((each) => each.fields ?? [])].map((each) => [
        each.key,
        {
          kind: KINDS[each.type] ?? null,
          ...(each.type === 'grid' ? { rowKind: 'choice' } : {}),
          canBeRequired: each.type !== 'repeater',
          builtIn: true,
        },
      ]),
    ),
    consents: {},
  };
}

/** A course's sign-up as the views render it for no answers: the asterisk style, with every hook of a form with rules. */
const course = modelOf([
  {
    key: 'p1',
    groups: [
      {
        key: 'g1',
        fields: [
          field('k-evenings', 'radio', 'evenings', { settings: { other: false } }),
          field('k-days', 'text', 'days', { required: true, rules: [show('k-evenings', 'equals', 'yes')] }),
          field('k-late', 'text', 'late', { rules: [show('k-days', 'equals', 'Fri')] }),
          field('k-comments', 'textarea', 'comments', { rules: [require('k-evenings', 'equals', 'no')] }),
          field('k-guests', 'repeater', 'guests', {
            settings: { minRows: 1, maxRows: 3 },
            fields: [
              field('k-guest', 'text', 'guest'),
              field('k-diet', 'text', 'diet', { rules: [show('k-guest', 'isNotEmpty')] }),
            ],
          }),
        ],
      },
      { key: 'g2', rules: [hide('k-evenings', 'equals', 'no')], fields: [field('k-room', 'dropdown', 'room')] },
    ],
  },
  {
    key: 'p2',
    rules: [show('k-evenings', 'equals', 'yes')],
    groups: [{ key: 'g3', fields: [field('k-parking', 'text', 'parking')] }],
  },
  {
    key: 'p3',
    rules: [show('k-evenings', 'equals', 'no')],
    groups: [{ key: 'g4', fields: [field('k-why', 'text', 'why')] }],
  },
]);

const COURSE = `
  <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
    <p class="jfb-help" hidden data-jfb-required-note>Questions marked * need an answer.</p>
    <section class="jfb-page" data-jfb-page="p1">
      <div class="jfb-group" data-jfb-group="g1">
        <div class="jfb-field" data-jfb-field="k-evenings">
          <fieldset><legend>Evenings?</legend>
            <input type="radio" id="evenings-yes" name="evenings" value="yes" data-jfb-requires>
            <input type="radio" id="evenings-no" name="evenings" value="no" data-jfb-requires>
          </fieldset>
        </div>
        <div class="jfb-field" data-jfb-field="k-days" hidden data-jfb-hidden>
          <label for="days">Days <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required" hidden>*</span></label>
          <input id="days" name="days" value="Fri" disabled data-jfb-requires>
        </div>
        <div class="jfb-field" data-jfb-field="k-late" hidden data-jfb-hidden>
          <label for="late">Late arrival</label>
          <input id="late" name="late" value="After eight" disabled data-jfb-requires>
        </div>
        <div class="jfb-field" data-jfb-field="k-comments">
          <label for="comments">Comments <span class="jfb-mark" aria-hidden="true" data-jfb-mark="required" hidden>*</span></label>
          <textarea id="comments" name="comments" data-jfb-requires></textarea>
        </div>
        <div class="jfb-field" data-jfb-field="k-guests">
          <div class="jfb-repeater">
            <input type="hidden" name="jfb-rows-guests" value="2">
            <fieldset class="jfb-row" data-jfb-row="0"><legend>Guest 1</legend>
              <div class="jfb-field" data-jfb-field="k-guest"><input id="guest-0" name="guests[0].guest" value="Bob" data-jfb-requires></div>
              <div class="jfb-field" data-jfb-field="k-diet"><input id="diet-0" name="guests[0].diet" value="Vegan" data-jfb-requires></div>
            </fieldset>
            <fieldset class="jfb-row" data-jfb-row="1"><legend>Guest 2</legend>
              <button type="submit" id="remove-1" name="jfb-nav" value="remove:guests:1">Remove</button>
              <div class="jfb-field" data-jfb-field="k-guest"><input id="guest-1" name="guests[1].guest" data-jfb-requires></div>
              <div class="jfb-field" data-jfb-field="k-diet" hidden data-jfb-hidden><input id="diet-1" name="guests[1].diet" disabled data-jfb-requires></div>
            </fieldset>
            <button type="submit" id="add" name="jfb-nav" value="add:guests">Add a guest</button>
          </div>
        </div>
      </div>
      <div class="jfb-group" data-jfb-group="g2">
        <div class="jfb-field" data-jfb-field="k-room">
          <label for="room">Room</label>
          <select id="room" name="room" disabled data-jfb-requires data-jfb-disabled><option value="">No rooms yet</option></select>
        </div>
      </div>
    </section>
    <section class="jfb-page" data-jfb-page="p2" hidden data-jfb-hidden>
      <div class="jfb-group" data-jfb-group="g3" hidden data-jfb-hidden>
        <div class="jfb-field" data-jfb-field="k-parking" hidden data-jfb-hidden><input id="parking" name="parking" disabled data-jfb-requires></div>
      </div>
    </section>
    <section class="jfb-page" data-jfb-page="p3" hidden data-jfb-hidden>
      <div class="jfb-group" data-jfb-group="g4" hidden data-jfb-hidden>
        <div class="jfb-field" data-jfb-field="k-why" hidden data-jfb-hidden><input id="why" name="why" disabled data-jfb-requires></div>
      </div>
    </section>
  </form>`;

const formOf = (markup: string): HTMLFormElement => {
  document.body.innerHTML = markup;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
};

const byId = (id: string): HTMLElement => {
  const element = document.getElementById(id);
  if (element === null) throw new Error('no #' + id);
  return element;
};

const fieldOf = (key: string, row?: number): HTMLElement => {
  const scope = row === undefined ? document : document.querySelector(`[data-jfb-row="${String(row)}"]`);
  const element = scope?.querySelector<HTMLElement>(`[data-jfb-field="${key}"]`);
  if (element == null) throw new Error('no field ' + key);
  return element;
};

const markOf = (key: string): HTMLElement => {
  const mark = fieldOf(key).querySelector<HTMLElement>('[data-jfb-mark]');
  if (mark === null) throw new Error('no mark on ' + key);
  return mark;
};

/** The attributes the rules decide, of an element by its field's key or its own selector. */
const stateOf = (element: Element) => ({
  hidden: element.hasAttribute('hidden'),
  ruled: element.hasAttribute('data-jfb-hidden'),
});

const choose = async (id: string): Promise<void> => {
  const input = byId(id) as HTMLInputElement;
  input.checked = true;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  await Promise.resolve();
};

const type = async (id: string, value: string): Promise<void> => {
  const input = byId(id) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await Promise.resolve();
};

const note = (): Element => {
  const element = document.querySelector('[data-jfb-required-note]');
  if (element === null) throw new Error('no note');
  return element;
};

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the context of the rules on the page', () => {
  it("is the render's: the clock to the second, the site's time zone, the culture and no member", () => {
    const form = formOf(COURSE);
    const context = contextOf(course, readPage(form, course), NOW, 'https://example.com/courses/');
    expect(context).toEqual({
      now: '2026-10-30T09:15:42Z',
      timeZone: 'Europe/London',
      culture: 'en-GB',
      member: { signedIn: false, groups: [] },
      query: {},
      hidden: [],
    });
  });

  it("reads the address's query values of the names the form lists, and those a post carried on before them", () => {
    const model: FormModel = { ...course, form: { ...course.form, settings: { queryValues: ['source', 'ref'] } } };
    const form = formOf(COURSE);
    const address = 'https://example.com/courses/?source=ad&source=mail&other=x&ref=a+b';
    expect({ ...contextOf(model, readPage(form, model), NOW, address).query }).toEqual({
      source: ['ad', 'mail'],
      ref: ['a b'],
    });

    form.insertAdjacentHTML('beforeend', '<input type="hidden" name="jfb-query-source" value="posted">');
    form.insertAdjacentHTML('beforeend', '<input type="hidden" name="jfb-query-other" value="kept out">');
    expect({ ...contextOf(model, readPage(form, model), NOW, address).query }).toEqual({ source: ['posted'] });
  });
});

describe('the rules on the page', () => {
  it('changes nothing the server rendered for the answers it had', () => {
    const form = formOf(COURSE);
    const before = form.outerHTML;
    const changes: MutationRecord[] = [];
    const observer = new MutationObserver((records) => changes.push(...records));
    observer.observe(form, { attributes: true, subtree: true });
    const rules = rulesOf(form, course, () => NOW);
    expect([...(rules?.state.hidden ?? [])].sort()).toEqual([
      'g3',
      'g4',
      'k-days',
      'k-late',
      'k-parking',
      'k-why',
      'p2',
      'p3',
    ]);
    expect(changes.concat(observer.takeRecords())).toEqual([]);
    observer.disconnect();
    expect(form.outerHTML).toBe(before);
  });

  it('shows a field a rule shows, with its controls enabled and required, its asterisk and the note', async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    await choose('evenings-yes');
    expect(stateOf(fieldOf('k-days'))).toEqual({ hidden: false, ruled: false });
    expect(byId('days').hasAttribute('disabled')).toBe(false);
    expect(byId('days').hasAttribute('required')).toBe(true);
    expect(markOf('k-days').hasAttribute('hidden')).toBe(false);
    expect(note().hasAttribute('hidden')).toBe(false);
    expect(byId('evenings-yes').hasAttribute('required')).toBe(false);
  });

  it("decides again with a shown field's answer, so what depends on it shows at once", async () => {
    const form = formOf(COURSE);
    const rules = rulesOf(form, course, () => NOW);
    await choose('evenings-yes');
    expect(stateOf(fieldOf('k-late'))).toEqual({ hidden: false, ruled: false });
    expect(byId('late').hasAttribute('disabled')).toBe(false);
    expect(rules?.state.hidden).not.toContain('k-late');
    expect(byId('late').hasAttribute('required')).toBe(false);
  });

  it('hides a field a rule hides again, disables its controls and takes back its asterisk and the note', async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    await choose('evenings-yes');
    await choose('evenings-no');
    expect(stateOf(fieldOf('k-days'))).toEqual({ hidden: true, ruled: true });
    expect(byId('days').hasAttribute('disabled')).toBe(true);
    expect(byId('days').hasAttribute('required')).toBe(false);
    expect(markOf('k-days').hasAttribute('hidden')).toBe(true);
    expect(stateOf(fieldOf('k-late'))).toEqual({ hidden: true, ruled: true });
    expect((byId('days') as HTMLInputElement).value).toBe('Fri');
  });

  it('requires a field a rule requires, with its asterisk, which brings the note', async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    await choose('evenings-no');
    expect(byId('comments').hasAttribute('required')).toBe(true);
    expect(markOf('k-comments').hasAttribute('hidden')).toBe(false);
    expect(note().hasAttribute('hidden')).toBe(false);
    await choose('evenings-yes');
    expect(byId('comments').hasAttribute('required')).toBe(false);
    expect(markOf('k-comments').hasAttribute('hidden')).toBe(true);
  });

  it('hides a group with its fields, and keeps disabled a control the server disables by itself', async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    const group = document.querySelector('[data-jfb-group="g2"]');
    if (group === null) throw new Error('no group');
    await choose('evenings-no');
    expect(stateOf(group)).toEqual({ hidden: true, ruled: true });
    expect(stateOf(fieldOf('k-room'))).toEqual({ hidden: true, ruled: true });
    await choose('evenings-yes');
    expect(stateOf(group)).toEqual({ hidden: false, ruled: false });
    expect(stateOf(fieldOf('k-room'))).toEqual({ hidden: false, ruled: false });
    expect(byId('room').hasAttribute('disabled')).toBe(true);
  });

  it('keeps a page that is not the current one hidden, and says when a rule hides it', async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    const page = (key: string): Element => {
      const element = document.querySelector(`[data-jfb-page="${key}"]`);
      if (element === null) throw new Error('no page ' + key);
      return element;
    };
    await choose('evenings-yes');
    expect([page('p1'), page('p2'), page('p3')].map(stateOf)).toEqual([
      { hidden: false, ruled: false },
      { hidden: true, ruled: false },
      { hidden: true, ruled: true },
    ]);
    expect(byId('parking').hasAttribute('disabled')).toBe(false);
    expect(byId('why').hasAttribute('disabled')).toBe(true);
  });

  it("decides each row of a repeatable group by its own answers, and leaves the group's own controls", async () => {
    const form = formOf(COURSE);
    rulesOf(form, course, () => NOW);
    await type('guest-1', 'Cy');
    expect(stateOf(fieldOf('k-diet', 1))).toEqual({ hidden: false, ruled: false });
    expect(byId('diet-1').hasAttribute('disabled')).toBe(false);
    await type('guest-0', '');
    expect(stateOf(fieldOf('k-diet', 0))).toEqual({ hidden: true, ruled: true });
    expect(byId('diet-0').hasAttribute('disabled')).toBe(true);
    expect(byId('remove-1').hasAttribute('disabled')).toBe(false);
    expect(byId('add').hasAttribute('disabled')).toBe(false);
  });

  it('works out the answers of one turn once', async () => {
    const form = formOf(COURSE);
    const rules = rulesOf(form, course, () => NOW);
    const states: unknown[] = [];
    const apply = rules?.apply.bind(rules);
    if (rules === null || apply === undefined) throw new Error('no rules');
    rules.apply = () => {
      const state = apply();
      states.push(state);
      return state;
    };
    const input = byId('guest-1') as HTMLInputElement;
    input.value = 'Cy';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await Promise.resolve();
    expect(states).toHaveLength(1);
  });

  it('leaves a form without rules, and one whose rules the engine refuses, as the server rendered it', async () => {
    const plain = modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [field('k-evenings', 'radio', 'evenings')] }] }]);
    expect(rulesOf(formOf(COURSE), plain, () => NOW)).toBeNull();

    const refused = modelOf([
      {
        key: 'p1',
        groups: [
          { key: 'g1', fields: [field('k-days', 'text', 'days', { rules: [show('k-gone', 'equals', 'yes')] })] },
        ],
      },
    ]);
    const form = formOf(COURSE);
    const before = form.outerHTML;
    expect(rulesOf(form, refused, () => NOW)).toBeNull();
    await choose('evenings-yes');
    expect(form.outerHTML).toBe(before);
  });
});

describe('the marks the rules change', () => {
  const grid = modelOf([
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            field('k-evenings', 'radio', 'evenings', { settings: { other: false } }),
            field('k-rate', 'grid', 'rate', {
              settings: { multiple: false },
              rows: [{ key: ROW_1 }, { key: ROW_2 }],
              rules: [require('k-evenings', 'equals', 'no')],
            }),
          ],
        },
      ],
    },
  ]);

  it("shows a row's own asterisk while its grid is not required, and the grid's while it is", async () => {
    const form = formOf(`
      <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
        <p class="jfb-help" data-jfb-required-note>Questions marked * need an answer.</p>
        <section class="jfb-page" data-jfb-page="p1"><div class="jfb-group" data-jfb-group="g1">
          <div class="jfb-field" data-jfb-field="k-evenings">
            <input type="radio" id="evenings-yes" name="evenings" value="yes" data-jfb-requires>
            <input type="radio" id="evenings-no" name="evenings" value="no" data-jfb-requires>
          </div>
          <div class="jfb-field" data-jfb-field="k-rate">
            <fieldset><legend>Rate <span class="jfb-mark" id="grid-mark" aria-hidden="true" data-jfb-mark="required" hidden>*</span></legend>
              <fieldset><legend>Food <span class="jfb-mark" id="row-mark" aria-hidden="true" data-jfb-mark="optional">*</span></legend>
                <input type="radio" id="food" name="rate.${ROW_1}" value="1" required>
              </fieldset>
              <fieldset><legend>Music</legend>
                <input type="radio" id="music" name="rate.${ROW_2}" value="1" data-jfb-requires>
              </fieldset>
            </fieldset>
          </div>
        </div></section>
      </form>`);
    rulesOf(form, grid, () => NOW);
    expect(note().hasAttribute('hidden')).toBe(false);
    await choose('evenings-no');
    expect(byId('grid-mark').hasAttribute('hidden')).toBe(false);
    expect(byId('row-mark').hasAttribute('hidden')).toBe(true);
    expect(byId('music').hasAttribute('required')).toBe(true);
    expect(byId('food').hasAttribute('required')).toBe(true);
    await choose('evenings-yes');
    expect(byId('grid-mark').hasAttribute('hidden')).toBe(true);
    expect(byId('row-mark').hasAttribute('hidden')).toBe(false);
    expect(byId('music').hasAttribute('required')).toBe(false);
    expect(byId('food').hasAttribute('required')).toBe(true);
  });

  it('shows "(optional)" while a field is not required, and a part\'s while its field is', async () => {
    const optional = modelOf([
      {
        key: 'p1',
        groups: [
          {
            key: 'g1',
            fields: [
              field('k-evenings', 'radio', 'evenings', { settings: { other: false } }),
              field('k-full', 'name', 'full', {
                settings: { parts: ['first', 'middle'] },
                rules: [require('k-evenings', 'equals', 'no')],
              }),
            ],
          },
        ],
      },
    ]);
    const form = formOf(`
      <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
        <section class="jfb-page" data-jfb-page="p1"><div class="jfb-group" data-jfb-group="g1">
          <div class="jfb-field" data-jfb-field="k-evenings">
            <input type="radio" id="evenings-yes" name="evenings" value="yes" data-jfb-requires>
            <input type="radio" id="evenings-no" name="evenings" value="no" data-jfb-requires>
          </div>
          <div class="jfb-field" data-jfb-field="k-full">
            <fieldset><legend>Name <span class="jfb-mark" id="field-mark" data-jfb-mark="optional">(optional)</span></legend>
              <label for="first">First</label><input id="first" name="full.first" data-jfb-requires>
              <label for="middle">Middle <span class="jfb-mark" id="part-mark" data-jfb-mark="required" hidden>(optional)</span></label>
              <input id="middle" name="full.middle">
            </fieldset>
          </div>
        </div></section>
      </form>`);
    rulesOf(form, optional, () => NOW);
    await choose('evenings-no');
    expect(byId('field-mark').hasAttribute('hidden')).toBe(true);
    expect(byId('part-mark').hasAttribute('hidden')).toBe(false);
    expect(byId('first').hasAttribute('required')).toBe(true);
    expect(byId('middle').hasAttribute('required')).toBe(false);
    await choose('evenings-yes');
    expect(byId('field-mark').hasAttribute('hidden')).toBe(false);
    expect(byId('part-mark').hasAttribute('hidden')).toBe(true);
    expect(byId('first').hasAttribute('required')).toBe(false);
  });
});
