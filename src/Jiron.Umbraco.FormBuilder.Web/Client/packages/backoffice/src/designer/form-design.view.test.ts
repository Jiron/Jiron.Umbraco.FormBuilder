// The Design view (ROADMAP 9.3b): each request of the canvas and the palette becomes an edit of the editor model through
// the workspace, with what it says for screen readers: a pick at the place the palette was opened for, after the
// selection or at the end of the page; a label edited in place; a delete that Ctrl+Z takes back; a new page shown;
// pasted questions as fields; Esc; and a refusal said in words. Its moves (ROADMAP 9.3c): a drop with Undo, a palette
// type dropped, a refusal shown at the element, Alt+Up and Alt+Down with the focus kept, Up and Down between elements,
// Move to... and the Keyboard shortcuts. A stand-in workspace drives the real undo history.
import { FORMULAS } from '@jfb/logic/calc';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import { CANVAS_REQUEST, type CanvasRequest } from './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { locate, type DesignDefinition } from './definition';
import type { Edit, EditorState } from './editor-model';
import { traitsOf, type EditorView } from './form-workspace.context';
import { EditorHistory } from './history';
import { PALETTE_PICK, type PalettePick } from './palette/palette.element';
import { builtInTypes, englishLook } from './preview/fixtures';
import type { UndoNotificationData } from '../ui/undo-notification.element';

// What the dialogs answer, and each dialog opened with what it was given.
const opened = vi.hoisted(() => ({ value: undefined as unknown, dialogs: [] as { alias: string; data: unknown }[] }));
// Whether what the canvas draws with can be read.
const reading = vi.hoisted(() => ({ fails: false }));

vi.mock('./preview/look', async (original) => ({
  ...(await original<typeof import('./preview/look')>()),
  readLook: async () => (reading.fails ? undefined : englishLook),
  themeSheets: async () => [],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: unknown, args?: { data?: unknown }) => {
    opened.dialogs.push({ alias: String(token), data: args?.data });
    return opened.value;
  },
}));

const { FormDesignViewElement, isNarrow, mediaOf, readWidth } = await import('./form-design.view');

const field = (key: string, type = 'text') => ({ key, type, alias: key, label: { '': key } });

const DEFINITION: DesignDefinition = {
  pages: [
    { key: 'p1', groups: [{ key: 'g1', fields: [field('name'), field('email', 'email')] }] },
    { key: 'p2', groups: [{ key: 'g2', fields: [field('note', 'textarea')] }] },
  ],
};

/** A workspace that holds the editor's state in the real history, as the form workspace does. */
class StandIn {
  readonly said: string[] = [];
  readonly #history: EditorHistory;
  readonly #editor: UmbObjectState<EditorView>;
  #next = 0;
  readonly editor;
  readonly fieldTypes = new UmbObjectState(builtInTypes).asObservable();
  readonly paletteGroups = new UmbObjectState([{ alias: 'text', name: '#jfbFieldPalette_text' }]).asObservable();
  readonly model = {
    traitsOf: (type: string) => {
      const found = Object.hasOwn(builtInTypes, type) ? builtInTypes[type] : undefined;
      return found ? traitsOf(found) : undefined;
    },
    newKey: () => `new${++this.#next}`,
    formulas: FORMULAS,
  };

  constructor(definition: DesignDefinition) {
    this.#history = new EditorHistory({ definition, selection: null });
    this.#editor = new UmbObjectState<EditorView>(this.#view());
    this.editor = this.#editor.asObservable();
  }

  /** The element that provides it, which Umbraco's context API asks of every context. */
  host: Element | undefined;
  getHostElement = () => this.host;
  getEntityType = () => 'jfb-form';
  getEditorState = (): EditorState => this.#history.state;

  edit(edit: Edit, said: string): Edit {
    const result = this.#history.apply(edit, said);
    if (result.ok && said) this.said.push(said);
    this.#editor.setValue(this.#view());
    return result;
  }

  undo = () => this.#travel(this.#history.undo());
  redo = () => this.#travel(this.#history.redo());

  #travel(said: string | undefined) {
    this.#editor.setValue(this.#view());
    return said;
  }

  #view(): EditorView {
    return { state: this.#history.state, canUndo: this.#history.canUndo, canRedo: this.#history.canRedo };
  }
}

/** Umbraco's toasts, each kept with what it was given and whether it was closed. */
class Toasts {
  readonly shown: { data: UndoNotificationData; closed: boolean }[] = [];
  host: Element | undefined;
  getHostElement = () => this.host;
  peek(_color: string, options: { data: UndoNotificationData }) {
    const toast = { data: options.data, closed: false };
    this.shown.push(toast);
    return { close: () => (toast.closed = true) };
  }
}

async function viewOf(definition = DEFINITION) {
  const workspace = new StandIn(definition);
  const toasts = new Toasts();
  const host = testHost();
  workspace.host = host;
  toasts.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  host.provideContext(UMB_NOTIFICATION_CONTEXT, toasts as never);
  const view = new FormDesignViewElement();
  host.append(view);
  await settle(view);
  const canvas = must(shadow(view).querySelector<FormCanvasElement>('jfb-form-canvas'), 'the canvas');
  const ask = async (request: CanvasRequest) => {
    view.dispatchEvent(new CustomEvent(CANVAS_REQUEST, { detail: request, bubbles: true, composed: true }));
    await settle(view);
  };
  const pick = async (palette: PalettePick) => {
    view.dispatchEvent(new CustomEvent(PALETTE_PICK, { detail: palette, bubbles: true, composed: true }));
    await settle(view);
  };
  const press = async (key: string, init: KeyboardEventInit = {}) => {
    view.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, composed: true, ...init }));
    await settle(view);
  };
  const state = () => workspace.getEditorState();
  const keys = (container: string) => {
    const element = locate(state().definition, container)?.element as unknown as {
      fields?: readonly { key: string }[];
    };
    return (element.fields ?? []).map((each) => each.key);
  };
  return { workspace, toasts, view, canvas, ask, pick, press, state, keys };
}

beforeAll(registerEnglish);

beforeEach(() => {
  localStorage.clear();
  reading.fails = false;
  opened.value = undefined;
  opened.dialogs = [];
});

/** What the live region says once its frame has passed. */
async function heard(view: HTMLElement): Promise<string> {
  await new Promise((done) => setTimeout(done, 50));
  return shadow(view).querySelector('[role="status"]')?.textContent ?? '';
}

/** A canvas control by its element's key and kind: the button that selects it, its handle, or a page's step. */
function control(canvas: HTMLElement, key: string, kind: 'select' | 'handle' | 'step'): HTMLElement {
  return must(
    shadow(canvas).querySelector<HTMLElement>(`[data-key="${key}"][data-control="${kind}"]`),
    `the ${kind} of ${key}`,
  );
}

/** Presses a key on a canvas control that has the focus. */
async function pressOn(
  view: HTMLElement & { updateComplete: Promise<unknown> },
  target: HTMLElement,
  key: string,
  init: KeyboardEventInit = {},
) {
  target.focus();
  target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, composed: true, cancelable: true, ...init }));
  await settle(view);
  await settle(view);
}

/** Sends a canvas request to the view, as the canvas does. */
async function ask(view: HTMLElement & { updateComplete: Promise<unknown> }, request: CanvasRequest) {
  view.dispatchEvent(new CustomEvent(CANVAS_REQUEST, { detail: request, bubbles: true, composed: true }));
  await settle(view);
}

/** The control the focus is on in the canvas. */
const focusedIn = (canvas: HTMLElement) => {
  const found = shadow(canvas).activeElement as HTMLElement | null;
  return found ? `${found.dataset['key'] ?? ''} ${found.dataset['control'] ?? found.className}` : 'none';
};

afterEach(() => {
  document.body.replaceChildren();
});

describe("the view's settings", () => {
  it('start at Desktop, then at the width this browser chose last', () => {
    expect(readWidth()).toBe('desktop');
    localStorage.setItem('jfb.designer.width', 'phone');
    expect(readWidth()).toBe('phone');
    localStorage.setItem('jfb.designer.width', 'wall');
    expect(readWidth()).toBe('desktop');
  });

  it('put the palette over the canvas up to 700 px, the narrowest workspace among them, and beside it wider', () => {
    expect([480, 620, 700, 701, 1024].map(isNarrow)).toEqual([true, true, true, false, false]);
  });

  it("read the media items a form's images and option cards show", () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'i', type: 'image', settings: { media: 'm1' } },
                { key: 'r', type: 'radio', options: { items: [{ value: 'a', image: 'm2' }, { value: 'b' }] } },
                { key: 'x', type: 'repeater', fields: [{ key: 'j', type: 'image', settings: { media: 'm1' } }] },
              ],
            },
          ],
        },
      ],
    };
    expect(mediaOf(definition)).toEqual(['m1', 'm2']);
  });
});

describe('the palette over the canvas', () => {
  it('leaves the canvas out of reach while it is open, and Esc gives the focus back to what opened it', async () => {
    const { view, canvas, ask, press } = await viewOf();
    Object.assign(view, { _narrow: true });
    await settle(view);
    expect(shadow(view).querySelector('jfb-designer-palette')).toBeNull();
    canvas.focusAddField();
    await ask({ kind: 'addField', page: 'p1' });
    expect(shadow(view).querySelector('jfb-designer-palette')?.getAttribute('mode')).toBe('overlay');
    expect(canvas.hasAttribute('inert')).toBe(true);
    await press('Escape');
    expect(shadow(view).querySelector('jfb-designer-palette')).toBeNull();
    expect(canvas.hasAttribute('inert')).toBe(false);
    expect(shadow(canvas).activeElement?.classList.contains('bo-add-field')).toBe(true);
  });

  it('closes with a click beside it', async () => {
    const { view, ask } = await viewOf();
    Object.assign(view, { _narrow: true });
    await settle(view);
    await ask({ kind: 'addField', page: 'p1' });
    must(shadow(view).querySelector<HTMLElement>('.scrim'), 'the scrim').click();
    await settle(view);
    expect(shadow(view).querySelector('jfb-designer-palette')).toBeNull();
  });
});

describe("the view's look", () => {
  it('says when it cannot be read, and reads it again with Try again', async () => {
    reading.fails = true;
    const { view } = await viewOf();
    const failed = must(shadow(view).querySelector('.failed'), 'the failure');
    expect([failed.getAttribute('role'), text(failed.querySelector('p'))]).toEqual([
      'alert',
      "The canvas couldn't be loaded.",
    ]);
    reading.fails = false;
    must(failed.querySelector('uui-button'), 'Try again').dispatchEvent(new Event('click'));
    await settle(view);
    await settle(view);
    expect(shadow(view).querySelector('.failed')).toBeNull();
  });
});

describe('a pick from the palette', () => {
  it('goes to the end of the page shown without a selection, selected, said, and kept as recently used', async () => {
    const { workspace, pick, state, keys } = await viewOf();
    await pick({ kind: 'field', type: 'phone' });
    expect(keys('g1')).toEqual(['name', 'email', 'new1']);
    expect(state().selection).toBe('new1');
    expect(locate(state().definition, 'new1')?.element).toEqual({
      key: 'new1',
      type: 'phone',
      alias: 'phone',
      label: { '': 'Phone' },
    });
    expect(workspace.said).toEqual(['Added Phone']);
    expect(JSON.parse(localStorage.getItem('jfb.designer.recent') ?? '[]')).toEqual(['phone']);
  });

  it('goes to the insert point the palette was opened for, and after the selected field', async () => {
    const { ask, pick, keys } = await viewOf();
    await ask({ kind: 'insertFirst', container: 'g1' });
    await pick({ kind: 'field', type: 'text' });
    expect(keys('g1')).toEqual(['new1', 'name', 'email']);
    // The new field is selected, so the next one follows it.
    await pick({ kind: 'field', type: 'email' });
    expect(keys('g1')).toEqual(['new1', 'new2', 'name', 'email']);
    await ask({ kind: 'insertAfter', key: 'name' });
    await pick({ kind: 'field', type: 'url' });
    expect(keys('g1')).toEqual(['new1', 'new2', 'name', 'new3', 'email']);
  });

  it("goes to the page shown, a group's or a new page, and a page's Add field to its end", async () => {
    const { ask, pick, state, keys, canvas } = await viewOf();
    await ask({ kind: 'show', shown: 1 });
    expect(canvas.shown).toBe(1);
    await ask({ kind: 'addField', page: 'p2' });
    await pick({ kind: 'field', type: 'date' });
    expect(keys('g2')).toEqual(['note', 'new1']);
    await pick({ kind: 'group' });
    expect(locate(state().definition, 'p2')?.element).toMatchObject({ groups: [{ key: 'g2' }, { key: 'new2' }] });
  });

  it('says why the model refuses it', async () => {
    const { view, pick, workspace } = await viewOf();
    await pick({ kind: 'field', type: 'captcha' });
    await pick({ kind: 'field', type: 'captcha' });
    expect(workspace.said).toEqual(['Added Captcha placement']);
    await new Promise((done) => setTimeout(done, 50));
    expect(shadow(view).querySelector('[role="status"]')?.textContent).toBe(
      'A form holds one Captcha placement at most.',
    );
  });

  it('starts a page at the selected field with New page, and shows it', async () => {
    const { ask, pick, state, canvas } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await pick({ kind: 'page' });
    expect(state().definition.pages.map((page) => page.key)).toEqual(['p1', state().selection, 'p2']);
    expect(canvas.shown).toBe(1);
  });
});

describe('the canvas requests', () => {
  it('edit a label in place, and leave it as it was when it comes back empty', async () => {
    const { ask, state, canvas, workspace } = await viewOf();
    await ask({ kind: 'editLabel', key: 'email' });
    expect(canvas.editing).toBe('email');
    await ask({ kind: 'label', key: 'email', text: '  Your email ' });
    expect(canvas.editing).toBeNull();
    expect(locate(state().definition, 'email')?.element).toMatchObject({ label: { '': 'Your email' } });
    expect(workspace.said).toEqual(['Renamed to Your email']);
    await ask({ kind: 'editLabel', key: 'email' });
    await ask({ kind: 'label', key: 'email', text: '   ' });
    expect(locate(state().definition, 'email')?.element).toMatchObject({ label: { '': 'Your email' } });
  });

  it('delete an element, which Ctrl+Z brings back and Ctrl+Y deletes again', async () => {
    const { ask, press, keys, state, workspace } = await viewOf();
    await ask({ kind: 'delete', key: 'name' });
    expect(keys('g1')).toEqual(['email']);
    expect(state().selection).toBe('email');
    expect(workspace.said).toEqual(['Deleted name']);
    await press('z', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email']);
    await press('y', { ctrlKey: true });
    expect(keys('g1')).toEqual(['email']);
  });

  it('duplicate with Ctrl+D, delete with Delete and clear the selection with Esc', async () => {
    const { ask, press, keys, state } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await press('d', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'new1', 'email']);
    await press('Delete');
    expect(keys('g1')).toEqual(['name', 'email']);
    await press('Escape');
    expect(state().selection).toBeNull();
  });

  it('add a page at the end and show it', async () => {
    const { ask, state, canvas } = await viewOf();
    await ask({ kind: 'addPage' });
    expect(state().definition.pages.map((page) => page.key)).toEqual(['p1', 'p2', 'new1']);
    expect(canvas.shown).toBe(2);
  });

  it('add the questions a paste gives, as fields at the end of the page', async () => {
    const { ask, keys, workspace, state } = await viewOf();
    opened.value = {
      questions: [
        { label: 'Your name', type: 'text' },
        { label: 'Do you need step-free access?', type: 'yesno' },
      ],
    };
    await ask({ kind: 'paste' });
    expect(keys('g1')).toEqual(['name', 'email', 'new1', 'new2']);
    expect(locate(state().definition, 'new2')?.element).toMatchObject({
      type: 'yesno',
      alias: 'doYouNeedStepFreeAccess',
      label: { '': 'Do you need step-free access?' },
    });
    expect(workspace.said).toEqual(['Added 2 fields']);
  });

  it('offer Undo after a delete, which takes back that delete only while it is the last change', async () => {
    const { ask, keys, toasts } = await viewOf();
    await ask({ kind: 'delete', key: 'email' });
    await toasts.shown[0]?.data.undo();
    expect(keys('g1')).toEqual(['name', 'email']);

    // Another change closes the toast, and its Undo leaves that change alone.
    await ask({ kind: 'delete', key: 'email' });
    await ask({ kind: 'duplicate', key: 'name' });
    expect(toasts.shown[1]?.closed).toBe(true);
    await toasts.shown[1]?.data.undo();
    expect(keys('g1')).toEqual(['name', 'new1']);
  });

  it('let go of a selection the canvas no longer draws, and give the focus to Add field', async () => {
    const { ask, state, canvas } = await viewOf({
      pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [field('name')] }] }],
    });
    await ask({ kind: 'delete', key: 'name' });
    expect(state().selection).toBeNull();
    expect(shadow(canvas).activeElement?.classList.contains('bo-add-field')).toBe(true);
  });

  it("edit a label in the culture the canvas shows it in, and not a heading's that has no text", async () => {
    const { ask, state, canvas } = await viewOf({
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [
                { ...field('email', 'email'), label: { '': 'Email', en: 'Your email' } },
                { key: 'h', type: 'heading', settings: { text: {} } },
              ],
            },
          ],
        },
      ],
    });
    await ask({ kind: 'editLabel', key: 'email' });
    await ask({ kind: 'label', key: 'email', text: 'Email address' });
    expect(locate(state().definition, 'email')?.element).toMatchObject({ label: { '': 'Email', en: 'Email address' } });
    await ask({ kind: 'editLabel', key: 'h' });
    expect(canvas.editing).toBeNull();
  });

  it('switch the width and keep it in this browser', async () => {
    const { ask, canvas } = await viewOf();
    await ask({ kind: 'width', width: 'tablet' });
    expect(canvas.width).toBe('tablet');
    expect(localStorage.getItem('jfb.designer.width')).toBe('tablet');
  });
});

describe('the moves', () => {
  it('make a drop where it lands, say where the element is now, and offer Undo', async () => {
    const { view, ask, keys, state, canvas, workspace, toasts } = await viewOf();
    await ask({
      kind: 'drop',
      source: { kind: 'field', key: 'name', label: 'name' },
      place: { container: 'g2', index: 1 },
    });
    expect([keys('g1'), keys('g2')]).toEqual([['email'], ['note', 'name']]);
    expect(state().selection).toBe('name');
    expect(canvas.shown).toBe(1);
    expect(workspace.said).toEqual(['Moved name']);
    expect(await heard(view)).toBe('Moved. name: position 2 of 2 in Group 1, Page 2.');
    expect(toasts.shown.map((toast) => toast.data.message)).toEqual(['Field moved.']);
    await toasts.shown[0]?.data.undo();
    expect([keys('g1'), keys('g2')]).toEqual([['name', 'email'], ['note']]);
  });

  it('say a group and a page by their places on the page and among the pages, a page keeping the focus on its step', async () => {
    const { view, ask, state, canvas } = await viewOf();
    await ask({
      kind: 'drop',
      source: { kind: 'group', key: 'g2', label: 'g2' },
      place: { container: 'p1', index: 0 },
    });
    expect(state().definition.pages[0]?.groups.map((group) => group.key)).toEqual(['g2', 'g1']);
    expect(await heard(view)).toBe('Moved. Group 1: position 1 of 2 on Page 1.');
    await ask({
      kind: 'drop',
      source: { kind: 'page', key: 'p2', label: 'Page 2' },
      place: { container: null, index: 0 },
    });
    expect(state().definition.pages.map((page) => page.key)).toEqual(['p2', 'p1']);
    expect(await heard(view)).toBe('Moved. Page 1: page 1 of 2.');
    expect(focusedIn(canvas)).toBe('p2 step');
  });

  it('add a field of a palette type dropped on the page, and keep the type as recently used', async () => {
    const { ask, keys, state, workspace, toasts } = await viewOf();
    await ask({
      kind: 'drop',
      source: { kind: 'new', type: 'phone', label: 'Phone' },
      place: { container: 'g1', index: 1 },
    });
    expect(keys('g1')).toEqual(['name', 'new1', 'email']);
    expect(state().selection).toBe('new1');
    expect(workspace.said).toEqual(['Added Phone']);
    expect(toasts.shown).toEqual([]);
    expect(JSON.parse(localStorage.getItem('jfb.designer.recent') ?? '[]')).toEqual(['phone']);
  });

  it('tell the canvas why the form refuses a place, and nothing for a place where it already is', async () => {
    const { canvas, pick } = await viewOf();
    const captcha = { kind: 'new', type: 'captcha', label: 'Captcha placement' } as const;
    expect(canvas.refusalOf(captcha, { container: 'g1', index: 0 })).toBeNull();
    await pick({ kind: 'field', type: 'captcha' });
    expect(canvas.refusalOf(captcha, { container: 'g1', index: 0 })).toBe(
      'A form holds one Captcha placement at most.',
    );
    expect(canvas.refusalOf({ kind: 'field', key: 'name', label: 'name' }, { container: 'g1', index: 0 })).toBeNull();
    expect(canvas.refusalOf({ kind: 'group', key: 'g1', label: 'g1' }, { container: 'g1', index: 0 })).toBe(
      "It can't go there.",
    );
    expect(canvas.refusalOf({ kind: 'field', key: 'name', label: 'name' }, null)).toBe("It can't go there.");
  });

  it('show a refused drop at the element and say it, without a toast, until the next change', async () => {
    const { view, ask, canvas, toasts } = await viewOf();
    await ask({ kind: 'refused', source: { kind: 'field', key: 'name', label: 'name' }, reason: "It can't go there." });
    expect(canvas.refusal).toEqual({ key: 'name', text: "It can't go there." });
    expect(await heard(view)).toBe("It can't go there.");
    expect(toasts.shown).toEqual([]);
    await ask({ kind: 'select', key: 'email' });
    expect(canvas.refusal).toBeNull();
    // A palette type refused has no element to show it at: it is said.
    await ask({ kind: 'refused', source: { kind: 'new', type: 'captcha', label: 'Captcha' }, reason: 'No.' });
    expect(canvas.refusal).toBeNull();
    expect(await heard(view)).toBe('No.');
  });

  it('move the field with Alt+Down into the next page, and keep the focus on the control the keys were pressed on', async () => {
    const { view, canvas, keys, toasts, workspace } = await viewOf();
    await pressOn(view, control(canvas, 'email', 'handle'), 'ArrowDown', { altKey: true });
    expect([keys('g1'), keys('g2')]).toEqual([['name'], ['email', 'note']]);
    expect(canvas.shown).toBe(1);
    expect(focusedIn(canvas)).toBe('email handle');
    expect(await heard(view)).toBe('Moved. email: position 1 of 2 in Group 1, Page 2.');
    expect(workspace.said).toEqual(['Moved email']);
    // Keyboard moves come one after another: no toast.
    expect(toasts.shown).toEqual([]);
    await pressOn(view, control(canvas, 'email', 'select'), 'ArrowUp', { altKey: true });
    expect(keys('g1')).toEqual(['name', 'email']);
    expect(canvas.shown).toBe(0);
    expect(focusedIn(canvas)).toBe('email select');
  });

  it('move the selection with Alt+Up from anywhere, and say at the field why it goes no further', async () => {
    const { view, ask, press, canvas, keys } = await viewOf();
    await ask({ kind: 'select', key: 'email' });
    await press('ArrowUp', { altKey: true });
    expect(keys('g1')).toEqual(['email', 'name']);
    await press('ArrowUp', { altKey: true });
    expect(keys('g1')).toEqual(['email', 'name']);
    expect(canvas.refusal).toEqual({ key: 'email', text: "It can't move any further." });
    expect(await heard(view)).toBe("It can't move any further.");
  });

  it('move a page by its step, the focus staying on the step', async () => {
    const { view, canvas, state } = await viewOf();
    await pressOn(view, control(canvas, 'p2', 'step'), 'ArrowUp', { altKey: true });
    expect(state().definition.pages.map((page) => page.key)).toEqual(['p2', 'p1']);
    expect(focusedIn(canvas)).toBe('p2 step');
    expect(await heard(view)).toBe('Moved. Page 1: page 1 of 2.');
  });

  it('act with Delete, Ctrl+D and F2 on the element the focus is on, and Undo gives the focus back to it', async () => {
    const { view, ask, press, canvas, keys, state } = await viewOf();
    await ask({ kind: 'select', key: 'email' });
    await pressOn(view, control(canvas, 'name', 'select'), 'Delete');
    expect(keys('g1')).toEqual(['email']);
    await press('z', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email']);
    expect(state().selection).toBe('name');
    expect(focusedIn(canvas)).toBe('name select');
    await pressOn(view, control(canvas, 'email', 'handle'), 'd', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email', 'new1']);
    await pressOn(view, control(canvas, 'name', 'select'), 'F2');
    expect(canvas.editing).toBe('name');
  });

  it('select the element a key acts on first, so the focus comes back to it after the edit and after Undo', async () => {
    const { view, press, canvas, keys, state } = await viewOf();
    // F2 on a field the focus is on, with nothing selected: the label kept, the focus back on the field.
    await pressOn(view, control(canvas, 'name', 'select'), 'F2');
    expect(state().selection).toBe('name');
    await ask(view, { kind: 'label', key: 'name', text: 'Full name' });
    expect(focusedIn(canvas)).toBe('name select');
    // Ctrl+D on another, then Ctrl+Z: the copy goes, and the focus comes back to the field copied.
    await pressOn(view, control(canvas, 'email', 'handle'), 'd', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email', 'new1']);
    await press('z', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email']);
    expect(focusedIn(canvas)).toBe('email select');
    // Alt+Down on a field that isn't selected, then Ctrl+Z: back in its place, with the focus.
    await pressOn(view, control(canvas, 'name', 'handle'), 'ArrowDown', { altKey: true });
    expect(keys('g1')).toEqual(['email', 'name']);
    await press('z', { ctrlKey: true });
    expect(keys('g1')).toEqual(['name', 'email']);
    expect(focusedIn(canvas)).toBe('name select');
  });

  it('give the focus to Add field when Undo takes away the field that had it, with nothing left selected', async () => {
    const { view, ask: request, press, canvas, keys } = await viewOf();
    await request({
      kind: 'drop',
      source: { kind: 'new', type: 'phone', label: 'Phone' },
      place: { container: 'g1', index: 2 },
    });
    expect(keys('g1')).toEqual(['name', 'email', 'new1']);
    expect(focusedIn(canvas)).toBe('new1 select');
    await press('z', { ctrlKey: true });
    await settle(view);
    expect(keys('g1')).toEqual(['name', 'email']);
    expect(focusedIn(canvas)).toContain('bo-add-field');
  });

  it('change nothing with a key while a drag lasts', async () => {
    const { view, canvas, keys } = await viewOf();
    const handle = control(canvas, 'name', 'handle');
    const event = new MouseEvent('pointerdown', { button: 0 });
    Object.defineProperty(event, 'pointerId', { value: 7 });
    handle.dispatchEvent(new CustomEvent('noop'));
    canvas.dragNew(event as PointerEvent, handle, 'phone', 'Phone');
    const move = new MouseEvent('pointermove', { clientX: 0, clientY: 30 });
    Object.defineProperty(move, 'pointerId', { value: 7 });
    window.dispatchEvent(move);
    await pressOn(view, control(canvas, 'email', 'select'), 'Delete');
    expect(keys('g1')).toEqual(['name', 'email']);
    const cancel = new MouseEvent('pointercancel');
    Object.defineProperty(cancel, 'pointerId', { value: 7 });
    window.dispatchEvent(cancel);
    await pressOn(view, control(canvas, 'email', 'select'), 'Delete');
    expect(keys('g1')).toEqual(['name']);
  });

  it("go through the page's elements with Up and Down from the control the focus is on", async () => {
    const { view, canvas } = await viewOf();
    await pressOn(view, control(canvas, 'p1', 'select'), 'ArrowDown');
    expect(focusedIn(canvas)).toBe('g1 select');
    await pressOn(view, control(canvas, 'g1', 'select'), 'ArrowDown');
    expect(focusedIn(canvas)).toBe('name select');
    await pressOn(view, control(canvas, 'name', 'handle'), 'ArrowDown');
    expect(focusedIn(canvas)).toBe('email select');
    // Past the last element the focus stays where it is.
    await pressOn(view, control(canvas, 'email', 'select'), 'ArrowDown');
    expect(focusedIn(canvas)).toBe('email select');
    await pressOn(view, control(canvas, 'name', 'select'), 'ArrowUp');
    expect(focusedIn(canvas)).toBe('g1 select');
  });

  it('move a field with Move to... to the end of the place chosen, with Undo, and leave it on Cancel', async () => {
    const { ask, canvas, keys, toasts } = await viewOf();
    opened.value = { key: 'g2' };
    await ask({ kind: 'moveTo', key: 'name' });
    expect(opened.dialogs).toEqual([
      {
        alias: 'Jiron.FormBuilder.Modal.MoveTo',
        data: {
          name: 'name',
          sections: [
            {
              heading: 'Page 1',
              places: [{ key: 'g1', name: 'Group 1 (where it is now)', refusal: null, disabled: false, inside: false }],
            },
            {
              heading: 'Page 2',
              places: [{ key: 'g2', name: 'Group 1', refusal: null, disabled: false, inside: false }],
            },
          ],
        },
      },
    ]);
    expect([keys('g1'), keys('g2')]).toEqual([['email'], ['note', 'name']]);
    expect(toasts.shown.map((toast) => toast.data.message)).toEqual(['Field moved.']);
    expect(focusedIn(canvas)).toBe('name select');

    opened.value = undefined;
    await ask({ kind: 'moveTo', key: 'name' });
    expect(keys('g2')).toEqual(['note', 'name']);
    expect(toasts.shown).toHaveLength(1);
    expect(focusedIn(canvas)).toBe('name select');
  });

  it("show where it can't go in Move to..., with why", async () => {
    const { ask } = await viewOf({
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [field('captcha', 'captcha'), { ...field('kids', 'repeater'), fields: [field('kid')] }],
            },
          ],
        },
      ],
    });
    await ask({ kind: 'moveTo', key: 'captcha' });
    expect((opened.dialogs[0]?.data as { sections: unknown }).sections).toEqual([
      {
        heading: 'Page 1',
        places: [
          { key: 'g1', name: 'Group 1 (where it is now)', refusal: null, disabled: false, inside: false },
          {
            key: 'kids',
            name: 'kids, inside Group 1',
            refusal: "A repeatable group can't hold that.",
            disabled: true,
            inside: true,
          },
        ],
      },
    ]);
  });

  it('open the Keyboard shortcuts with ?, and not while typing', async () => {
    const { view, press } = await viewOf();
    await press('?');
    expect(opened.dialogs.map((dialog) => dialog.alias)).toEqual(['Jiron.FormBuilder.Modal.Shortcuts']);
    const box = document.createElement('input');
    shadow(view).append(box);
    box.dispatchEvent(new KeyboardEvent('keydown', { key: '?', bubbles: true, composed: true }));
    await settle(view);
    expect(opened.dialogs).toHaveLength(1);
  });

  it('pass a palette type pressed on to the canvas, which follows the drag', async () => {
    const { pick, canvas } = await viewOf();
    const dragNew = vi.spyOn(canvas, 'dragNew').mockImplementation(() => undefined);
    const event = new MouseEvent('pointerdown') as PointerEvent;
    const handle = document.createElement('button');
    await pick({ kind: 'drag', type: 'phone', label: 'Phone', event, handle });
    expect(dragNew).toHaveBeenCalledWith(event, handle, 'phone', 'Phone');
  });
});
