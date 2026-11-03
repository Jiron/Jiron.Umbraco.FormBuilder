// The inspector's option editor (ROADMAP 9.4b, ADR-0172): where a field's options come from, a list with its count or
// what it comes from, a standard list, a kind configured through its served schema, the field's own options added,
// labelled, pasted, moved and removed with what each says, an option's details, a parent and the answers an option is
// shown for, and a grid's rows. A stand-in inspector applies each change to the field, as the Design view does.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, testHost, text } from '../../testing';
import type { DesignField } from '../definition';
import { builtInTypes, optionSources } from '../preview/fixtures';
import { OptionEditorElement } from './option-editor.element';

// Umbraco's media picker: what it was opened with, and what it answers; closed without a choice when there is none.
const modal = vi.hoisted(() => ({ value: undefined as unknown, opened: [] as unknown[] }));
vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: unknown }) => {
    modal.opened.push(args?.data);
    if (modal.value === undefined) throw new Error('closed');
    return modal.value;
  },
}));

// The middle dot between a list's name and what it holds.
const DOT = String.fromCharCode(0xb7);

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

const RADIO: DesignField = {
  key: 'r',
  type: 'radio',
  alias: 'colour',
  label: { '': 'Colour' },
  options: {
    items: [
      { value: 'red', label: { '': 'Red' } },
      { value: 'green', label: { '': 'Green' } },
    ],
  },
};

async function editorOf(field: DesignField, extra: Partial<OptionEditorElement> = {}) {
  const element = new OptionEditorElement();
  const changes: { name: string; typed: string | undefined }[] = [];
  Object.assign(element, {
    field,
    type: must(builtInTypes[field.type], field.type),
    culture: 'en-US',
    defaultCulture: 'en-US',
    sources: optionSources,
    lists: [],
    ...extra,
  });
  element.change = (update, name, typed) => {
    changes.push({ name, typed });
    element.field = update(element.field);
  };
  testHost().append(element);
  await settle(element);
  return { element, changes, options: () => element.field['options'] as Record<string, unknown> | undefined };
}

const choose = (select: HTMLSelectElement, value: string) => {
  select.value = value;
  select.dispatchEvent(new Event('change'));
};

const type = (box: HTMLInputElement | HTMLTextAreaElement, value: string, event = 'input') => {
  box.value = value;
  box.dispatchEvent(new Event(event));
};

const press = (button: Element) => button.dispatchEvent(new Event('click'));

const said = async (element: OptionEditorElement) => {
  await new Promise((done) => requestAnimationFrame(done));
  await settle(element);
  return text(element.querySelector('[role="status"]'));
};

describe('the option editor', () => {
  it('offers where options come from, and a new source keeps only what it holds', async () => {
    const { element, options, changes } = await editorOf(RADIO);
    const from = must(element.querySelector<HTMLSelectElement>('#options-source'), 'Options from');
    expect([...from.options].map((option) => [option.value, text(option)])).toEqual([
      ['items', 'This field'],
      ['list', 'An option list'],
      ['code', 'A standard list'],
      ['content', 'Pages of the site'],
      ['dataType', 'A data type'],
      ['countries', 'The country list'],
      ['url', 'A JSON address'],
    ]);
    choose(from, 'code');
    await settle(element);
    expect(options()).toEqual({ source: 'code', config: {} });
    const standard = must(element.querySelector<HTMLSelectElement>('#options-code'), 'the standard list');
    expect([...standard.options].map((option) => text(option))).toEqual([
      'Choose a list',
      'Months',
      'Days of the week',
      'Agreement, from strongly disagree to strongly agree',
      'Satisfaction, from very dissatisfied to very satisfied',
      'Yes, no, not sure',
    ]);
    choose(standard, 'months');
    expect(options()).toEqual({ source: 'code', config: { source: 'months' } });
    expect(changes.map((each) => each.name)).toEqual(['Options from', 'Standard list']);
  });

  it('offers only the kinds the site serves, a standard list only with a source, and keeps a source it lacks', async () => {
    const served = (kinds: string[], sources = optionSources.sources) => ({
      kinds: optionSources.kinds.filter((kind) => kinds.includes(kind.kind)),
      sources,
    });
    const offered = (element: OptionEditorElement) =>
      [...must(element.querySelector<HTMLSelectElement>('#options-source'), 'Options from').options].map(
        (option) => option.value,
      );
    expect(offered((await editorOf(RADIO, { sources: served(['countries']) })).element)).toEqual([
      'items',
      'list',
      'countries',
    ]);
    expect(offered((await editorOf(RADIO, { sources: served(['code', 'url'], []) })).element)).toEqual([
      'items',
      'list',
      'url',
    ]);
    const lacking = await editorOf({ ...RADIO, options: { source: 'url', config: {} } }, { sources: served([]) });
    expect(offered(lacking.element)).toEqual(['items', 'list', 'url']);
    expect(must(lacking.element.querySelector<HTMLSelectElement>('#options-source'), 'Options from').value).toBe('url');
  });

  it("lists the library's lists with how many options they hold, or where they come from", async () => {
    const lists = [
      { key: 'l1', alias: 'rooms', name: 'Rooms', kind: 'manual', count: 3 },
      { key: 'l2', alias: 'products', name: 'Products', kind: 'content', count: null },
    ];
    const { element, options } = await editorOf({ ...RADIO, options: { source: 'list' } }, { lists });
    const list = must(element.querySelector<HTMLSelectElement>('#options-list'), 'the list');
    expect([...list.options].map((option) => text(option))).toEqual([
      'Choose a list',
      `Rooms ${DOT} 3 options`,
      `Products ${DOT} pages of the site`,
    ]);
    choose(list, 'l1');
    expect(options()).toEqual({ source: 'list', list: 'l1' });
    const none = await editorOf({ ...RADIO, options: { source: 'list' } }, { lists: [] });
    expect(text(none.element.querySelector('.help'))).toBe('There are no option lists yet.');
  });

  it("configures a kind through its served schema, at the field's options.config", async () => {
    const { element, options } = await editorOf({ ...RADIO, options: { source: 'countries', config: {} } });
    const countries = must(element.querySelector<HTMLTextAreaElement>('#setting-config-countries'), 'the countries');
    expect(text(element.querySelector('label[for="setting-config-countries"]'))).toBe('Countries');
    type(countries, 'CH\nDE');
    expect(options()).toEqual({ source: 'countries', config: { countries: ['CH', 'DE'] } });
  });

  it("picks the pages' root with Umbraco's content picker, and their types and a data type from the site's (ADR-0173)", async () => {
    const picked = (box: Element, selection: string[]) => {
      Object.assign(box, { selection });
      box.dispatchEvent(new Event('change'));
    };
    // The site's page types come with the setting as its choices, as the endpoint serves them.
    const sources = {
      ...optionSources,
      kinds: optionSources.kinds.map((kind) => ({
        ...kind,
        settings: kind.settings.map((setting) =>
          setting.name === 'documentTypes'
            ? {
                ...setting,
                choices: [
                  { value: 'type-1', label: 'Article' },
                  { value: 'type-2', label: 'Event' },
                ],
              }
            : setting,
        ),
      })),
    };
    const content = await editorOf({ ...RADIO, options: { source: 'content', config: {} } }, { sources });
    const root = must(content.element.querySelector('umb-input-document#setting-config-root'), 'the root');
    expect((root as unknown as { max: number }).max).toBe(1);
    picked(root, ['page-1']);
    const types = must(content.element.querySelector('fieldset#setting-config-documentTypes'), 'types');
    expect(text(types.querySelector('legend'))).toBe('Document types');
    expect(types.getAttribute('aria-describedby')).toBe('setting-config-documentTypes-help');
    const ticks = () => [...types.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(ticks().map((tick) => text(types.querySelector(`label[for="${tick.id}"]`)))).toEqual(['Article', 'Event']);
    ticks()[1]?.click();
    ticks()[0]?.click();
    expect(content.options()).toEqual({
      source: 'content',
      config: { root: 'page-1', documentTypes: ['type-1', 'type-2'] },
    });
    picked(root, []);
    expect(content.options()?.['config']).toEqual({ documentTypes: ['type-1', 'type-2'] });

    // A type the site no longer has stays ticked, to be taken away.
    const gone = await editorOf(
      { ...RADIO, options: { source: 'content', config: { documentTypes: ['type-9'] } } },
      { sources },
    );
    const goneTicks = [...gone.element.querySelectorAll<HTMLInputElement>('#setting-config-documentTypes input')];
    expect(
      goneTicks.map((tick) => [tick.checked, text(gone.element.querySelector(`label[for="${tick.id}"]`))]),
    ).toEqual([
      [false, 'Article'],
      [false, 'Event'],
      [true, 'One the site no longer has'],
    ]);
    goneTicks[2]?.click();
    expect(gone.options()).toEqual({ source: 'content', config: {} });

    // The data types that hold values, as the snapshot serves Umbraco's own; one is needed, so "None chosen" can't be.
    const dataType = await editorOf({ ...RADIO, options: { source: 'dataType', config: {} } });
    const select = must(
      dataType.element.querySelector<HTMLSelectElement>('select#setting-config-dataType'),
      'data type',
    );
    expect([...select.options].map((option) => [text(option), option.disabled])).toEqual([
      ['None chosen', true],
      ['Checkbox list', false],
      ['Dropdown', false],
      ['Dropdown multiple', false],
      ['Radiobox', false],
    ]);
    expect(select.value).toBe('');
    choose(select, '0b6a45e7-44ba-430d-9da5-4e46060b9e03');
    expect(dataType.options()).toEqual({
      source: 'dataType',
      config: { dataType: '0b6a45e7-44ba-430d-9da5-4e46060b9e03' },
    });

    // A site without any says so.
    const none = {
      ...optionSources,
      kinds: optionSources.kinds.map((kind) => ({
        ...kind,
        settings: kind.settings.map((setting) => ({ ...setting, choices: [] })),
      })),
    };
    const empty = await editorOf({ ...RADIO, options: { source: 'dataType', config: {} } }, { sources: none });
    expect(text(empty.element.querySelector('#setting-config-dataType'))).toBe('The site has none to choose from.');
  });

  it('adds, labels, pastes, moves and removes its own options, saying what each did', async () => {
    const { element, options, changes } = await editorOf(RADIO);
    const values = () => ((options()?.['items'] as { value: string }[] | undefined) ?? []).map((item) => item.value);
    press(must(element.querySelector('.row-actions uui-button'), 'Add option'));
    await settle(element);
    expect(values()).toEqual(['red', 'green', 'Option 3']);

    const boxes = () => [...element.querySelectorAll<HTMLInputElement>('.row-label')];
    expect(boxes().map((box) => box.getAttribute('aria-label'))).toEqual([
      'Label of option 1',
      'Label of option 2',
      'Label of option 3',
    ]);
    type(must(boxes()[2], 'the third label'), 'Blue');
    expect((options()?.['items'] as { label?: unknown }[])[2]?.label).toEqual({ '': 'Blue' });
    expect(changes.at(-1)?.typed).toBe('option-Option 3-label');

    press(must(element.querySelectorAll('.row-actions uui-button')[1], 'Paste options'));
    await settle(element);
    type(
      must(element.querySelector<HTMLTextAreaElement>('#options-paste'), 'the pasted text'),
      'Yellow\nred\n\nPurple',
    );
    press(must(element.querySelector('.paste uui-button'), 'Add these options'));
    expect(values()).toEqual(['red', 'green', 'Option 3', 'Yellow', 'Purple']);
    expect(await said(element)).toBe('Added 2 options. 1 line was an option already.');

    const handles = () => [...element.querySelectorAll<HTMLElement>('.row-handle')];
    expect(handles()[0]?.getAttribute('aria-label')).toBe('Move Red');
    handles()[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
    expect(values()).toEqual(['green', 'red', 'Option 3', 'Yellow', 'Purple']);
    expect(await said(element)).toBe('Moved. Red: position 2 of 5.');
    handles()[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', altKey: true, bubbles: true }));
    expect(await said(element)).toBe("It can't move any further.");

    const remove = must(element.querySelector('[aria-label="Remove Yellow"]'), 'Remove Yellow');
    press(remove);
    expect(values()).toEqual(['green', 'red', 'Option 3', 'Purple']);
    expect(await said(element)).toBe('Removed Yellow.');
  });

  it("puts the focus on the row moved, the option added and the row in a removed one's place once the field is back", async () => {
    const { element } = await editorOf(RADIO);
    // As in the Design view, the changed field comes back later, after the history has taken the change.
    element.change = (update) => {
      setTimeout(() => (element.field = update(element.field)), 20);
    };
    const back = async () => {
      await new Promise((done) => setTimeout(done, 40));
      await settle(element);
    };
    const focused = () => document.activeElement?.getAttribute('aria-label');
    const handle = (label: string) => must(element.querySelector<HTMLElement>(`[aria-label="Move ${label}"]`), label);

    handle('Red').focus();
    handle('Red').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
    await back();
    expect(focused()).toBe('Move Red');

    press(must(element.querySelector('.row-actions uui-button'), 'Add option'));
    await back();
    expect(focused()).toBe('Label of option 3');

    press(must(element.querySelector('[aria-label="Remove Red"]'), 'Remove Red'));
    await back();
    expect(focused()).toBe('Move Option 3');
  });

  it("keeps the canvas's Alt+Up and Alt+Down for the canvas, outside a row's handle", async () => {
    const { element } = await editorOf(RADIO);
    const outside = vi.fn();
    document.body.addEventListener('keydown', outside);
    try {
      element
        .querySelector('.row-handle')
        ?.dispatchEvent(
          new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true, composed: true }),
        );
      expect(outside).not.toHaveBeenCalled();
    } finally {
      document.body.removeEventListener('keydown', outside);
    }
  });

  it("opens an option's details: what it stores, its places, and on cards its description and image", async () => {
    const { element, options } = await editorOf(RADIO, { cards: true });
    const toggle = must(element.querySelector<HTMLElement>('[aria-label="Details of Red"]'), 'Details of Red');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    press(toggle);
    await settle(element);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const details = must(element.querySelector('#option-0-details'), 'the details');
    expect([...details.querySelectorAll('.label')].map((label) => text(label))).toEqual([
      'Stored as',
      'Description',
      'Image',
      'Places',
    ]);

    const value = must(details.querySelector<HTMLInputElement>('#option-0-value'), 'Stored as');
    type(value, 'GREEN', 'change');
    await settle(element);
    expect(text(details.querySelector('#option-0-value-why'))).toBe('Another option stores this already.');
    expect(value.getAttribute('aria-invalid')).toBe('true');
    type(value, ' red', 'change');
    await settle(element);
    expect(text(element.querySelector('#option-0-value-why'))).toBe(
      'Use 1 to 200 characters, without spaces at either end.',
    );
    type(value, 'crimson', 'change');
    await settle(element);
    expect((options()?.['items'] as { value: string }[])[0]?.value).toBe('crimson');

    const places = must(element.querySelector<HTMLInputElement>('#option-0-places'), 'Places');
    type(places, 'many', 'change');
    await settle(element);
    expect(text(element.querySelector('#option-0-places-why'))).toBe(
      'Enter a whole number from 0 to 1,000,000, or nothing for no limit.',
    );
    type(places, '1000001', 'change');
    await settle(element);
    expect(places.getAttribute('aria-invalid')).toBe('true');
    type(places, '1000000', 'change');
    await settle(element);
    expect(places.getAttribute('aria-invalid')).toBe('false');
    expect((options()?.['items'] as { places?: number }[])[0]?.places).toBe(1_000_000);
    type(places, '12', 'change');
    expect((options()?.['items'] as { places?: number }[])[0]?.places).toBe(12);
    type(places, '', 'change');
    expect((options()?.['items'] as { places?: number }[])[0]).not.toHaveProperty('places');

    type(must(element.querySelector<HTMLTextAreaElement>('#option-0-description'), 'the description'), 'Bright');
    expect((options()?.['items'] as { description?: unknown }[])[0]?.description).toEqual({ '': 'Bright' });
  });

  it('shows a description and an image only on cards, and places only where the type counts them', async () => {
    const labels = async (field: DesignField) => {
      const { element } = await editorOf(field);
      press(must(element.querySelector('[aria-label="Details of Red"]'), 'Details of Red'));
      await settle(element);
      return [...must(element.querySelector('#option-0-details'), 'the details').querySelectorAll('.label')].map(
        (label) => text(label),
      );
    };
    expect(await labels(RADIO)).toEqual(['Stored as', 'Places']);
    // Its own layout is no longer what decides: the inspector says whether it shows as cards.
    expect(await labels({ ...RADIO, settings: { layout: 'cards' } })).toEqual(['Stored as', 'Places']);
    expect(await labels({ ...RADIO, type: 'ranking' })).toEqual(['Stored as']);
  });

  it('puts an image chosen in the media picker on the option', async () => {
    modal.value = { selection: ['media-1'] };
    const { element, options } = await editorOf(RADIO, { cards: true });
    press(must(element.querySelector('[aria-label="Details of Red"]'), 'Details of Red'));
    await settle(element);
    press(must(element.querySelector('#option-0-details .row-actions uui-button'), 'Choose image'));
    await vi.waitFor(() => expect((options()?.['items'] as { image?: string }[])[0]?.image).toBe('media-1'));
    expect(modal.opened).toEqual([{ multiple: false }]);
    await settle(element);
    expect(text(element.querySelector('#option-0-details .help:not([id])'))).toBe('An image is chosen.');
    modal.value = undefined;
    press(must(element.querySelector('#option-0-details .row-actions uui-button'), 'Choose image'));
    await settle(element);
    expect((options()?.['items'] as { image?: string }[])[0]?.image).toBe('media-1');
  });

  it("shows another language's own words, empty with the default's as a hint where it has none", async () => {
    const { element } = await editorOf(
      {
        ...RADIO,
        options: {
          items: [
            { value: 'red', label: { '': 'Red', 'de-DE': 'Rot' } },
            { value: 'green', label: { '': 'Green' } },
          ],
        },
      },
      { culture: 'de-DE', defaultCulture: 'en-US' },
    );
    const boxes = [...element.querySelectorAll<HTMLInputElement>('.row-label')];
    expect(boxes.map((box) => [box.value, box.placeholder])).toEqual([
      ['Rot', 'Red'],
      ['', 'Green'],
    ]);
  });

  it("shows a kind's settings with their defaults where the field leaves them out", async () => {
    const { element } = await editorOf({ ...RADIO, options: { source: 'url', config: {} } });
    expect(must(element.querySelector<HTMLInputElement>('#setting-config-listPath'), 'Where the list is').value).toBe(
      '$',
    );
  });

  it('removes one of two options that store the same, which only a draft written elsewhere holds', async () => {
    const twice: DesignField = {
      ...RADIO,
      options: { items: [{ value: 'red' }, { value: 'blue' }, { value: 'red' }] },
    };
    const { element, options } = await editorOf(twice);
    expect(element.querySelectorAll('.option-row')).toHaveLength(3);
    press(must(element.querySelectorAll('[aria-label="Remove red"]')[1], 'the second red'));
    expect((options()?.['items'] as { value: string }[]).map((item) => item.value)).toEqual(['red', 'blue']);
  });

  it('keeps the focus in the box of a value it changed, once the field is back', async () => {
    const { element } = await editorOf(RADIO);
    element.change = (update) => {
      setTimeout(() => (element.field = update(element.field)), 20);
    };
    press(must(element.querySelector('[aria-label="Details of Red"]'), 'Details of Red'));
    await settle(element);
    const value = must(element.querySelector<HTMLInputElement>('#option-0-value'), 'Stored as');
    value.focus();
    type(value, 'crimson', 'change');
    await new Promise((done) => setTimeout(done, 40));
    await settle(element);
    expect(document.activeElement?.id).toBe('option-0-value');
    expect((document.activeElement as HTMLInputElement).value).toBe('crimson');
  });

  it("offers Depends on only for a type whose options may follow another field's answer, and not a grid's columns", async () => {
    const parent: DesignField = { key: 'p', type: 'dropdown', alias: 'size', options: { items: [{ value: 's' }] } };
    const grid: DesignField = { key: 'g', type: 'grid', alias: 'rating', options: { items: [{ value: 'poor' }] } };
    const columns = await editorOf(grid, { parents: [parent] });
    expect(columns.element.querySelector('#options-parent')).toBeNull();
    const radio = await editorOf(RADIO, { parents: [parent] });
    expect(radio.element.querySelector('#options-parent')).not.toBeNull();
    expect((await editorOf(RADIO)).element.querySelector('#options-parent')).toBeNull();
  });

  it('depends on a choice field before it, each own option shown for the answers ticked', async () => {
    const parent: DesignField = {
      key: 'p',
      type: 'dropdown',
      alias: 'size',
      label: { '': 'Size' },
      options: { items: [{ value: 's', label: { '': 'Small' } }, { value: 'l' }] },
    };
    const { element, options } = await editorOf({ ...RADIO, type: 'dropdown' }, { parents: [parent] });
    const depends = must(element.querySelector<HTMLSelectElement>('#options-parent'), 'Depends on');
    expect([...depends.options].map((option) => text(option))).toEqual(['No other field', 'Size']);
    choose(depends, 'p');
    await settle(element);
    expect(options()?.['parent']).toBe('p');
    press(must(element.querySelector('[aria-label="Details of Red"]'), 'Details of Red'));
    await settle(element);
    const boxes = [...element.querySelectorAll<HTMLInputElement>('#option-0-details input[type="checkbox"]')];
    expect(boxes.map((box) => text(box.closest('label')))).toEqual(['Small', 'l']);
    must(boxes[1], 'l').checked = true;
    boxes[1]?.dispatchEvent(new Event('change'));
    expect((options()?.['items'] as { parentValues?: string[] }[])[0]?.parentValues).toEqual(['l']);

    // Another parent, or none, means the answers ticked mean nothing: each option is shown for all again.
    choose(depends, '');
    await settle(element);
    expect(options()).toEqual({
      items: [
        { value: 'red', label: { '': 'Red' } },
        { value: 'green', label: { '': 'Green' } },
      ],
    });
  });

  it('keeps an answer the parent no longer offers ticked under its value, so it can be unticked', async () => {
    const parent: DesignField = { key: 'p', type: 'dropdown', alias: 'size', options: { items: [{ value: 's' }] } };
    const field: DesignField = {
      ...RADIO,
      type: 'dropdown',
      options: { parent: 'p', items: [{ value: 'red', parentValues: ['s', 'xl'] }] },
    };
    const { element, options } = await editorOf(field, { parents: [parent] });
    press(must(element.querySelector('[aria-label="Details of red"]'), 'Details of red'));
    await settle(element);
    const boxes = [...element.querySelectorAll<HTMLInputElement>('#option-0-details input[type="checkbox"]')];
    expect(boxes.map((box) => [text(box.closest('label')), box.checked])).toEqual([
      ['s', true],
      ['xl', true],
    ]);
    must(boxes[1], 'xl').checked = false;
    boxes[1]?.dispatchEvent(new Event('change'));
    expect((options()?.['items'] as { parentValues?: string[] }[])[0]?.parentValues).toEqual(['s']);
  });

  it("edits a grid's rows: adds, pastes, requires and removes them, each with a key of its own", async () => {
    const grid: DesignField = { key: 'g', type: 'grid', alias: 'rating', label: { '': 'Rating' } };
    const { element } = await editorOf(grid, { edits: 'rows' });
    const rows = () =>
      (element.field['rows'] as { key: string; label?: unknown; required?: boolean }[] | undefined) ?? [];
    expect(text(element.querySelector('.help'))).toBe(
      'No rows yet. Each row is a statement visitors answer on the columns.',
    );
    press(must(element.querySelector('.row-actions uui-button'), 'Add row'));
    await settle(element);
    expect(rows().map((row) => row.label)).toEqual([{ '': 'Row 1' }]);
    press(must(element.querySelectorAll('.row-actions uui-button')[1], 'Paste rows'));
    await settle(element);
    type(must(element.querySelector<HTMLTextAreaElement>('#rows-paste'), 'the pasted rows'), 'Food\nService');
    press(must(element.querySelector('.paste uui-button'), 'Add these rows'));
    await settle(element);
    expect(rows().map((row) => row.label)).toEqual([{ '': 'Row 1' }, { '': 'Food' }, { '': 'Service' }]);
    expect(new Set(rows().map((row) => row.key)).size).toBe(3);
    // "Needs an answer" is said once over the boxes, and each box names its row.
    expect([...element.querySelectorAll('.rows-caption')].map((caption) => text(caption))).toEqual(['Needs an answer']);
    const required = must(element.querySelector<HTMLInputElement>('#row-1-required'), 'Needs an answer');
    expect(required.getAttribute('aria-label')).toBe('Needs an answer: Food');
    required.checked = true;
    required.dispatchEvent(new Event('change'));
    expect(rows()[1]?.required).toBe(true);
    press(must(element.querySelector('[aria-label="Remove Row 1"]'), 'Remove Row 1'));
    expect(rows().map((row) => row.label)).toEqual([{ '': 'Food' }, { '': 'Service' }]);
  });
});
