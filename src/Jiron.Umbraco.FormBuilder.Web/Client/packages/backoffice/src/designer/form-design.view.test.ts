// The Design view (ROADMAP 9.3b): each request of the canvas and the palette becomes an edit of the editor model through
// the workspace, with what it says for screen readers: a pick at the place the palette was opened for, after the
// selection or at the end of the page; a label edited in place; a delete that Ctrl+Z takes back; a new page shown;
// pasted questions as fields; Esc; and a refusal said in words. A stand-in workspace drives the real undo history.
import { FORMULAS } from '@jfb/logic/calc';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost } from '../testing';
import type { FormCanvasElement } from './canvas/form-canvas.element';
import { CANVAS_REQUEST, type CanvasRequest } from './canvas/form-canvas.element';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { locate, type DesignDefinition } from './definition';
import type { Edit, EditorState } from './editor-model';
import { traitsOf, type EditorView } from './form-workspace.context';
import { EditorHistory } from './history';
import { PALETTE_PICK, type PalettePick } from './palette/palette.element';
import { builtInTypes, englishLook } from './preview/fixtures';

const opened = vi.hoisted(() => ({ value: undefined as unknown }));

vi.mock('./preview/look', async (original) => ({
  ...(await original<typeof import('./preview/look')>()),
  readLook: async () => englishLook,
  themeSheets: async () => [],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async () => opened.value,
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

async function viewOf(definition = DEFINITION) {
  const workspace = new StandIn(definition);
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
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
  return { workspace, view, canvas, ask, pick, press, state, keys };
}

beforeAll(registerEnglish);

beforeEach(() => localStorage.clear());

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

  it('switch the width and keep it in this browser', async () => {
    const { ask, canvas } = await viewOf();
    await ask({ kind: 'width', width: 'tablet' });
    expect(canvas.width).toBe('tablet');
    expect(localStorage.getItem('jfb.designer.width')).toBe('tablet');
  });
});
