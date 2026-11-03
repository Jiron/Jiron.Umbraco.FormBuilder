// The designer's moves (ROADMAP 9.3c, ADR-0169): a drop's edit, where an element is after a move, the places Move
// to... offers with the edit each makes, and the keys' names.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignField } from '../definition';
import type { EditorState, FieldTraits, ModelContext } from '../editor-model';
import { modifierTerm, onMac } from './keys';
import { dropEdit, endOf, moveToPlaces, positionOf } from './moves';

const TRAITS: Record<string, FieldTraits> = {
  text: { kind: 'text', onePerForm: false, childFields: false },
  repeater: { kind: null, onePerForm: false, childFields: true },
  captcha: { kind: null, onePerForm: true, childFields: false },
};

function contextOf(): ModelContext {
  let next = 0;
  return { traitsOf: (type) => TRAITS[type], newKey: () => `new${++next}`, formulas: null };
}

const field = (key: string, type = 'text', extra: Partial<DesignField> = {}): DesignField => ({
  key,
  type,
  alias: key,
  label: { '': key },
  ...extra,
});

/** Page "one": "contact" (name, email, the repeatable group kids with kidName); page "two": "notes" (note, captcha). */
const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'one',
      groups: [
        {
          key: 'contact',
          fields: [field('name'), field('email'), field('kids', 'repeater', { fields: [field('kidName')] })],
        },
      ],
    },
    { key: 'two', groups: [{ key: 'notes', fields: [field('note'), field('captcha', 'captcha')] }] },
  ],
};

const state: EditorState = { definition: DEFINITION, selection: null };

describe("a drop's edit", () => {
  it('moves what is dragged to the place', () => {
    const edit = dropEdit(
      state,
      { kind: 'field', key: 'name', label: 'name' },
      { container: 'notes', index: 1 },
      contextOf(),
      () => null,
    );
    expect(edit?.ok && edit.state.definition.pages[1]?.groups[0]?.fields.map((each) => each.key)).toEqual([
      'note',
      'name',
      'captcha',
    ]);
  });

  it('adds a new field of the type dragged, and is refused as an insert is', () => {
    const made = dropEdit(
      state,
      { kind: 'new', type: 'text', label: 'Short text' },
      { container: 'contact', index: 0 },
      contextOf(),
      () => field('fresh'),
    );
    expect(made?.ok && made.state.selection).toBe('fresh');
    const second = dropEdit(
      state,
      { kind: 'new', type: 'captcha', label: 'Captcha' },
      { container: 'contact', index: 0 },
      contextOf(),
      () => field('another', 'captcha'),
    );
    expect(second).toEqual({ ok: false, refusal: 'onePerForm' });
    const inRepeater = dropEdit(
      state,
      { kind: 'new', type: 'repeater', label: 'Repeatable group' },
      { container: 'kids', index: 0 },
      contextOf(),
      () => field('inner', 'repeater', { fields: [] }),
    );
    expect(inRepeater).toEqual({ ok: false, refusal: 'notInRepeater' });
  });

  it('is none for a type the site no longer has', () => {
    expect(
      dropEdit(
        state,
        { kind: 'new', type: 'gone', label: 'Gone' },
        { container: 'contact', index: 0 },
        contextOf(),
        () => null,
      ),
    ).toBeNull();
  });
});

describe('where an element is', () => {
  it('counts a field among its group or repeatable group, with its page', () => {
    expect(positionOf(DEFINITION, 'email')).toEqual({
      kind: 'field',
      position: 2,
      count: 3,
      container: 'contact',
      page: 0,
    });
    expect(positionOf(DEFINITION, 'kidName')).toEqual({
      kind: 'field',
      position: 1,
      count: 1,
      container: 'kids',
      page: 0,
    });
  });

  it('counts a group on its page, and a page among the pages', () => {
    expect(positionOf(DEFINITION, 'notes')).toEqual({
      kind: 'group',
      position: 1,
      count: 1,
      container: 'two',
      page: 1,
    });
    expect(positionOf(DEFINITION, 'two')).toEqual({ kind: 'page', position: 2, count: 2, container: null, page: 1 });
    expect(positionOf(DEFINITION, 'nowhere')).toBeUndefined();
  });

  it('knows the end of a page, a group and a repeatable group', () => {
    expect(endOf(DEFINITION, 'one')).toEqual({ container: 'one', index: 1 });
    expect(endOf(DEFINITION, 'contact')).toEqual({ container: 'contact', index: 3 });
    expect(endOf(DEFINITION, 'kids')).toEqual({ container: 'kids', index: 1 });
    expect(endOf(DEFINITION, 'nowhere')).toBeNull();
  });
});

describe('the places Move to... offers', () => {
  it('gives a field every group and repeatable group by page, with where it is now and what the form refuses', () => {
    const sections = moveToPlaces(state, 'captcha', contextOf());
    expect(
      sections.map((section) => [
        section.page,
        section.places.map((place) => [place.key, place.kind, place.within, place.current]),
      ]),
    ).toEqual([
      [
        0,
        [
          ['contact', 'group', null, false],
          ['kids', 'repeater', 'contact', false],
        ],
      ],
      [1, [['notes', 'group', null, true]]],
    ]);
    const [contact, kids] = sections[0]?.places ?? [];
    expect(contact?.edit.ok).toBe(true);
    expect(kids?.edit).toEqual({ ok: false, refusal: 'notInRepeater' });
    // The captcha is last in its group already.
    expect(sections[1]?.places[0]?.edit).toEqual({ ok: false, refusal: 'unchanged' });
  });

  it('never offers a repeatable group a place inside itself, nor in what it holds', () => {
    const places = moveToPlaces(state, 'kids', contextOf()).flatMap((section) =>
      section.places.map((place) => place.key),
    );
    expect(places).toEqual(['contact', 'notes']);
    // A draft from elsewhere may hold a repeatable group in another; the model refuses to make one.
    const nested: EditorState = {
      definition: {
        pages: [
          {
            key: 'one',
            groups: [
              {
                key: 'contact',
                fields: [
                  field('outer', 'repeater', { fields: [field('inner', 'repeater', { fields: [field('x')] })] }),
                ],
              },
            ],
          },
        ],
      },
      selection: null,
    };
    const offered = moveToPlaces(nested, 'outer', contextOf()).flatMap((section) =>
      section.places.map((place) => place.key),
    );
    expect(offered).toEqual(['contact']);
  });

  it('gives a group the pages, and a page none', () => {
    const [pages] = moveToPlaces(state, 'contact', contextOf());
    expect(pages?.page).toBe(-1);
    expect(pages?.places.map((place) => [place.key, place.kind, place.current])).toEqual([
      ['one', 'page', true],
      ['two', 'page', false],
    ]);
    // To the end of the page.
    const two = pages?.places[1]?.edit;
    expect(two?.ok && two.state.definition.pages[1]?.groups.map((group) => group.key)).toEqual(['notes', 'contact']);
    expect(moveToPlaces(state, 'one', contextOf())).toEqual([]);
    expect(moveToPlaces(state, 'nowhere', contextOf())).toEqual([]);
  });

  it('changes nothing it is given', () => {
    const before = JSON.stringify(DEFINITION);
    moveToPlaces(state, 'name', contextOf());
    expect(JSON.stringify(DEFINITION)).toBe(before);
  });
});

describe("the keys' names", () => {
  it('are Cmd on a Mac and an iPad, and Ctrl elsewhere', () => {
    expect(onMac('Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15')).toBe(true);
    expect(onMac('Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X)')).toBe(true);
    expect(onMac('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Edg/140.0')).toBe(false);
    expect(modifierTerm(true)).toBe('jfbDesigner_keyCmd');
    expect(modifierTerm(false)).toBe('jfbDesigner_keyCtrl');
  });
});
