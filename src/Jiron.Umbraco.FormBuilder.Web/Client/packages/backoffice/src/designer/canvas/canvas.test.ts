// The canvas (ROADMAP 9.3b, ADR-0168): a field drawn by the element's views with nothing of it taking input, the
// designer's controls over it with their names, its label edited in place, a repeatable group's own fields, a note for
// a field visitors don't see; and the canvas around them: the steps, a page's groups and its end, the empty states,
// After sending, the width switch and the requests each control sends.
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
  duplicate: 'Duplicate',
  duplicateNamed: 'Duplicate Email address',
  delete: 'Delete',
  deleteNamed: 'Delete Email address',
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

  it('says it is selected, and shows Duplicate and Delete while it is, each named with the field', async () => {
    const { element, requests } = await fieldOf(stateOf(email, { selected: true }));
    expect(element.hasAttribute('selected')).toBe(true);
    expect(controls(element).querySelector('.bo-select')?.getAttribute('aria-current')).toBe('true');
    const tools = [...controls(element).querySelectorAll<HTMLButtonElement>('.bo-tools button')];
    expect(tools.map((tool) => [tool.getAttribute('aria-label'), text(tool)])).toEqual([
      ['Duplicate Email address', 'Duplicate'],
      ['Delete Email address', 'Delete'],
    ]);
    for (const tool of tools) tool.click();
    expect(requests).toEqual([
      { kind: 'duplicate', key: 'e' },
      { kind: 'delete', key: 'e' },
    ]);
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
  it('shows the pages as steps, then Add page and After sending, the page shown current', async () => {
    const { element, requests, root } = await canvasOf(DEFINITION, { shown: 1 });
    const steps = [...root.querySelectorAll<HTMLButtonElement>('.bo-steps button')];
    expect(steps.map((step) => [text(step), step.getAttribute('aria-current')])).toEqual([
      ['Your details', 'false'],
      ['Page 2', 'step'],
      ['Add page', null],
      ['After sending', 'false'],
    ]);
    for (const step of steps) step.click();
    expect(requests).toEqual([
      { kind: 'show', shown: 0 },
      { kind: 'show', shown: 1 },
      { kind: 'addPage' },
      { kind: 'show', shown: 'after' },
    ]);
    expect(element.shown).toBe(1);
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

  it("names each group's and page's button, and an empty group's place for a field", async () => {
    const { requests, root } = await canvasOf(DEFINITION, { selection: 'g1' });
    const groups = [...root.querySelectorAll<HTMLButtonElement>('.bo-select-group')];
    expect(groups.map((group) => [group.getAttribute('aria-label'), group.getAttribute('aria-current')])).toEqual([
      ['About you, group', 'true'],
      ['Group 2, group', 'false'],
    ]);
    expect(root.querySelector('.bo-select-page')?.getAttribute('aria-label')).toBe('Your details, page');
    expect(root.querySelectorAll('.bo-group > .bo-tools button')).toHaveLength(2);
    const empty = must(root.querySelector<HTMLButtonElement>('.bo-empty-group'), "the empty group's place");
    expect(empty.getAttribute('aria-label')).toBe('Add a field here, in Group 2');
    empty.click();
    root.querySelector<HTMLButtonElement>('.bo-insert-first')?.click();
    root.querySelector<HTMLButtonElement>('.bo-add-field')?.click();
    expect(requests).toEqual([
      { kind: 'insertFirst', container: 'g2' },
      { kind: 'insertFirst', container: 'g1' },
      { kind: 'addField', page: 'p1' },
    ]);
  });

  it('points a new form to the palette and to pasting questions, and an empty later page to New page', async () => {
    const blank: DesignDefinition = { pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] };
    const first = await canvasOf(blank);
    expect(text(first.root.querySelector('.bo-empty'))).toBe(
      'Add your first field: pick a type on the left, or paste your questions.',
    );
    first.root.querySelector<HTMLButtonElement>('.bo-empty .bo-link')?.click();
    expect(first.requests).toEqual([{ kind: 'paste' }]);
    expect(text(first.root.querySelector('.jfb-navigation'))).toBe('Send');

    const later = await canvasOf(DEFINITION, { shown: 1 });
    expect(text(later.root.querySelector('.bo-empty'))).toBe(
      'No fields on this page yet. To split a page instead, use New page in the palette.',
    );
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

  it("shows After sending as visitors see it, the message's markup without anything that runs, with Change", async () => {
    const { requests, root } = await canvasOf(DEFINITION, { shown: 'after' });
    const outcome = must(root.querySelector<HTMLElement>('.jfb-outcome'), 'the outcome');
    expect(outcome.innerHTML).toBe('<p>Thanks, <strong>see you</strong>!</p>');
    expect(outcome.inert).toBe(true);
    must(root.querySelector('uui-button[label="Change"]'), 'Change').dispatchEvent(new Event('click'));
    expect(requests).toEqual([{ kind: 'changeOutcome' }]);
  });

  it('switches the width, the chosen one pressed, and keeps Undo and Redo in its menu as they can be used', async () => {
    const { requests, root } = await canvasOf(DEFINITION, { width: 'tablet', canUndo: true });
    const widths = [...root.querySelectorAll<HTMLButtonElement>('.bo-widths button')];
    expect(widths.map((width) => [text(width), width.getAttribute('aria-pressed')])).toEqual([
      ['Phone', 'false'],
      ['Tablet', 'true'],
      ['Desktop', 'false'],
    ]);
    expect(root.querySelector<HTMLElement>('.jfb-form')?.style.getPropertyValue('--jfb-size-form')).toBe('768px');
    widths[0]?.click();
    const [undo, redo] = [...root.querySelectorAll<HTMLButtonElement>('.bo-menu button')];
    expect([undo?.disabled, redo?.disabled]).toEqual([false, true]);
    undo?.click();
    expect(requests).toEqual([{ kind: 'width', width: 'phone' }, { kind: 'undo' }]);
  });

  it('passes on what its fields ask, and moves the focus to an element', async () => {
    const { element, requests, root } = await canvasOf(DEFINITION, { selection: 'e' });
    const field = must(root.querySelector<CanvasFieldElement>('jfb-canvas-field[key="e"]'), 'the field');
    field.querySelector<HTMLButtonElement>('.bo-tools button')?.click();
    expect(requests).toEqual([{ kind: 'duplicate', key: 'e' }]);
    element.focusElement('n');
    expect(root.activeElement?.closest('jfb-canvas-field')?.getAttribute('key')).toBe('n');
    element.focusElement('g1');
    expect(root.activeElement?.getAttribute('data-select')).toBe('g1');
  });
});
