// The canvas (ROADMAP 9.3b and 22.1, ADR-0168, ADR-0256): a field drawn by the element's views with nothing of it taking
// input, the designer's controls over it with their names, its label edited in place, a repeatable group's own fields,
// a note for a field visitors don't see; and the canvas around them: the row of tabs and tools above the sheet, the open
// tab's menu, a page's groups and its end, the empty states, After sending, a click on the empty room, "Narrowed to
// fit" and the requests each control sends. Its moves (ROADMAP 9.3c): each element's handle, a refusal shown at the
// element, the order Up and Down go through, and the focus on a handle or a tab.
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { DesignDefinition, DesignField } from '../definition';
import { fieldModel, type PreviewContext } from '../preview/field-models';
import { builtInTypes, englishLook } from '../preview/fixtures';
import {
  CanvasFieldElement,
  FIELD_REQUEST,
  type FieldRequest,
  type FieldState,
  type FieldWords,
} from './canvas-field.element';
import { CANVAS_REQUEST, FormCanvasElement, type CanvasRequest } from './form-canvas.element';

const context: PreviewContext = {
  look: englishLook,
  types: builtInTypes,
  culture: 'en-US',
  marker: 'optional',
  layout: 'vertical',
  captcha: null,
  prefix: 'test',
  showRows: true,
};

const WORDS: FieldWords = {
  name: 'Email address, email field',
  insertHere: 'Add a field here',
  insertAfter: 'Add a field here, after Email address',
  move: 'Move Email address',
  moveTo: 'Move to...',
  moveToNamed: 'Move to..., Email address',
  duplicate: 'Duplicate',
  duplicateNamed: 'Duplicate Email address',
  delete: 'Delete',
  deleteNamed: 'Delete Email address',
  more: 'More actions',
  newPageAfter: 'Move what follows to a new page',
  copyAlias: 'Copy alias',
  labelBox: 'Label',
  unseen: 'Hidden field: not shown to visitors',
  insertFirst: 'Add a field here, before Guest name',
};

const email: DesignField = { key: 'e', type: 'email', alias: 'email', label: { '': 'Email address' } };

function stateOf(field: DesignField, extra: Partial<FieldState> = {}): FieldState {
  return {
    key: field.key,
    model: fieldModel(field, context),
    words: WORDS,
    selected: false,
    editing: false,
    labelText: 'Email address',
    insertActive: false,
    ...extra,
  };
}

async function fieldOf(state: FieldState) {
  const element = new CanvasFieldElement();
  element.classes = englishLook.classes;
  element.state = state;
  const requests: FieldRequest[] = [];
  element.addEventListener(FIELD_REQUEST, (event) => requests.push((event as CustomEvent<FieldRequest>).detail));
  testHost().append(element);
  await settle(element);
  return { element, requests };
}

const controls = (element: Element) => must(element.querySelector(':scope > .bo-chrome'), 'the controls');

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('a field on the canvas', () => {
  it("is the field's wrapper as the views write it, and nothing the views wrote takes input", async () => {
    const { element } = await fieldOf(stateOf(email));
    expect([...element.classList]).toEqual(['bo-field', 'jfb-field', 'jfb-field-email']);
    expect(element.getAttribute('key')).toBe('e');
    expect(text(element.querySelector('label.jfb-label'))).toBe('Email address (optional)');
    const written = [...element.children].filter((child) => child !== controls(element));
    expect(written.map((child) => child.localName)).toEqual(['label', 'input']);
    expect(written.every((child) => (child as HTMLElement).inert)).toBe(true);
  });

  it('lies under a button that selects it, named for screen readers, and an insert point below it', async () => {
    const { element, requests } = await fieldOf(stateOf(email));
    const select = must(controls(element).querySelector<HTMLButtonElement>('.bo-select'), 'the select button');
    expect([select.getAttribute('aria-label'), select.getAttribute('aria-current')]).toEqual([WORDS.name, 'false']);
    expect(controls(element).querySelector('.bo-tools')).toBeNull();
    select.click();
    select.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    const insert = must(controls(element).querySelector<HTMLButtonElement>('.bo-insert-after'), 'the insert point');
    expect([insert.getAttribute('aria-label'), text(insert)]).toEqual([WORDS.insertAfter, 'Add a field here']);
    insert.click();
    expect(requests).toEqual([
      { kind: 'select', key: 'e' },
      { kind: 'editLabel', key: 'e' },
      { kind: 'insertAfter', key: 'e' },
    ]);
  });

  it('says it is selected, and shows Move to..., Duplicate, Delete and its "..." while it is, each named with the field', async () => {
    const { element, requests } = await fieldOf(stateOf(email, { selected: true }));
    expect(element.hasAttribute('selected')).toBe(true);
    expect(controls(element).querySelector('.bo-select')?.getAttribute('aria-current')).toBe('true');
    const tools = [...controls(element).querySelectorAll<HTMLButtonElement>('.bo-tools > button')];
    expect(tools.map((tool) => [tool.getAttribute('aria-label'), text(tool)])).toEqual([
      ['Move to..., Email address', 'Move to...'],
      ['Duplicate Email address', 'Duplicate'],
      ['Delete Email address', 'Delete'],
      ['More actions', ''],
    ]);
    const menu = must(controls(element).querySelector('.bo-tools uui-popover-container'), 'the menu');
    expect(tools[3]?.getAttribute('popovertarget')).toBe(menu.id);
    expect(menu.id).toBe('jfb-more-e');
    for (const tool of tools) tool.click();
    const items = [...menu.querySelectorAll<HTMLButtonElement>('button')];
    expect(items.map((item) => text(item))).toEqual(['Move what follows to a new page', 'Copy alias']);
    for (const item of items) item.click();
    expect(requests).toEqual([
      { kind: 'moveTo', key: 'e' },
      { kind: 'duplicate', key: 'e' },
      { kind: 'delete', key: 'e' },
      { kind: 'newPageAfter', key: 'e' },
      { kind: 'copyAlias', key: 'e' },
    ]);
  });

  it('offers no "..." where it would hold nothing: nothing follows the field, and it has no alias', async () => {
    const words: FieldWords = Object.fromEntries(
      Object.entries(WORDS).filter(([name]) => name !== 'copyAlias' && name !== 'newPageAfter'),
    ) as unknown as FieldWords;
    const { element } = await fieldOf({ ...stateOf(email, { selected: true }), words });
    expect([...controls(element).querySelectorAll('.bo-tools > button')].map((tool) => text(tool))).toEqual([
      'Move to...',
      'Duplicate',
      'Delete',
    ]);
  });

  it('offers Copy alias only for a field with an alias', async () => {
    const words: FieldWords = Object.fromEntries(
      Object.entries(WORDS).filter(([name]) => name !== 'copyAlias'),
    ) as unknown as FieldWords;
    const { element } = await fieldOf({ ...stateOf(email, { selected: true }), words });
    expect([...controls(element).querySelectorAll('.bo-menu button')].map((item) => text(item))).toEqual([
      'Move what follows to a new page',
    ]);
  });

  it('carries the chip of a field without a text in the language shown, as its description too, but not while selected', async () => {
    const { element } = await fieldOf(stateOf(email, { untranslated: 'No German text yet' }));
    const chip = must(controls(element).querySelector('.bo-chip'), 'the chip');
    expect(text(chip)).toBe('No German text yet');
    expect(controls(element).querySelector('.bo-select')?.getAttribute('aria-describedby')).toBe(chip.id);
    element.state = stateOf(email, { untranslated: 'No German text yet', selected: true });
    await element.updateComplete;
    expect(controls(element).querySelector('.bo-chip')).toBeNull();
    expect(controls(element).querySelector('.bo-select')?.hasAttribute('aria-describedby')).toBe(false);
  });

  it('has a handle named with the field, which a press drags and a click selects', async () => {
    const { element, requests } = await fieldOf(stateOf(email));
    const handle = must(controls(element).querySelector<HTMLButtonElement>(':scope > .bo-handle'), 'the handle');
    expect([handle.getAttribute('aria-label'), handle.dataset['key'], handle.dataset['control']]).toEqual([
      'Move Email address',
      'e',
      'handle',
    ]);
    const select = must(controls(element).querySelector<HTMLButtonElement>(':scope > .bo-select'), 'the select');
    expect([select.dataset['key'], select.dataset['control']]).toEqual(['e', 'select']);
    const press = new MouseEvent('pointerdown', { bubbles: true }) as PointerEvent;
    handle.dispatchEvent(press);
    handle.click();
    expect(requests).toEqual([
      { kind: 'drag', key: 'e', event: press, handle },
      { kind: 'select', key: 'e' },
    ]);
    element.focusField('handle');
    expect(document.activeElement).toBe(handle);
  });

  it('hides its handle while its label is edited, and shows why a move was refused beside it', async () => {
    const editing = await fieldOf(stateOf(email, { selected: true, editing: true }));
    expect(controls(editing.element).querySelector<HTMLElement>('.bo-handle')?.hidden).toBe(true);
    document.body.replaceChildren();
    const refused = await fieldOf(stateOf(email, { refusal: "It can't move any further." }));
    const note = must(controls(refused.element).querySelector('.bo-refusal'), 'the note');
    expect([text(note), note.getAttribute('aria-hidden')]).toEqual(["It can't move any further.", 'true']);
  });

  it('keeps its written markup and its buttons, which may hold the focus, when it is drawn again from the same model', async () => {
    const state = stateOf(email);
    const { element } = await fieldOf(state);
    const label = element.querySelector('label');
    const select = controls(element).querySelector('.bo-select');
    element.state = { ...state, selected: true };
    await settle(element);
    expect(element.querySelector('label')).toBe(label);
    expect(controls(element).querySelector('.bo-select')).toBe(select);
    // A new model is written again, the buttons staying where they are.
    element.state = { ...state, model: fieldModel({ ...email, label: { '': 'Your email' } }, context) };
    await settle(element);
    expect(element.querySelector('label')).not.toBe(label);
    expect(text(element.querySelector('label'))).toBe('Your email (optional)');
    expect(controls(element).querySelector('.bo-select')).toBe(select);
  });

  it('edits its label in place: Enter keeps the text, Escape puts the old one back', async () => {
    const { element, requests } = await fieldOf(stateOf(email, { editing: true }));
    const box = must(element.querySelector<HTMLInputElement>('label.jfb-label > input.bo-label-box'), 'the label box');
    expect([box.value, box.getAttribute('aria-label'), (box.closest('label') as HTMLElement).inert]).toEqual([
      'Email address',
      'Label',
      false,
    ]);
    expect(text(element.querySelector('label.jfb-label .jfb-mark'))).toBe('(optional)');
    expect(controls(element).querySelector('.bo-select')?.hasAttribute('hidden')).toBe(true);
    box.value = 'Your email';
    box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    box.dispatchEvent(new FocusEvent('blur'));
    expect(requests).toEqual([{ kind: 'label', key: 'e', text: 'Your email' }]);

    const other = await fieldOf(stateOf(email, { editing: true }));
    const again = must(other.element.querySelector<HTMLInputElement>('input.bo-label-box'), 'the label box');
    again.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(other.requests).toEqual([{ kind: 'label', key: 'e', text: null }]);
  });

  it('shows a label written for screen readers only while it is edited', async () => {
    const hidden = { ...email, hideLabel: true };
    const { element } = await fieldOf(stateOf(hidden, { editing: true }));
    const label = must(element.querySelector('label.jfb-label'), 'the label');
    expect([
      label.classList.contains('jfb-visually-hidden'),
      label.querySelector('input.bo-label-box') !== null,
    ]).toEqual([false, true]);
    const shown = await fieldOf(stateOf(hidden));
    expect(shown.element.querySelector('label.jfb-label')?.classList.contains('jfb-visually-hidden')).toBe(true);
  });

  it('ends an edit at once where no label shows, and keeps the button that selects the field', async () => {
    const heading: DesignField = { key: 'h', type: 'heading', settings: { text: {} } };
    const { element, requests } = await fieldOf(stateOf(heading, { editing: true }));
    await Promise.resolve();
    expect(requests).toEqual([{ kind: 'label', key: 'h', text: null }]);
    expect(controls(element).querySelector('.bo-select')?.hasAttribute('hidden')).toBe(false);
  });

  it('shows a note in the place of a field visitors do not see', async () => {
    const { element } = await fieldOf(stateOf({ key: 'h', type: 'hidden', alias: 'source', label: { '': 'Source' } }));
    expect(element.classList.contains('bo-unseen')).toBe(true);
    expect(text(element.querySelector(':scope > .bo-note'))).toBe(WORDS.unseen);
  });

  it("shows a repeatable group's first row with its own fields, each a canvas field, after an insert point", async () => {
    const guest: DesignField = { key: 'g', type: 'text', alias: 'guest', label: { '': 'Guest name' } };
    const repeater: DesignField = {
      key: 'r',
      type: 'repeater',
      alias: 'guests',
      label: { '': 'Guests' },
      fields: [guest],
    };
    const { element, requests } = await fieldOf(
      stateOf(repeater, { children: [stateOf(guest, { selected: true })], firstActive: true }),
    );
    const row = must(element.querySelector('fieldset.jfb-row > .jfb-fields'), "the row's fields");
    // A drag drops fields into it as into a group.
    expect(row.getAttribute('data-fields')).toBe('r');
    const nested = [...row.querySelectorAll<CanvasFieldElement>(':scope > jfb-canvas-field')];
    expect(nested.map((field) => field.key)).toEqual(['g']);
    expect(element.nestedField('g')).toBe(nested[0]);
    await settle(nested[0]);
    expect(nested[0]?.hasAttribute('selected')).toBe(true);
    // The way down to the row takes input; the rest of the group does not.
    expect((element.querySelector(':scope > fieldset') as HTMLElement).inert).toBe(false);
    expect((element.querySelector(':scope > fieldset > legend') as HTMLElement).inert).toBe(true);
    const first = must(
      row.querySelector<HTMLButtonElement>('.bo-row-start > .bo-insert-first'),
      'the first insert point',
    );
    expect(first.hasAttribute('data-active')).toBe(true);
    expect(first.getAttribute('aria-label')).toBe(WORDS.insertFirst);
    first.click();
    nested[0]?.querySelector<HTMLButtonElement>(':scope > .bo-chrome > .bo-select')?.click();
    expect(requests).toEqual([
      { kind: 'insertFirst', container: 'r' },
      { kind: 'select', key: 'g' },
    ]);
  });
});

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      title: { '': 'Your details' },
      groups: [
        {
          key: 'g1',
          title: { '': 'About you' },
          columns: 2,
          fields: [email, { key: 'n', type: 'text', alias: 'name', label: { '': 'Name' } }],
        },
        { key: 'g2', fields: [] },
      ],
    },
    { key: 'p2', groups: [{ key: 'g3', fields: [] }] },
  ],
  outcomes: [
    { key: 'o', kind: 'message', message: { '': '<p>Thanks, <strong>see you</strong>!</p><script>alert(1)</script>' } },
  ],
};

async function canvasOf(definition: DesignDefinition, extra: Partial<FormCanvasElement> = {}) {
  const element = new FormCanvasElement();
  Object.assign(element, { definition, look: englishLook, types: builtInTypes, ...extra });
  const requests: CanvasRequest[] = [];
  element.addEventListener(CANVAS_REQUEST, (event) => requests.push((event as CustomEvent<CanvasRequest>).detail));
  // In the document itself: jsdom gives the focus to nothing a shadow host leaves unslotted.
  document.body.append(element);
  await settle(element);
  return { element, requests, root: shadow(element) };
}

describe('the canvas', () => {
  it('shows the shape of the sheet while its look is read, busy, without the row above it', async () => {
    const { root } = await canvasOf(DEFINITION, { look: undefined });
    expect(root.querySelector('.bo-waiting')?.getAttribute('aria-busy')).toBe('true');
    expect(root.querySelectorAll('.bo-waiting .bo-bone')).toHaveLength(7);
    expect([root.querySelector('.bo-bar'), root.querySelector('.bo-pages'), root.querySelector('uui-loader')]).toEqual([
      null,
      null,
      null,
    ]);
  });

  it('shows the pages as tabs, then "+" and After sending, the page shown current, each name whole in its tip', async () => {
    const { element, requests, root } = await canvasOf(DEFINITION, { shown: 1 });
    const tabs = [...root.querySelectorAll<HTMLButtonElement>('.bo-tab-name')];
    expect(tabs.map((tab) => [text(tab), tab.title, tab.getAttribute('aria-current')])).toEqual([
      ['Your details', 'Your details', 'false'],
      ['Page 2', 'Page 2', 'step'],
    ]);
    expect(root.querySelector('.bo-tab[data-page="p2"]')?.hasAttribute('data-open')).toBe(true);
    const add = must(root.querySelector<HTMLButtonElement>('.bo-add-page'), 'Add page');
    expect([add.getAttribute('aria-label'), add.title, text(add)]).toEqual(['Add page', 'Add page', '']);
    const after = must(root.querySelector<HTMLButtonElement>('.bo-after-step'), 'After sending');
    expect([text(after), after.getAttribute('aria-current')]).toEqual(['After sending', 'false']);
    for (const button of [...tabs, add, after]) button.click();
    expect(requests).toEqual([
      { kind: 'show', shown: 0 },
      { kind: 'show', shown: 1 },
      { kind: 'addPage' },
      { kind: 'show', shown: 'after' },
    ]);
    expect(element.shown).toBe(1);
    // One control adds a page; the row holds the pages, then the tools at its end.
    expect(root.querySelectorAll('[aria-label="Add page"]')).toHaveLength(1);
    expect([...root.querySelectorAll('.bo-row > *')].map((part) => part.className)).toEqual(['bo-pages', 'bo-end']);
  });

  it("keeps the open tab's bold to its name, so the menu in it looks as the row's own menu does", () => {
    const styles = (customElements.get('jfb-form-canvas') as unknown as { styles: { cssText: string }[] }).styles
      .map((style) => style.cssText)
      .join('\n');
    expect(styles).toMatch(/\.bo-tab\[data-open\] \.bo-tab-name \{[^}]*font-weight: 700;/);
    expect(styles).not.toMatch(/\.bo-tab\[data-open\] \{[^}]*font-weight/);
    expect(styles).toMatch(/\.bo-tab uui-popover-container \{[^}]*color: var\(--uui-color-text\);/);
  });

  it('gives the sheet room for the chosen width, its padding and its two borders, so Desktop is 960 px of form', () => {
    const styles = (customElements.get('jfb-form-canvas') as unknown as { styles: { cssText: string }[] }).styles
      .map((style) => style.cssText)
      .join(' ');
    // The rule whose selector is the part alone, not one that holds it (`.bo-waiting .bo-sheet`).
    const rule = (part: string) =>
      styles
        .split('}')
        .map((one) => one.replace(/\/\*[\s\S]*?\*\//g, '').trim())
        .find((one) => one.startsWith(`${part} {`)) ?? '';
    for (const part of ['.bo-sheet', '.bo-narrowed', '.bo-under'])
      expect(rule(part)).toContain(
        'inline-size: min(100%, calc(var(--bo-width) + 2 * var(--bo-sheet-padding) + 2px));',
      );
    expect(rule('.bo-sheet')).toContain('border: 1px solid');
  });

  it("holds the open page's menu in its tab: Rename, Duplicate and Delete, and no Delete for a form's only page", async () => {
    const { requests, root } = await canvasOf(DEFINITION, { shown: 0 });
    const more = must(root.querySelector<HTMLButtonElement>('.bo-tab[data-open] .bo-more-element'), 'the "..."');
    expect(more.getAttribute('aria-label')).toBe('More for Your details');
    expect(root.querySelectorAll('.bo-tab .bo-more-element')).toHaveLength(1);
    const menu = must(root.querySelector(`#${more.getAttribute('popovertarget') ?? ''}`), 'the menu');
    const items = [...menu.querySelectorAll<HTMLButtonElement>('button')];
    expect(items.map((item) => text(item))).toEqual(['Rename', 'Duplicate', 'Delete']);
    for (const item of items) item.click();
    expect(requests).toEqual([
      { kind: 'renamePage', key: 'p1' },
      { kind: 'duplicate', key: 'p1' },
      { kind: 'delete', key: 'p1' },
    ]);
    document.body.replaceChildren();
    const one = await canvasOf({ pages: [{ key: 'only', groups: [{ key: 'g', fields: [] }] }] });
    expect([...one.root.querySelectorAll('.bo-tab .bo-menu button')].map((item) => text(item))).toEqual([
      'Rename',
      'Duplicate',
    ]);
    document.body.replaceChildren();
    const after = await canvasOf(DEFINITION, { shown: 'after' });
    expect(after.root.querySelector('.bo-tab .bo-more-element')).toBeNull();
  });

  it("lets the selection go with a click on the sheet's empty room, and leaves a click on a control to the control", async () => {
    const { requests, root } = await canvasOf(DEFINITION, { selection: 'e' });
    must(root.querySelector<HTMLElement>('.bo-page'), 'the page').click();
    must(root.querySelector<HTMLElement>('.bo-viewport'), 'the room around the sheet').click();
    must(root.querySelector<HTMLButtonElement>('.bo-add-field'), 'Add field').click();
    root.querySelector<HTMLButtonElement>('[data-select="g1"]')?.click();
    expect(requests).toEqual([
      { kind: 'clear' },
      { kind: 'clear' },
      { kind: 'addField', page: 'p1' },
      { kind: 'select', key: 'g1' },
    ]);
    document.body.replaceChildren();
    const none = await canvasOf(DEFINITION, { selection: null });
    must(none.root.querySelector<HTMLElement>('.bo-page'), 'the page').click();
    expect(none.requests).toEqual([]);
  });

  it('keeps the room of "Narrowed to fit" at the sheet\'s top edge, showing the words only where the sheet is narrowed', async () => {
    const { element, root } = await canvasOf(DEFINITION);
    const narrowed = must(root.querySelector<HTMLElement>('.bo-viewport > .bo-narrowed'), 'Narrowed to fit');
    expect(narrowed.nextElementSibling?.classList.contains('bo-sheet')).toBe(true);
    // jsdom lays nothing out: the sheet has no room, so it is narrowed.
    expect([text(narrowed), narrowed.hasAttribute('data-shown'), narrowed.getAttribute('aria-hidden')]).toEqual([
      'Narrowed to fit',
      true,
      'false',
    ]);
    const viewport = must(root.querySelector<HTMLElement>('.bo-viewport'), 'the viewport');
    Object.defineProperty(viewport, 'clientWidth', { value: 2000 });
    element.width = 'phone';
    await settle(element);
    expect([narrowed.hasAttribute('data-shown'), narrowed.getAttribute('aria-hidden')]).toEqual([false, 'true']);
    expect(root.querySelector('.bo-viewport > .bo-narrowed')).toBe(narrowed);
  });

  it('draws the page shown in the default theme: its title, its groups with their columns, its fields, and Next', async () => {
    const { root } = await canvasOf(DEFINITION);
    const form = must(root.querySelector('.bo-sheet > .jfb-form'), 'the form');
    expect(form.classList.contains('jfb-theme-default')).toBe(true);
    expect(text(root.querySelector('.jfb-page > h2.jfb-title'))).toBe('Your details');
    expect(root.querySelector('fieldset.jfb-group.jfb-columns-2 > legend')?.textContent).toBe('About you');
    expect(all<CanvasFieldElement>(root.host, 'jfb-canvas-field').map((field) => field.key)).toEqual(['e', 'n']);
    const navigation = must(root.querySelector('.jfb-navigation'), 'the buttons');
    expect([text(navigation), (navigation as HTMLElement).inert]).toEqual(['Next', true]);
  });

  it("keeps the page's buttons and watches its groups for being far from the view (section 4.12, ADR-0176)", async () => {
    const watched: Element[] = [];
    const real = globalThis.IntersectionObserver;
    globalThis.IntersectionObserver = class {
      observe = (group: Element) => watched.push(group);
      unobserve = (): undefined => undefined;
      disconnect = (): undefined => undefined;
    } as unknown as typeof IntersectionObserver;
    try {
      const { element, root } = await canvasOf(DEFINITION);
      const navigation = root.querySelector('.jfb-navigation');
      expect(watched).toEqual([...root.querySelectorAll('.bo-page > [data-group]')]);
      expect(watched).toHaveLength(2);
      // A selection draws the page again: its buttons are the same node, so the theme's rules that look inside the
      // form have nothing to look at again.
      element.selection = 'e';
      await settle(element);
      expect(root.querySelector('.jfb-navigation')).toBe(navigation);
      expect(watched).toHaveLength(2);
    } finally {
      globalThis.IntersectionObserver = real;
    }
  });

  it("names each group's button, gives the page none, and names an empty group's place for a field", async () => {
    const { requests, root } = await canvasOf(DEFINITION, { selection: 'g1' });
    const groups = [...root.querySelectorAll<HTMLButtonElement>('.bo-select-group')];
    expect(groups.map((group) => [group.getAttribute('aria-label'), group.getAttribute('aria-current')])).toEqual([
      ['About you, group', 'true'],
      ['Group 2, group', 'false'],
    ]);
    expect(root.querySelector('.bo-page > .bo-select')).toBeNull();
    expect(
      [...root.querySelectorAll('.bo-group > .bo-tools > button')].map((tool) => tool.getAttribute('aria-label')),
    ).toEqual(['Move to\u2026, About you', 'Duplicate About you', 'Delete About you', 'More actions']);
    const groupMenu = [...root.querySelectorAll<HTMLButtonElement>('.bo-group > .bo-tools .bo-menu button')];
    expect(groupMenu.map((item) => text(item))).toEqual(['Move what follows to a new page']);
    groupMenu[0]?.click();
    expect(root.querySelector('.bo-page > .bo-tools uui-popover-container')).toBeNull();
    const empty = must(root.querySelector<HTMLButtonElement>('.bo-empty-group'), "the empty group's place");
    expect(empty.getAttribute('aria-label')).toBe('Add a field here, in Group 2');
    empty.click();
    root.querySelector<HTMLButtonElement>('.bo-insert-first')?.click();
    root.querySelector<HTMLButtonElement>('.bo-add-field')?.click();
    expect(requests).toEqual([
      { kind: 'newPageAfter', key: 'g1' },
      { kind: 'insertFirst', container: 'g2' },
      { kind: 'insertFirst', container: 'g1' },
      { kind: 'addField', page: 'p1' },
    ]);
  });

  it('offers Save group as template in a group\'s "..." only to who may save groups', async () => {
    const { requests, root } = await canvasOf(DEFINITION, { selection: 'g1', saveGroups: true });
    const groupMenu = [...root.querySelectorAll<HTMLButtonElement>('.bo-group > .bo-tools .bo-menu button')];
    expect(groupMenu.map((item) => text(item))).toEqual([
      'Move what follows to a new page',
      'Save group as template\u2026',
    ]);
    groupMenu[1]?.click();
    expect(requests).toEqual([{ kind: 'saveGroup', key: 'g1' }]);
    document.body.replaceChildren();
    const without = await canvasOf(DEFINITION, { selection: 'g1', saveGroups: false });
    expect(
      [...without.root.querySelectorAll('.bo-group > .bo-tools .bo-menu button')].map((item) => text(item)),
    ).toEqual(['Move what follows to a new page']);
  });

  it('shows the insert point right above the selected field as its "+" alone, out from under its bar', async () => {
    const short = (key: string, label: string): DesignField => ({
      key,
      type: 'text',
      alias: key,
      label: { '': label },
    });
    const { element, root } = await canvasOf({
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                short('a', 'A'),
                short('b', 'B'),
                {
                  key: 'r',
                  type: 'repeater',
                  alias: 'r',
                  label: { '': 'R' },
                  fields: [short('c', 'C'), short('d', 'D')],
                },
              ],
            },
          ],
        },
      ],
      outcomes: [],
    });
    const beside = async (selection: string) => {
      element.selection = selection;
      await settle(element);
      for (const field of all<CanvasFieldElement>(element, 'jfb-canvas-field')) await settle(field);
      return [...root.querySelectorAll('[data-beside-tools]')].map(
        (point) =>
          point.closest('jfb-canvas-field, [data-fields]')?.getAttribute('key') ??
          `first of ${point.closest('[data-fields]')?.getAttribute('data-fields')}`,
      );
    };
    expect(await beside('b')).toEqual(['a']);
    expect(await beside('a')).toEqual(['first of g']);
    expect(await beside('r')).toEqual(['b']);
    expect(await beside('c')).toEqual(['first of r']);
    expect(await beside('d')).toEqual(['c']);
    // Its words stay its name for screen readers.
    expect(
      root.querySelector('jfb-canvas-field[key="c"] > .bo-chrome > .bo-insert-after')?.getAttribute('aria-label'),
    ).toBe('Add a field here, after C');
  });

  it("switches the language it shows, marks a field without a text in it, none in the default's, and copies only an alias", async () => {
    const languages = [
      { culture: 'en-US', name: 'English (United States)', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
    ];
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'a', type: 'text', alias: 'a', label: { '': 'Name', 'de-DE': 'Name' } },
                { key: 'b', type: 'text', alias: 'b', label: { '': 'Email' } },
                // Written in German only: the default's text is empty.
                { key: 'c', type: 'text', label: { '': '', 'de-DE': 'Nur Deutsch' } },
              ],
            },
          ],
        },
      ],
      outcomes: [],
    };
    const chips = async (element: FormCanvasElement, root: ShadowRoot) => {
      for (const field of all<CanvasFieldElement>(element, 'jfb-canvas-field')) await settle(field);
      return [...root.querySelectorAll('.bo-chip')].map((chip) => [
        chip.closest('jfb-canvas-field')?.getAttribute('key'),
        text(chip),
      ]);
    };
    const german = await canvasOf(definition, { look: { ...englishLook, languages, culture: 'de-DE' } });
    expect(await chips(german.element, german.root)).toEqual([['b', 'No German text yet']]);
    const button = must(german.root.querySelector<HTMLButtonElement>('.bo-language'), 'the language');
    expect([text(button), button.getAttribute('aria-label'), button.title]).toEqual([
      'de-DE',
      'Language de-DE: Deutsch (Deutschland)',
      'Language de-DE: Deutsch (Deutschland)',
    ]);
    // The product's own menu: buttons in a popover, no menu roles; the language shown marked, and checked.
    const languagesMenu = must(
      german.root.querySelector<HTMLElement>(`#${button.getAttribute('popovertarget') ?? ''}`),
      'the languages',
    );
    expect(languagesMenu.querySelector('[role]')).toBeNull();
    const items = [...languagesMenu.querySelectorAll<HTMLButtonElement>('.bo-menu button')];
    expect(
      items.map((item) => [
        text(item),
        item.getAttribute('aria-current'),
        item.querySelector('uui-icon')?.getAttribute('name') ?? null,
      ]),
    ).toEqual([
      ['English (United States)', null, null],
      ['Deutsch (Deutschland)', 'true', 'icon-check'],
    ]);
    // Open, it takes the focus on the language shown; Escape closes it and gives the focus back to its button.
    languagesMenu.removeAttribute('popover');
    languagesMenu.dispatchEvent(Object.assign(new Event('toggle'), { newState: 'open' }));
    expect(german.root.activeElement).toBe(items[1]);
    items[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, composed: true }));
    expect(german.root.activeElement).toBe(button);
    items[1]?.click();
    items[0]?.click();
    expect(german.requests).toEqual([{ kind: 'culture', culture: 'en-US' }]);
    document.body.replaceChildren();

    const english = await canvasOf(definition, {
      look: { ...englishLook, languages, culture: 'en-US' },
      selection: 'c',
    });
    expect(await chips(english.element, english.root)).toEqual([]);
    const menu = [...english.root.querySelectorAll('jfb-canvas-field[key="c"] .bo-menu button')].map((item) =>
      text(item),
    );
    // Nothing follows c on its page, and it has no alias: its "..." would hold nothing, and Change type is the inspector's.
    expect(menu).toEqual([]);
  });

  it("marks a page and a group whose title or description lacks the language shown, describing a group's button and a page's tab, a group's none while selected", async () => {
    const languages = [
      { culture: 'en-US', name: 'English (United States)', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
    ];
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          title: { '': 'Your details', 'de-DE': 'Ihre Angaben' },
          description: { '': '<p>We answer within a day.</p>' },
          groups: [
            {
              key: 'g1',
              title: { '': 'Contact' },
              fields: [{ key: 'a', type: 'text', alias: 'a', label: { '': 'A', de: 'A' } }],
            },
            {
              key: 'g2',
              title: { '': 'More', de: 'Mehr' },
              fields: [{ key: 'b', type: 'text', alias: 'b', label: { '': 'B', de: 'B' } }],
            },
          ],
        },
      ],
      outcomes: [],
    };
    const marks = (root: ShadowRoot) =>
      [...root.querySelectorAll(':is(.bo-page, .bo-group) > .bo-chips > .bo-chip:not(.bo-rule-chip)')].map((chip) => [
        chip.id,
        text(chip),
      ]);
    const german = await canvasOf(definition, { look: { ...englishLook, languages, culture: 'de-DE' } });
    expect(marks(german.root)).toEqual([
      ['jfb-chip-p', 'No German text yet'],
      ['jfb-chip-g1', 'No German text yet'],
    ]);
    expect(
      must(german.root.querySelector('.bo-tab-name[data-key="p"]'), 'the tab').getAttribute('aria-describedby'),
    ).toBe('jfb-chip-p');
    expect(must(german.root.querySelector('[data-select="g1"]'), 'the group').getAttribute('aria-describedby')).toBe(
      'jfb-chip-g1',
    );
    expect(
      must(german.root.querySelector('[data-select="g2"]'), 'the other group').hasAttribute('aria-describedby'),
    ).toBe(false);
    german.element.selection = 'g1';
    await german.element.updateComplete;
    expect(marks(german.root)).toEqual([['jfb-chip-p', 'No German text yet']]);
    document.body.replaceChildren();
    const english = await canvasOf(definition, { look: { ...englishLook, languages, culture: 'en-US' } });
    expect(marks(english.root)).toEqual([]);
  });

  it("draws a text that names a dictionary item as the item's text, and an edit of the label starts from its key", async () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          title: { '': '#Forms.Page' },
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'a', type: 'text', alias: 'a', label: { '': '#Forms.Name' } },
                { key: 'b', type: 'text', alias: 'b', label: { '': '#Forms.Gone' } },
              ],
            },
          ],
        },
      ],
      outcomes: [],
    };
    const lookups = new Map([
      ['forms.page', { found: true, text: 'Your details' }],
      ['forms.name', { found: true, text: 'Your name' }],
      ['forms.gone', { found: false, text: '' }],
    ]);
    const { element, root } = await canvasOf(definition, { lookups });
    const fields = all<CanvasFieldElement>(element, 'jfb-canvas-field');
    for (const field of fields) await settle(field);
    expect(text(root.querySelector('.jfb-title'))).toBe('Your details');
    expect(fields.map((field) => text(field.querySelector('label')))).toEqual([
      'Your name (optional)',
      '#Forms.Gone (optional)',
    ]);
    expect(fields.map((field) => field.state?.labelText)).toEqual(['#Forms.Name', '#Forms.Gone']);

    // In another language the marks read the draft's own texts: a key is the dictionary's to translate (ADR-0228).
    document.body.replaceChildren();
    const languages = [
      { culture: 'en-US', name: 'English (United States)', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
    ];
    const german = await canvasOf(definition, { lookups, look: { ...englishLook, languages, culture: 'de-DE' } });
    for (const field of all<CanvasFieldElement>(german.element, 'jfb-canvas-field')) await settle(field);
    expect(german.root.querySelectorAll('.bo-chip:not(.bo-rule-chip)')).toHaveLength(0);
  });

  it("carries a chip for each rule in use on a page, a group or a field, each describing it, a page's its tab, and a field's or group's none while it is selected", async () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          title: { '': 'Your details' },
          rules: [{ key: 'pr', effect: 'hide', conditions: [{ context: 'memberSignedIn', op: 'isChecked' }] }],
          groups: [
            {
              key: 'g',
              title: { '': 'About you' },
              rules: [{ key: 'gr', effect: 'show', conditions: [{ field: 'a', op: 'isNotEmpty' }] }],
              fields: [
                { key: 'a', type: 'text', alias: 'a', label: { '': 'Name' } },
                {
                  key: 'b',
                  type: 'text',
                  alias: 'b',
                  label: { '': 'Email' },
                  rules: [
                    { key: 'br', effect: 'show', conditions: [{ field: 'a', op: 'equals', value: 'Ann' }] },
                    { key: 'off', effect: 'require', enabled: false, conditions: [{ field: 'a', op: 'isEmpty' }] },
                  ],
                },
              ],
            },
          ],
        },
      ],
      validationRules: [
        { key: 'v', field: 'a', message: { '': 'Say more' }, conditions: [{ field: 'a', op: 'isEmpty' }] },
      ],
      outcomes: [],
    };
    const chips = async (element: FormCanvasElement, root: ShadowRoot) => {
      for (const field of all<CanvasFieldElement>(element, 'jfb-canvas-field')) await settle(field);
      return [...root.querySelectorAll('.bo-rule-chip')].map((chip) => [
        chip.closest('jfb-canvas-field')?.getAttribute('key') ??
          chip.closest('[data-group]')?.getAttribute('data-group') ??
          chip.closest('[data-groups]')?.getAttribute('data-groups'),
        chip.id,
        text(chip),
      ]);
    };
    const { element, root } = await canvasOf(definition);
    expect(await chips(element, root)).toEqual([
      ['p', 'jfb-rule-chip-p-0', 'Hidden when the visitor is signed in'],
      ['g', 'jfb-rule-chip-g-0', 'Shown when Name is not empty'],
      ['a', 'jfb-rule-chip-a-0', 'Shows "Say more" when Name is empty'],
      // A rule switched off changes nothing for visitors, so it has no chip.
      ['b', 'jfb-rule-chip-b-0', 'Shown when Name is "Ann"'],
    ]);
    expect(root.querySelector('.bo-tab-name[data-key="p"]')?.getAttribute('aria-describedby')).toBe(
      'jfb-rule-chip-p-0',
    );
    expect(root.querySelector('.bo-select-group')?.getAttribute('aria-describedby')).toBe('jfb-rule-chip-g-0');
    expect(root.querySelector('jfb-canvas-field[key="b"] .bo-select')?.getAttribute('aria-describedby')).toBe(
      'jfb-rule-chip-b-0',
    );
    // A change that leaves a field's rules saying the same keeps its list, so the field isn't drawn again.
    const said = must(root.querySelector<CanvasFieldElement>('jfb-canvas-field[key="b"]'), 'b').state?.rules;
    element.definition = { ...definition, pages: definition.pages.map((one) => ({ ...one, title: { '': 'About' } })) };
    await settle(element);
    expect(must(root.querySelector<CanvasFieldElement>('jfb-canvas-field[key="b"]'), 'b').state?.rules).toBe(said);
    document.body.replaceChildren();

    const selected = await canvasOf(definition, { selection: 'b' });
    expect((await chips(selected.element, selected.root)).map(([key]) => key)).toEqual(['p', 'g', 'a']);
    expect(selected.root.querySelector('jfb-canvas-field[key="b"] .bo-select')?.hasAttribute('aria-describedby')).toBe(
      false,
    );
    document.body.replaceChildren();

    // The canvas draws no selection for a page: its chips stay.
    const page = await canvasOf(definition, { selection: 'p' });
    expect((await chips(page.element, page.root)).map(([key]) => key)).toEqual(['p', 'g', 'a', 'b']);
  });

  it('points a new form to Add field and to pasting questions, and says an empty later page has no fields yet', async () => {
    const blank: DesignDefinition = { pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] };
    const first = await canvasOf(blank);
    expect(text(first.root.querySelector('.bo-empty'))).toBe('Add your first field, or paste your questions.');
    first.root.querySelector<HTMLButtonElement>('.bo-empty .bo-link')?.click();
    expect(first.requests).toEqual([{ kind: 'paste' }]);
    expect(text(first.root.querySelector('.jfb-navigation'))).toBe('Send');

    const later = await canvasOf(DEFINITION, { shown: 1 });
    expect(text(later.root.querySelector('.bo-empty'))).toBe('No fields on this page yet.');
    expect([...later.root.querySelectorAll('.jfb-navigation button')].map((button) => text(button))).toEqual([
      'Send',
      'Back',
    ]);
  });

  it('says why an image shows nothing: no picture chosen, or one the canvas has no address for', async () => {
    const images: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'none', type: 'image', settings: {} },
                { key: 'unread', type: 'image', settings: { media: 'm-1' } },
                { key: 'read', type: 'image', settings: { media: 'm-2', alt: 'Our hall' } },
              ],
            },
          ],
        },
      ],
    };
    const { root } = await canvasOf(images, { mediaUrls: { 'm-2': '/media/hall.jpg' } });
    const fields = all<CanvasFieldElement>(root.host, 'jfb-canvas-field');
    await Promise.all(fields.map((field) => field.updateComplete));
    const note = (field: CanvasFieldElement) =>
      [...field.children].find((child) => child.classList.contains('bo-note'));
    expect(fields.map((field) => text(note(field) ?? null))).toEqual([
      'Image: no picture chosen yet',
      "Image: its picture can't be shown here",
      '',
    ]);
    expect(fields[2]?.querySelector('img')?.getAttribute('src')).toBe('/media/hall.jpg');
  });

  it("shows a page's groups once it has more than one, or a group with its own title, though none holds a field", async () => {
    const two = await canvasOf({
      pages: [
        {
          key: 'p',
          groups: [
            { key: 'g1', fields: [] },
            { key: 'g2', fields: [] },
          ],
        },
      ],
    });
    expect(two.root.querySelector('.bo-empty')).toBeNull();
    expect(two.root.querySelectorAll('.bo-empty-group')).toHaveLength(2);
    const titled = await canvasOf({
      pages: [{ key: 'p', groups: [{ key: 'g', title: { '': 'About you' }, fields: [] }] }],
    });
    expect(titled.root.querySelector('.bo-empty')).toBeNull();
    expect(titled.root.querySelector('fieldset.bo-group > legend')?.textContent).toBe('About you');
  });

  it("gives the focus to the page's Add field for an element it doesn't draw", async () => {
    const { element, root } = await canvasOf({ pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] });
    element.focusElement('g');
    expect(root.activeElement?.classList.contains('bo-add-field')).toBe(true);
  });

  it("shows After sending as visitors see it, the message's markup without anything that runs, the outcomes' place under it", async () => {
    const { root } = await canvasOf(DEFINITION, { shown: 'after' });
    const outcome = must(root.querySelector<HTMLElement>('.jfb-outcome'), 'the outcome');
    expect(outcome.innerHTML).toBe('<p>Thanks, <strong>see you</strong>!</p>');
    expect(outcome.inert).toBe(true);
    // Nothing to press on the sheet: the outcomes are edited beside it or under it (ADR-0256).
    expect(root.querySelector('.bo-sheet uui-button, .bo-sheet button')).toBeNull();
    const under = must(root.querySelector<HTMLElement>('.bo-under'), 'the place under the sheet');
    expect([under.previousElementSibling?.classList.contains('bo-sheet'), under.querySelector('slot')?.name]).toEqual([
      true,
      'after',
    ]);
    expect(under.style.getPropertyValue('--bo-width')).toBe(
      root.querySelector<HTMLElement>('.bo-sheet')?.style.getPropertyValue('--bo-width'),
    );
    const page = await canvasOf(DEFINITION);
    expect(page.root.querySelector('.bo-under')).toBeNull();
  });

  it('shows the first outcome for every visitor: one whose rule is off counts, one with a rule that may not match does not', async () => {
    const ruled = (enabled: boolean) => [
      { key: 'r', effect: 'use', enabled, match: 'all' as const, conditions: [{ field: 'name', op: 'isNotEmpty' }] },
    ];
    const definition: DesignDefinition = {
      ...DEFINITION,
      outcomes: [
        { key: 'press', kind: 'message', message: { '': '<p>Press</p>' }, rules: ruled(true) },
        { key: 'off', kind: 'message', message: { '': '<p>Off</p>' }, rules: ruled(false) },
        { key: 'o', kind: 'message', message: { '': '<p>Last</p>' } },
      ],
    };
    const { root } = await canvasOf(definition, { shown: 'after' });
    expect(must(root.querySelector<HTMLElement>('.jfb-outcome'), 'the outcome').innerHTML).toBe('<p>Off</p>');
  });

  it('switches the width, the chosen one pressed, and keeps Undo and Redo in its menu as they can be used', async () => {
    const { requests, root } = await canvasOf(DEFINITION, { width: 'tablet', canUndo: true });
    const widths = [...root.querySelectorAll<HTMLButtonElement>('.bo-widths button')];
    expect(
      widths.map((width) => [
        width.getAttribute('aria-label'),
        width.title,
        width.querySelector('uui-icon')?.getAttribute('name'),
        width.getAttribute('aria-pressed'),
      ]),
    ).toEqual([
      ['Phone', 'Phone', 'icon-iphone', 'false'],
      ['Tablet', 'Tablet', 'icon-ipad', 'true'],
      ['Desktop', 'Desktop', 'icon-display', 'false'],
    ]);
    expect(root.querySelector<HTMLElement>('.jfb-form')?.style.getPropertyValue('--jfb-size-form')).toBe('768px');
    widths[0]?.click();
    const [undo, redo] = [...root.querySelectorAll<HTMLButtonElement>('#jfb-canvas-more button')];
    expect([undo?.disabled, redo?.disabled]).toEqual([false, true]);
    undo?.click();
    expect(requests).toEqual([{ kind: 'width', width: 'phone' }, { kind: 'undo' }]);
  });

  it('puts the focus on the first item of its menu the canvas can do as the menu opens', async () => {
    const { root } = await canvasOf(DEFINITION, { canUndo: false });
    const menu = must(root.querySelector<HTMLElement>('#jfb-canvas-more'), 'the menu');
    // jsdom has no popovers and lets nothing in a closed one take the focus: the menu stands open as the browser opens it.
    menu.removeAttribute('popover');
    menu.dispatchEvent(Object.assign(new Event('toggle'), { newState: 'open' }));
    // Undo and Redo can't be used yet: the Rules overview comes first.
    expect(root.activeElement?.textContent?.trim()).toBe('Rules overview');
  });

  it('offers "Move what follows to a new page" only where something follows, and Change type nowhere on the canvas', async () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [
                { key: 'n', type: 'text', alias: 'name', label: { '': 'Name' } },
                { key: 'h', type: 'heading', settings: { text: { '': 'About you' } } },
              ],
            },
          ],
        },
      ],
    };
    const menuOf = (root: ShadowRoot, key: string) =>
      [...root.querySelectorAll(`jfb-canvas-field[key="${key}"] .bo-menu button`)].map((item) => text(item));
    const name = await canvasOf(definition, { selection: 'n' });
    expect(menuOf(name.root, 'n')).toEqual(['Move what follows to a new page', 'Copy alias']);
    document.body.replaceChildren();
    const heading = await canvasOf(definition, { selection: 'h' });
    expect(menuOf(heading.root, 'h')).toEqual([]);
    expect(heading.root.querySelector('jfb-canvas-field[key="h"] .bo-more-element')).toBeNull();
    document.body.replaceChildren();
    const group = await canvasOf(definition, { selection: 'g1' });
    expect(group.root.querySelector('.bo-group > .bo-tools .bo-more-element')).toBeNull();
  });

  it('passes on what its fields ask, and moves the focus to an element', async () => {
    const { element, requests, root } = await canvasOf(DEFINITION, { selection: 'e' });
    const field = must(root.querySelector<CanvasFieldElement>('jfb-canvas-field[key="e"]'), 'the field');
    for (const tool of field.querySelectorAll<HTMLButtonElement>('.bo-tools button')) tool.click();
    expect(requests).toEqual([
      { kind: 'moveTo', key: 'e' },
      { kind: 'duplicate', key: 'e' },
      { kind: 'delete', key: 'e' },
      { kind: 'newPageAfter', key: 'e' },
      { kind: 'copyAlias', key: 'e' },
    ]);
    element.focusElement('n');
    expect(root.activeElement?.closest('jfb-canvas-field')?.getAttribute('key')).toBe('n');
    element.focusElement('g1');
    expect(root.activeElement?.getAttribute('data-select')).toBe('g1');
  });

  it('gives a field a change did not touch the same state, so only the fields it changes are drawn again', async () => {
    const { element, root } = await canvasOf(DEFINITION, { selection: 'e' });
    const field = (key: string) => must(root.querySelector<CanvasFieldElement>(`jfb-canvas-field[key="${key}"]`), key);
    const [e, n] = [field('e').state, field('n').state];
    element.selection = 'n';
    await settle(element);
    expect(field('n').state).not.toBe(n);
    expect(field('n').state?.selected).toBe(true);
    expect(field('e').state).not.toBe(e);
    element.refusal = { key: 'e', text: 'No.' };
    await settle(element);
    const kept = field('n').state;
    element.insertAt = { container: 'g2', index: 0 };
    await settle(element);
    expect(field('n').state).toBe(kept);
    expect(field('e').state?.words).toBe(e?.words);
  });

  it("gives each group a handle named with it, each tab its page's key, and shows a refusal at a group or a page", async () => {
    const { element, requests, root } = await canvasOf(DEFINITION, { refusal: { key: 'g1', text: 'No.' } });
    const handles = [...root.querySelectorAll<HTMLButtonElement>('.bo-handle-group')];
    expect(
      handles.map((handle) => [handle.getAttribute('aria-label'), handle.dataset['key'], handle.dataset['control']]),
    ).toEqual([
      ['Move About you', 'g1', 'handle'],
      ['Move Group 2', 'g2', 'handle'],
    ]);
    handles[0]?.click();
    expect(requests).toEqual([{ kind: 'select', key: 'g1' }]);
    const tabs = [...root.querySelectorAll<HTMLButtonElement>('.bo-tab-name')];
    expect(tabs.map((tab) => [tab.dataset['key'], tab.dataset['control']])).toEqual([
      ['p1', 'tab'],
      ['p2', 'tab'],
    ]);
    expect(text(root.querySelector('[data-group="g1"] > .bo-refusal'))).toBe('No.');
    element.refusal = { key: 'p1', text: 'Not here.' };
    await settle(element);
    expect(text(root.querySelector('.bo-page > .bo-refusal'))).toBe('Not here.');
    expect(root.querySelectorAll('.bo-refusal')).toHaveLength(1);
    expect(root.querySelector('[data-groups="p1"]')).not.toBeNull();
    expect(root.querySelector('.jfb-fields[data-fields="g2"]')).not.toBeNull();
  });

  it('moves the focus through the page with Up and Down in reading order: each group, its fields', async () => {
    const { element, root } = await canvasOf(DEFINITION);
    const focused = () => {
      const found = root.activeElement as HTMLElement | null;
      return found?.dataset['key'] ?? found?.closest('jfb-canvas-field')?.getAttribute('key') ?? 'none';
    };
    // The page has no button on the sheet: it is not on the way.
    element.focusNext('p1', 1);
    expect(focused()).toBe('none');
    element.focusElement('g1');
    expect(focused()).toBe('g1');
    element.focusNext('g1', 1);
    expect(focused()).toBe('e');
    element.focusNext('n', 1);
    expect(focused()).toBe('g2');
    element.focusNext('g2', 1);
    expect(focused()).toBe('g2');
    element.focusNext('g1', -1);
    expect(focused()).toBe('g2');
    // An element of another page is not on the way.
    element.focusNext('g3', 1);
    expect(focused()).toBe('g2');
  });

  it("takes a field dropped on a page that shows only its sentence into the page's one group", async () => {
    const { root } = await canvasOf({ pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] });
    expect(root.querySelector('.bo-empty')?.getAttribute('data-fields')).toBe('g');
  });

  it("leaves out the bare page's one group on the way", async () => {
    const { element, root } = await canvasOf({ pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] });
    element.focusElement('p');
    element.focusNext('p', 1);
    expect((root.activeElement as HTMLElement | null)?.dataset['key']).toBe('p');
  });

  it("gives the focus to an element's handle, and to a page's tab for a page", async () => {
    const { element, root } = await canvasOf(DEFINITION, { shown: 0 });
    element.focusElement('e', 'handle');
    expect([
      (root.activeElement as HTMLElement).dataset['key'],
      (root.activeElement as HTMLElement).dataset['control'],
    ]).toEqual(['e', 'handle']);
    element.focusElement('g1', 'handle');
    expect((root.activeElement as HTMLElement).dataset['handle']).toBe('g1');
    element.focusElement('p2', 'tab');
    expect([
      (root.activeElement as HTMLElement).dataset['key'],
      (root.activeElement as HTMLElement).dataset['control'],
    ]).toEqual(['p2', 'tab']);
    element.focusElement('p1');
    expect((root.activeElement as HTMLElement).dataset['key']).toBe('p1');
  });

  it('opens the rules overview and the Keyboard shortcuts from its menu, and names the keys as this keyboard has them', async () => {
    const { requests, root } = await canvasOf(DEFINITION);
    // The items shown; one hidden would leave the list.
    const items = [...root.querySelectorAll<HTMLButtonElement>('#jfb-canvas-more button')].filter(
      (item) => !item.hidden,
    );
    expect(items.map((item) => [text(item.querySelector('span')), text(item.querySelector('kbd'))])).toEqual([
      ['Undo', 'Ctrl+Z'],
      ['Redo', 'Ctrl+Y'],
      ['Rules overview', ''],
      ['Keyboard shortcuts', '?'],
    ]);
    items[2]?.click();
    items[3]?.click();
    expect(requests).toEqual([{ kind: 'rulesOverview' }, { kind: 'shortcuts' }]);
  });

  it("lists the site's designer actions after its own items, by their labels, and asks for the one chosen (ADR-0018)", async () => {
    const { requests, root } = await canvasOf(DEFINITION, {
      designerActions: [
        { alias: 'Test.Translate', label: 'Translate with a tool...' },
        { alias: 'Test.Count', label: '#jfbDesigner_undo' },
      ],
    });
    const items = [...root.querySelectorAll<HTMLButtonElement>('#jfb-canvas-more button')];
    expect(items.map((item) => text(item.querySelector('span')))).toEqual([
      'Undo',
      'Redo',
      'Rules overview',
      'Keyboard shortcuts',
      'Translate with a tool...',
      'Undo',
    ]);
    items[4]?.click();
    expect(requests).toEqual([{ kind: 'designerAction', alias: 'Test.Translate' }]);
  });

  it("shows the form check's badge before its menu: its icon and the number found, its words as its name and tip (ADR-0181, ADR-0256)", async () => {
    const none = await canvasOf(DEFINITION);
    // Before the first check its place is kept, empty and out of reach.
    const kept = must(none.root.querySelector<HTMLElement>('.bo-check'), 'the place');
    expect([kept.localName, kept.dataset['state'], kept.getAttribute('aria-hidden'), text(kept)]).toEqual([
      'span',
      'none',
      'true',
      '',
    ]);
    const cases: [Parameters<typeof canvasOf>[1], string, string, string, string | null][] = [
      [{ check: { kind: 'checking' } }, 'checking', '\u2026', 'Form check: checking\u2026', null],
      [{ check: { kind: 'failed' } }, 'failed', '', 'Form check: not checked', 'icon-alert'],
      [{ check: { kind: 'checked', errors: 1, warnings: 1 } }, 'error', '2', 'Form check: 2 problems', 'icon-alert'],
      [{ check: { kind: 'checked', errors: 0, warnings: 1 } }, 'warning', '1', 'Form check: 1 problem', 'icon-info'],
      [{ check: { kind: 'checked', errors: 0, warnings: 0 } }, 'clear', '', 'Form check: no problems', 'icon-check'],
      // (no problems: its check stands on a round disc, a status rather than a box to tick, checked below)
      [
        { check: { kind: 'checked', errors: 150, warnings: 3 } },
        'error',
        '99+',
        'Form check: 153 problems',
        'icon-alert',
      ],
    ];
    for (const [extra, state, shown, words, icon] of cases) {
      document.body.replaceChildren();
      const { requests, root } = await canvasOf(DEFINITION, extra);
      const badge = must(root.querySelector<HTMLButtonElement>('button.bo-check'), 'the badge');
      expect([
        badge.dataset['state'],
        text(badge),
        badge.getAttribute('aria-label'),
        badge.title,
        badge.querySelector('uui-icon')?.getAttribute('name') ?? null,
      ]).toEqual([state, shown, words, words, icon]);
      expect(badge.getAttribute('aria-haspopup')).toBe('dialog');
      expect(badge.querySelector('.bo-check-disc') !== null).toBe(state === 'clear');
      expect(badge.nextElementSibling?.classList.contains('bo-more')).toBe(true);
      badge.click();
      expect(requests).toEqual([{ kind: 'formCheck' }]);
    }
  });
});
