// The Design view (ROADMAP 9.3b): each request of the canvas and the palette becomes an edit of the editor model through
// the workspace, with what it says for screen readers: a pick at the place the palette was opened for, after the
// selection or at the end of the page; a label edited in place; a delete that Ctrl+Z takes back; a new page shown;
// pasted questions as fields; Esc; and a refusal said in words. Its moves (ROADMAP 9.3c): a drop with Undo, a palette
// type dropped, a refusal shown at the element, Alt+Up and Alt+Down with the focus kept, Up and Down between elements,
// Move to... and the Keyboard shortcuts. The inspector (ROADMAP 9.4a): beside the canvas with the selection, its changes
// as edits that typing in one box makes one step, the palette folded beside it up to 1024 px, an alias that entries know
// changed only after a question, the element's "...", the language the canvas shows, and the keys from inside it.
// Change type (ROADMAP 9.4d): the dialog with each type's preview, the change made with Undo, a type the site can't
// offer refused, and a read that fails said. A stand-in workspace drives the real undo history.
import { FORMULAS } from '@jfb/logic/calc';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import { CANVAS_REQUEST, type CanvasRequest } from './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { locate, type DesignDefinition, type DesignField } from './definition';
import type { Edit, EditorState } from './editor-model';
import { readOnce, traitsOf, type EditorView } from './form-workspace.context';
import { EditorHistory } from './history';
import { PALETTE_PICK, type PalettePick } from './palette/palette.element';
import { builtInChanges, builtInTypes, englishLook } from './preview/fixtures';
import type { UndoNotificationData } from '../ui/undo-notification.element';

// What the dialogs answer, and each dialog opened with what it was given.
const opened = vi.hoisted(() => ({
  value: undefined as unknown,
  dialogs: [] as { alias: string; data: unknown }[],
  confirmed: false,
}));
// Whether what the canvas draws with can be read, the cultures it was asked for, and whether the site has German too.
const reading = vi.hoisted(() => ({
  fails: false,
  cultures: [] as (string | undefined)[],
  german: false,
  late: null as string | null,
}));

vi.mock('./preview/look', async (original) => ({
  ...(await original<typeof import('./preview/look')>()),
  readLook: async (_host: unknown, culture?: string) => {
    reading.cultures.push(culture);
    if (culture !== undefined && culture === reading.late) await new Promise((done) => setTimeout(done, 40));
    if (reading.fails) return undefined;
    if (!reading.german) return englishLook;
    const languages = [
      { culture: 'en-US', name: 'English (United States)', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
    ];
    return { ...englishLook, languages, culture: culture ?? 'en-US' };
  },
  themeSheets: async () => [],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: unknown, args?: { data?: unknown }) => {
    opened.dialogs.push({ alias: String(token), data: args?.data });
    return opened.value;
  },
  umbConfirmModal: async (_host: unknown, data: unknown) => {
    opened.dialogs.push({ alias: 'confirm', data });
    if (!opened.confirmed) throw new Error('cancelled');
  },
}));

/** What the server resolves a draft's options to, and each ask: the form, the field, its options and the culture. */
const resolving = vi.hoisted(() => ({
  asked: [] as { form: string; field: string; options: unknown; culture: string }[],
  answer: { options: [{ value: '1', label: 'January', parentValues: [] }], isAvailable: true } as {
    options: { value: string; label?: string; parentValues: string[] }[];
    isAvailable: boolean;
  },
}));

vi.mock('./inspector/option-data', () => ({
  readOptionSources: async () => ({ kinds: [], sources: [] }),
  readOptionLists: async () => [],
  resolveOptions: async (_host: unknown, form: string, field: DesignField, culture: string) => {
    resolving.asked.push({ form, field: field.key, options: field['options'], culture });
    return resolving.answer;
  },
}));

/** The media items Umbraco's imaging was asked for, each answered with a URL of its own. */
const imaging = vi.hoisted(() => ({ asked: [] as string[][] }));

vi.mock('@umbraco-cms/backoffice/imaging', () => ({
  UmbImagingRepository: class {
    async requestResizedItems(keys: string[]) {
      imaging.asked.push(keys);
      return { data: keys.map((key) => ({ unique: key, url: `/media/${key}.svg` })) };
    }
  },
}));

const { FormDesignViewElement, isCrowded, isNarrow, mediaOf, OPTIONS_PAUSE, readWidth } =
  await import('./form-design.view');
const { INSPECTOR_REQUEST } = await import('./inspector/inspector.element');
const { MOVE_TO_MODAL_ALIAS } = await import('./moves/move-to.modal.token');
type ChangeTypeModalData = import('./change-type/change-type.modal.token').ChangeTypeModalData;

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
  /** Each type's changes as the server serves them; none read while `unreadable`. */
  unreadable = false;
  readonly asked: string[] = [];
  readonly changesOf = readOnce(async (type: string) => {
    this.asked.push(type);
    return this.unreadable ? undefined : builtInChanges[type]?.changes;
  });
  readonly #history: EditorHistory;
  readonly #editor: UmbObjectState<EditorView>;
  #next = 0;
  readonly editor;
  readonly types = new UmbObjectState(builtInTypes);
  readonly fieldTypes = this.types.asObservable();
  readonly paletteGroups = new UmbObjectState([{ alias: 'text', name: '#jfbFieldPalette_text' }]).asObservable();
  readonly model = {
    traitsOf: (type: string) => {
      const types = this.types.getValue();
      const found = Object.hasOwn(types, type) ? types[type] : undefined;
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
  getUnique = () => 'form1';
  getEditorState = (): EditorState => this.#history.state;

  /** The aliases entries know, by field: changing one asks first. */
  readonly kept: Record<string, string> = {};
  keptAliasOf = (key: string): string | null => this.kept[key] ?? null;

  edit(edit: Edit, said: string, merge?: string): Edit {
    const result = this.#history.apply(edit, said, merge);
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
  opened.confirmed = false;
  reading.cultures = [];
  reading.german = false;
  reading.late = null;
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

  it("change a field's type with Change type..., which names what goes first, with Undo, and leave it on Cancel", async () => {
    const name: DesignField = {
      ...field('name'),
      placeholder: { '': 'Your full name' },
      settings: { maxLength: 50 },
    };
    const phone: DesignField = {
      ...field('phone'),
      rules: [{ key: 'r', effect: 'show', conditions: [{ field: 'name', op: 'contains', value: 'a' }] }],
    };
    const { ask, canvas, state, toasts, workspace } = await viewOf({
      pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [name, phone] }] }],
    });
    // A field selected has its changes read ahead, which the dialog then uses.
    await ask({ kind: 'select', key: 'name' });
    expect(workspace.asked).toEqual(['text']);
    opened.value = { to: 'dropdown' };
    await ask({ kind: 'changeType', key: 'name' });

    expect(workspace.asked).toEqual(['text', 'dropdown']);
    const data = opened.dialogs[0]?.data as ChangeTypeModalData;
    expect(opened.dialogs[0]?.alias).toBe('Jiron.FormBuilder.Modal.ChangeType');
    expect([data.name, data.type]).toEqual(['name', 'Short text']);
    expect(data.targets.map((target) => target.alias)).toEqual(
      builtInChanges['text']?.changes.map((change) => change.to),
    );
    const dropdown = must(
      data.targets.find((target) => target.alias === 'dropdown'),
      'the dropdown',
    );
    expect(dropdown).toMatchObject({ name: 'Dropdown', group: 'choices', refusal: null });
    expect(dropdown.words.goes).toContain('Placeholder: Your full name');
    expect(dropdown.words.needs).toEqual(['At least one option', 'A rule of phone uses "contains"']);
    expect(data.targets[0]).toMatchObject({ alias: 'textarea', group: 'Text' });

    const changed = locate(state().definition, 'name')?.element as DesignField;
    expect([changed.type, changed['placeholder'], changed['settings'], changed.alias]).toEqual([
      'dropdown',
      undefined,
      undefined,
      'name',
    ]);
    expect(workspace.said.at(-1)).toBe('Changed name to Dropdown');
    expect(toasts.shown.map((toast) => toast.data.message)).toEqual(['Type changed to Dropdown.']);
    expect(focusedIn(canvas)).toBe('name select');
    await toasts.shown[0]?.data.undo();
    expect((locate(state().definition, 'name')?.element as DesignField).type).toBe('text');

    opened.value = undefined;
    await ask({ kind: 'changeType', key: 'name' });
    expect((locate(state().definition, 'name')?.element as DesignField).type).toBe('text');
    expect(toasts.shown).toHaveLength(1);
    expect(focusedIn(canvas)).toBe('name select');
  });

  it("refuse in Change type... a type the site lacks what it needs for, with why, and say when the types can't be read", async () => {
    const { ask, canvas, state, toasts, view, workspace } = await viewOf();
    const textarea = must(builtInTypes['textarea'], 'the long text');
    workspace.types.setValue({
      ...builtInTypes,
      textarea: {
        ...textarea,
        availability: { isAvailable: false, reason: '#jfbFieldTypeAvailability_captchaNoKeys' },
      },
    });
    opened.value = { to: 'textarea' };
    await ask({ kind: 'changeType', key: 'name' });
    const data = opened.dialogs[0]?.data as ChangeTypeModalData;
    expect(data.targets.find((target) => target.alias === 'textarea')?.refusal).toBe(
      "No captcha provider has its keys yet. They go in the site's configuration.",
    );
    // Were a refused type to come back, nothing would change.
    expect((locate(state().definition, 'name')?.element as DesignField).type).toBe('text');
    expect(toasts.shown).toEqual([]);

    workspace.unreadable = true;
    opened.dialogs = [];
    await ask({ kind: 'changeType', key: 'email' });
    expect(focusedIn(canvas)).toBe('email select');
    expect(opened.dialogs).toEqual([]);
    expect(toasts.shown.at(-1)?.data.message).toBe(
      "The types this field may become couldn't be read. Try again in a moment.",
    );

    // Asked twice while the first ask reads, the dialog opens once.
    workspace.unreadable = false;
    opened.value = undefined;
    opened.dialogs = [];
    for (let twice = 0; twice < 2; twice++)
      view.dispatchEvent(
        new CustomEvent(CANVAS_REQUEST, { detail: { kind: 'changeType', key: 'note' }, bubbles: true, composed: true }),
      );
    await settle(view);
    await new Promise((done) => setTimeout(done, 50));
    expect(opened.dialogs).toHaveLength(1);
  });

  it('say in Change type... what the entries screen does with older answers, and refuse what the form refuses', async () => {
    const { ask, workspace } = await viewOf({
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [field('amount', 'number'), { ...field('kids', 'repeater'), fields: [field('kid')] }],
            },
          ],
        },
      ],
    });
    workspace.kept['amount'] = 'amount';
    await ask({ kind: 'changeType', key: 'amount' });
    const amount = opened.dialogs[0]?.data as ChangeTypeModalData;
    expect(amount.targets.find((target) => target.alias === 'text')?.words.entries).toBe(
      "Entries already submitted keep their answers, but the entries screen's filters and sorting skip them.",
    );
    expect(amount.targets.find((target) => target.alias === 'textarea')?.words.entries).toBeTruthy();
    expect(amount.targets.find((target) => target.alias === 'slider')?.words.entries).toBeNull();

    // A child of a repeatable group can't become one more.
    workspace.types.setValue({
      ...builtInTypes,
      textarea: { ...must(builtInTypes['textarea'], 'the long text'), onePerForm: true },
    });
    opened.dialogs = [];
    await ask({ kind: 'changeType', key: 'kid' });
    const kid = opened.dialogs[0]?.data as ChangeTypeModalData;
    expect(kid.targets.find((target) => target.alias === 'textarea')?.refusal).toBe(
      "A repeatable group can't hold a field of the type Long text.",
    );
    expect(kid.targets.find((target) => target.alias === 'email')?.refusal).toBeNull();
    expect(workspace.kept['kid']).toBeUndefined();
  });

  it('change a field that is not selected, which the change selects, with the focus on it', async () => {
    const { ask, canvas, state } = await viewOf();
    expect(state().selection).toBeNull();
    opened.value = { to: 'textarea' };
    await ask({ kind: 'changeType', key: 'name' });
    expect((locate(state().definition, 'name')?.element as DesignField).type).toBe('textarea');
    expect(state().selection).toBe('name');
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

describe('the inspector in the Design view', () => {
  const inspectorOf = (view: HTMLElement) =>
    shadow(view).querySelector<HTMLElement & { mode?: string }>('jfb-designer-inspector');
  const tell = async (view: HTMLElement & { updateComplete: Promise<unknown> }, detail: unknown) => {
    view.dispatchEvent(new CustomEvent(INSPECTOR_REQUEST, { detail, bubbles: true, composed: true }));
    await settle(view);
  };
  const relabel = (label: string) => (element: Record<string, unknown>) => ({ ...element, label: { '': label } });

  it('folds the palette beside it in a view up to 1024 px, and not in a wider or a narrow one', () => {
    expect([620, 700, 701, 1024, 1025].map(isCrowded)).toEqual([false, false, true, true, false]);
  });

  it('shows the selected element beside the canvas, and none without a selection', async () => {
    const { view, ask } = await viewOf();
    expect(inspectorOf(view)).toBeNull();
    await ask({ kind: 'select', key: 'email' });
    const inspector = must(inspectorOf(view), 'the inspector');
    expect([inspector.getAttribute('mode'), (inspector as unknown as { selection: string }).selection]).toEqual([
      'docked',
      'email',
    ]);
    await ask({ kind: 'select', key: 'name' });
    expect((inspector as unknown as { selection: string }).selection).toBe('name');
  });

  it('makes its changes edits, typing in one box one step to undo, and says a refusal', async () => {
    const { view, workspace, ask, state, toasts } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: relabel('N'),
      said: 'Changed Label',
      merge: 'name:label:1',
    });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: relabel('Na'),
      said: 'Changed Label',
      merge: 'name:label:1',
    });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: relabel('Nam'),
      said: 'Changed Label',
      merge: 'name:label:1',
    });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'Nam' });
    expect(workspace.undo()).toBe('Changed Label');
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'name' });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: (element: Record<string, unknown>) => ({ ...element, alias: 'email' }),
      said: 'Changed Alias',
      merge: undefined,
    });
    expect(await heard(view)).toBe('Another field has that alias.');
    expect(toasts.shown.at(-1)?.data.message).toBe('Another field has that alias.');
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: (element: unknown) => element,
      said: 'x',
      merge: undefined,
    });
    expect(toasts.shown).toHaveLength(1);
  });

  it('changes an alias no entry knows at once, and keeps nothing of the one before', async () => {
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    const revert = vi.fn();
    await tell(view, { kind: 'alias', key: 'name', alias: 'fullName', revert });
    const changed = locate(state().definition, 'name')?.element;
    expect([changed?.alias, changed?.formerAliases]).toEqual(['fullName', undefined]);
    expect(opened.dialogs).toEqual([]);
    expect(revert).not.toHaveBeenCalled();
    expect(await heard(view)).toBe('Changed Alias');
  });

  it('asks before changing an alias that entries know, keeps it as a former alias, and gives it up when it comes back', async () => {
    const { view, ask, state, workspace } = await viewOf();
    workspace.kept['name'] = 'name';
    await ask({ kind: 'select', key: 'name' });
    const revert = vi.fn();
    await tell(view, { kind: 'alias', key: 'name', alias: 'fullName', revert });
    expect(opened.dialogs.map((dialog) => dialog.alias)).toEqual(['confirm']);
    expect(opened.dialogs[0]?.data).toMatchObject({ headline: 'Change the alias?', confirmLabel: 'Change the alias' });
    expect(revert).toHaveBeenCalledOnce();
    expect(locate(state().definition, 'name')?.element.alias).toBe('name');

    opened.confirmed = true;
    await tell(view, { kind: 'alias', key: 'name', alias: 'fullName', revert });
    expect(locate(state().definition, 'name')?.element.formerAliases).toEqual(['name']);
    await tell(view, { kind: 'alias', key: 'name', alias: 'givenName', revert });
    expect(opened.dialogs).toHaveLength(2);
    const twice = locate(state().definition, 'name')?.element;
    expect([twice?.alias, twice?.formerAliases]).toEqual(['givenName', ['name']]);
    await tell(view, { kind: 'alias', key: 'name', alias: 'name', revert });
    const back = locate(state().definition, 'name')?.element;
    expect([back?.alias, back?.formerAliases]).toEqual(['name', undefined]);
  });

  it('folds to its rail and opens again, and Close lets the element go with the focus on it', async () => {
    const { view, canvas, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'email' });
    await tell(view, { kind: 'fold' });
    expect(inspectorOf(view)?.getAttribute('mode')).toBe('rail');
    await tell(view, { kind: 'unfold' });
    expect(inspectorOf(view)?.getAttribute('mode')).toBe('docked');
    await tell(view, { kind: 'close' });
    await settle(view);
    expect(state().selection).toBeNull();
    expect(inspectorOf(view)).toBeNull();
    expect(focusedIn(canvas)).toBe('email select');
  });

  it('folds the palette to its rail beside it up to 1024 px, opens it over the canvas from there, and back once the selection goes', async () => {
    const { view, ask, pick, press } = await viewOf();
    Object.assign(view, { _crowded: true });
    await settle(view);
    const palette = () => shadow(view).querySelector('jfb-designer-palette')?.getAttribute('mode');
    expect(palette()).toBe('docked');
    await ask({ kind: 'select', key: 'email' });
    expect(palette()).toBe('rail');
    await pick({ kind: 'unfold' });
    expect(palette()).toBe('overlay');
    expect(shadow(view).querySelector('.scrim')).not.toBeNull();
    await press('Escape');
    expect(palette()).toBe('rail');
    await tell(view, { kind: 'fold' });
    expect(palette()).toBe('docked');
    await tell(view, { kind: 'unfold' });
    await ask({ kind: 'select', key: 'email' });
    await press('Escape');
    expect(palette()).toBe('docked');
  });

  it('opens over a narrow canvas with the focus on its heading, the canvas out of reach', async () => {
    const { view, canvas, ask } = await viewOf();
    Object.assign(view, { _narrow: true });
    await settle(view);
    await ask({ kind: 'select', key: 'email' });
    await settle(view);
    const inspector = must(inspectorOf(view), 'the inspector');
    expect(inspector.getAttribute('mode')).toBe('overlay');
    expect(canvas.hasAttribute('inert')).toBe(true);
    expect(shadow(inspector).activeElement?.localName).toBe('h2');
  });

  it('takes the focus to its heading over a narrow canvas once a pick has closed the palette over it', async () => {
    const { view, ask, pick } = await viewOf();
    Object.assign(view, { _narrow: true });
    await settle(view);
    await ask({ kind: 'addField', page: 'p1' });
    // A browser moves no focus into an inert element; jsdom does, so where the heading took it is read at the call.
    const reached: boolean[] = [];
    const focusHeading = vi
      .spyOn(
        must(customElements.get('jfb-designer-inspector'), 'the inspector').prototype as { focusHeading(): void },
        'focusHeading',
      )
      .mockImplementation(function (this: HTMLElement) {
        reached.push(!this.closest('[inert]') && !this.hasAttribute('inert'));
      });
    try {
      await pick({ kind: 'field', type: 'phone' });
      await settle(view);
      expect(inspectorOf(view)?.getAttribute('mode')).toBe('overlay');
      expect(reached).toContain(true);
    } finally {
      focusHeading.mockRestore();
    }
  });

  it('starts a new page after an element from its "..." and copies a field\'s alias', async () => {
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await ask({ kind: 'newPageAfter', key: 'name' });
    expect(
      state().definition.pages.map((page) => page.groups.flatMap((group) => group.fields.map((each) => each.key))),
    ).toEqual([['name'], ['email'], ['note']]);
    expect(locate(state().definition, state().selection ?? '')?.kind).toBe('page');
    expect(await heard(view)).toBe('Started a new page after name');
    await ask({ kind: 'newPageAfter', key: 'g2' });
    expect(state().definition.pages).toHaveLength(4);

    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await tell(view, { kind: 'element', action: 'copyAlias', key: 'email' });
    expect(writeText).toHaveBeenCalledWith('email');
    expect(await heard(view)).toBe('Copied email');
  });

  it('says when the clipboard refuses an alias, and copies nothing for a group', async () => {
    const { view, toasts } = await viewOf();
    const writeText = vi.fn(async () => {
      throw new Error('denied');
    });
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await tell(view, { kind: 'element', action: 'copyAlias', key: 'name' });
    expect(toasts.shown.at(-1)?.data.message).toBe("The alias couldn't be copied.");
    await tell(view, { kind: 'element', action: 'copyAlias', key: 'g1' });
    expect(writeText).toHaveBeenCalledOnce();
  });

  it('acts from its bar over a narrow canvas: Move to..., Duplicate and Delete', async () => {
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'email' });
    await tell(view, { kind: 'element', action: 'duplicate', key: 'email' });
    expect(state().definition.pages[0]?.groups[0]?.fields).toHaveLength(3);
    await tell(view, { kind: 'element', action: 'delete', key: 'email' });
    expect(locate(state().definition, 'email')).toBeUndefined();
    await tell(view, { kind: 'element', action: 'moveTo', key: 'name' });
    expect(opened.dialogs.at(-1)?.alias).toBe(MOVE_TO_MODAL_ALIAS);
  });

  it('shows the language chosen on a site with two, in the canvas and the inspector', async () => {
    reading.german = true;
    const { view, ask, canvas } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await ask({ kind: 'culture', culture: 'de-DE' });
    await settle(view);
    expect(reading.cultures).toEqual([undefined, 'de-DE']);
    expect((canvas as unknown as { look: { culture: string } }).look.culture).toBe('de-DE');
    expect((inspectorOf(view) as unknown as { culture: string }).culture).toBe('de-DE');
  });

  it('keeps the element shortcuts out of the inspector, where Delete in a box would take the field, and Escape lets go', async () => {
    const { view, canvas, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'email' });
    const inspector = must(inspectorOf(view), 'the inspector');
    const sectionButton = must(shadow(inspector).querySelector<HTMLButtonElement>('.section-head button'), 'a section');
    await pressOn(view, sectionButton, 'Delete');
    await pressOn(view, sectionButton, 'd', { ctrlKey: true });
    await pressOn(view, sectionButton, 'ArrowUp', { altKey: true });
    expect(locate(state().definition, 'email')?.index).toBe(1);
    expect(state().definition.pages[0]?.groups[0]?.fields).toHaveLength(2);
    const select = document.createElement('select');
    shadow(inspector).append(select);
    await pressOn(view, select, 'Escape');
    expect(state().selection).toBe('email');
    await pressOn(view, sectionButton, 'Escape');
    expect(state().selection).toBeNull();
    await settle(view);
    expect(focusedIn(canvas)).toBe('email select');
  });

  it('undoes from the inspector without taking the focus out of it', async () => {
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: relabel('Full name'),
      said: 'Changed Label',
      merge: undefined,
    });
    const inspector = must(inspectorOf(view), 'the inspector');
    const sectionButton = must(shadow(inspector).querySelector<HTMLButtonElement>('.section-head button'), 'a section');
    await pressOn(view, sectionButton, 'z', { ctrlKey: true });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'name' });
    expect(shadow(inspector).activeElement).toBe(sectionButton);
    await pressOn(view, sectionButton, 'y', { ctrlKey: true });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'Full name' });
    expect(shadow(inspector).activeElement).toBe(sectionButton);
  });

  it("edits a label in place in another language as that language's text, and leaves the default's words alone", async () => {
    reading.german = true;
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'culture', culture: 'de-DE' });
    await ask({ kind: 'label', key: 'name', text: 'Ihr Name' });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'name', 'de-DE': 'Ihr Name' });
    // Enter on the English words as the canvas showed them writes no German text.
    await ask({ kind: 'label', key: 'email', text: 'email' });
    expect(locate(state().definition, 'email')?.element.label).toEqual({ '': 'email' });
    await settle(view);
  });

  it('undoes from a switch or a list box in it, and folds with the focus on the rail', async () => {
    const { view, ask, state } = await viewOf();
    await ask({ kind: 'select', key: 'name' });
    await tell(view, {
      kind: 'change',
      key: 'name',
      update: relabel('Full name'),
      said: 'Changed Label',
      merge: undefined,
    });
    const inspector = must(inspectorOf(view), 'the inspector');
    const required = must(shadow(inspector).querySelector<HTMLInputElement>('#prop-required'), 'Required');
    await pressOn(view, required, 'z', { ctrlKey: true });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'name' });
    const list = document.createElement('select');
    shadow(inspector).append(list);
    await pressOn(view, list, 'y', { ctrlKey: true });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'Full name' });
    const box = document.createElement('input');
    shadow(inspector).append(box);
    await pressOn(view, box, 'z', { ctrlKey: true });
    expect(locate(state().definition, 'name')?.element.label).toEqual({ '': 'Full name' });

    const forgot = vi.spyOn(inspector as unknown as { forget: () => void }, 'forget');
    await pressOn(view, required, 'z', { ctrlKey: true });
    expect(forgot).toHaveBeenCalledOnce();

    await tell(view, { kind: 'fold' });
    await settle(view);
    expect(shadow(inspector).activeElement?.getAttribute('label')).toBe('Open the inspector');
    await tell(view, { kind: 'unfold' });
    await settle(view);
    expect(shadow(inspector).activeElement?.localName).toBe('h2');
  });

  it('shows the language chosen last when an answer for one chosen before it comes later', async () => {
    reading.german = true;
    reading.late = 'de-DE';
    const { view, ask, canvas } = await viewOf();
    void ask({ kind: 'culture', culture: 'de-DE' });
    await ask({ kind: 'culture', culture: 'en-US' });
    await new Promise((done) => setTimeout(done, 80));
    await settle(view);
    expect((canvas as unknown as { look: { culture: string } }).look.culture).toBe('en-US');
  });
});

describe('options from elsewhere', () => {
  it("shows a field's options as the server resolves the draft's, asked again only when they change", async () => {
    resolving.asked.length = 0;
    const months = { source: 'code', config: { source: 'months' } };
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [
                { key: 'm', type: 'dropdown', alias: 'month', label: { '': 'Month' }, options: months },
                field('name'),
              ],
            },
          ],
        },
      ],
    };
    const { view, canvas, ask, state } = await viewOf(definition);
    await settle(view);
    const shown = async () => {
      const found = must(
        shadow(canvas).querySelector<HTMLElement & { updateComplete: Promise<unknown> }>('jfb-canvas-field[key="m"]'),
        'the month',
      );
      await settle(found);
      return [...found.querySelectorAll('option')].map((option) => text(option));
    };
    expect(resolving.asked).toEqual([{ form: 'form1', field: 'm', options: months, culture: 'en-US' }]);
    expect(await shown()).toContain('January');

    // Selecting another field asks nothing again; a change of the source does, once the changes pause.
    await ask({ kind: 'select', key: 'name' });
    expect(resolving.asked).toHaveLength(1);
    resolving.answer = { options: [{ value: 'mon', label: 'Monday', parentValues: [] }], isAvailable: true };
    const change = (options: unknown) =>
      view.dispatchEvent(
        new CustomEvent(INSPECTOR_REQUEST, {
          detail: {
            kind: 'change',
            key: 'm',
            update: (element: DesignField) => ({ ...element, options }),
            said: 'Changed Options from',
            merge: undefined,
          },
          bubbles: true,
          composed: true,
        }),
      );
    const paused = async () => {
      await new Promise((done) => setTimeout(done, OPTIONS_PAUSE + 50));
      await settle(view);
      await settle(view);
    };
    try {
      // Not after a pause either.
      await paused();
      expect(resolving.asked).toHaveLength(1);
      const weekdays = { source: 'code', config: { source: 'weekdays' } };
      change({ source: 'code', config: { source: 'week' } });
      await settle(view);
      change(weekdays);
      await settle(view);
      expect(resolving.asked).toHaveLength(1);
      await paused();
      expect(resolving.asked.map((each) => each.options)).toEqual([months, weekdays]);
      expect(await shown()).toContain('Monday');
      expect(locate(state().definition, 'm')?.element['options']).toEqual(weekdays);

      // A source the server can't give options from shows none, whatever the answer holds.
      resolving.answer = { options: [{ value: 'x', label: 'Stale', parentValues: [] }], isAvailable: false };
      change({ source: 'code', config: { source: 'gone' } });
      await paused();
      expect(resolving.asked).toHaveLength(3);
      const left = await shown();
      expect(left).not.toContain('Stale');
      expect(left).not.toContain('Monday');
    } finally {
      resolving.answer = { options: [{ value: '1', label: 'January', parentValues: [] }], isAvailable: true };
    }
  });
});

describe("the canvas's images", () => {
  it("are read through Umbraco's imaging, once, and a card shows its option's", async () => {
    imaging.asked.length = 0;
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'g1',
              fields: [
                {
                  key: 'r',
                  type: 'radio',
                  alias: 'room',
                  label: { '': 'Room' },
                  settings: { layout: 'cards' },
                  options: { items: [{ value: 'hall', label: { '': 'Hall' }, image: 'm1' }] },
                },
              ],
            },
          ],
        },
      ],
    };
    const { view, canvas } = await viewOf(definition);
    await vi.waitFor(async () => {
      const card = must(
        shadow(canvas).querySelector<HTMLElement & { updateComplete: Promise<unknown> }>('jfb-canvas-field[key="r"]'),
        'the room',
      );
      await settle(card);
      expect(card.querySelector('img')?.getAttribute('src')).toBe('/media/m1.svg');
    });
    await settle(view);
    expect(imaging.asked).toEqual([['m1']]);
  });
});
