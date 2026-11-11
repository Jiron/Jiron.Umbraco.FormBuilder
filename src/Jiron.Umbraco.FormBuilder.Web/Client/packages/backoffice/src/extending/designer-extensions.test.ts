// A site's own designer actions and field text actions (ADR-0018): the registry lists them as their manifests say; the
// designer host changes the draft's texts by their paths as one undoable edit, saves as Preview does, and refuses what
// is not a text; and the inspector draws a field text action beside the texts its manifest names, with the field, the
// text and the language, never beside a page's.
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { DesignDefinition } from '../designer/definition';
import { designerHost, type DesignerContext } from '../designer/designer-host';
import type { Edit, EditorState } from '../designer/editor-model';
import { InspectorElement } from '../designer/inspector/inspector.element';
import { builtInTypes } from '../designer/preview/fixtures';
import { stepsOf, withTexts } from '../designer/text-changes';
import { must, registerEnglish, settle, shadow } from '../testing';
import type { JfbFieldText, JfbFormDesigner } from '../../public/types/jiron-form-builder';
import { designerActions, fieldTextActionsFor } from './extension-registry';

/** A site's element beside a field's text, for the tests. */
class HelpAction extends HTMLElement {
  designer: JfbFormDesigner | undefined;
  fieldKey = '';
  text: JfbFieldText = 'label';
  language = '';
}
if (!customElements.get('test-help-action')) customElements.define('test-help-action', HelpAction);

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p',
      title: { '': 'About you' },
      groups: [
        {
          key: 'g',
          fields: [
            { key: 'name', type: 'text', alias: 'name', label: { '': 'Name', 'de-DE': 'Name' } },
            { key: 'dob', type: 'date', alias: 'dob', label: { '': 'DOB' } },
          ],
        },
      ],
    },
  ],
};

const registered: string[] = [];

function register(manifest: Parameters<typeof umbExtensionsRegistry.register>[0] & { alias: string }) {
  umbExtensionsRegistry.register(manifest);
  registered.push(manifest.alias);
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  for (const alias of registered.splice(0)) umbExtensionsRegistry.unregister(alias);
});

describe('a change of texts by their paths', () => {
  it('reads the paths the server walks, and nothing else', () => {
    expect(stepsOf('$.pages[0].groups[10].fields[2].help')).toEqual(['pages', 0, 'groups', 10, 'fields', 2, 'help']);
    expect(stepsOf('$.settings.submission.submitLabel')).toEqual(['settings', 'submission', 'submitLabel']);
    for (const path of ['', '$', 'pages[0]', '$.pages[x]', '$..label', '$.pages[0]].label', '$.1abc'])
      expect(stepsOf(path)).toBeNull();
  });

  it("adds a language beside a text's default, replaces one in any letter case, and writes a new default where a text belongs", () => {
    const next = must(
      withTexts(DEFINITION, [
        { path: '$.pages[0].groups[0].fields[1].label', culture: 'de-DE', text: 'Geburtsdatum' },
        { path: '$.pages[0].groups[0].fields[0].label', culture: 'de-de', text: 'Ihr Name' },
        { path: '$.pages[0].groups[0].fields[1].help', culture: '', text: 'As on your passport.' },
        { path: '$.pages[0].title', culture: '', text: 'You' },
      ]),
      'the changed definition',
    );
    const fields = must(next.pages[0]?.groups[0]?.fields, 'the fields');
    expect(fields[1]?.label).toEqual({ '': 'DOB', 'de-DE': 'Geburtsdatum' });
    expect(fields[0]?.label).toEqual({ '': 'Name', 'de-de': 'Ihr Name' });
    expect((fields[1] as Record<string, unknown>)['help']).toEqual({ '': 'As on your passport.' });
    expect(next.pages[0]?.title).toEqual({ '': 'You' });
    expect(DEFINITION.pages[0]?.groups[0]?.fields[1]?.label, 'the draft it was given stays as it was').toEqual({
      '': 'DOB',
    });
  });

  it('refuses all of them when one is not a text of the draft, or is a translation of nothing', () => {
    const refused = [
      { path: '$.pages[0].groups[0].fields[1].help', culture: 'de-DE', text: 'Hilfe' },
      { path: '$.pages[0].groups[0].fields[1].alias', culture: '', text: 'x' },
      { path: '$.pages[0].groups[0].fields[1].type', culture: '', text: 'email' },
      { path: '$.pages[0].groups[0].fields', culture: '', text: 'x' },
      { path: '$.pages[0].groups[0].fields[5].label', culture: '', text: 'x' },
      { path: '$.pages[0].groups[0].fields[1].secret', culture: '', text: 'x' },
      { path: '$.pages[0].groups[0].fields[1].label', culture: 'not a culture', text: 'x' },
      { path: '$.pages[0].groups[0].fields[1].label', culture: '', text: '   ' },
    ];
    for (const change of refused) {
      expect(
        withTexts(DEFINITION, [{ path: '$.pages[0].title', culture: '', text: 'Kept?' }, change]),
        change.path,
      ).toBeNull();
    }
    expect(withTexts(DEFINITION, [])).toBeNull();
  });
});

describe("the designer a site's extension gets", () => {
  function contextOf(unsaved: boolean, submit: () => Promise<void> = async () => undefined) {
    let state: EditorState = { definition: DEFINITION, selection: 'dob' };
    const edits: { readonly edit: Edit; readonly said: string }[] = [];
    const context: DesignerContext = {
      getUnique: () => 'form-key',
      getLanguage: () => null,
      getEditorState: () => state,
      getHasUnpersistedChanges: () => unsaved,
      requestSubmit: vi.fn(submit),
      edit: (edit: Edit, said: string) => {
        edits.push({ edit, said });
        if (edit.ok) state = edit.state;
        return edit;
      },
    };
    return { context, edits, state: () => state };
  }

  it('changes texts as one of its own edits, which says what it did, and keeps the selection', () => {
    const { context, edits, state } = contextOf(false);
    const designer = designerHost(context);

    expect(designer.formKey).toBe('form-key');
    expect(designer.getLanguage()).toBe('');
    expect(
      designer.changeTexts(
        [{ path: '$.pages[0].groups[0].fields[1].label', culture: 'de-DE', text: 'Geburtsdatum' }],
        'Translated 1 text',
      ),
    ).toBe(true);
    expect(edits.map((edit) => edit.said)).toEqual(['Translated 1 text']);
    expect(state().selection).toBe('dob');
    expect(state().definition.pages[0]?.groups[0]?.fields[1]?.label).toEqual({ '': 'DOB', 'de-DE': 'Geburtsdatum' });
    expect(designer.changeTexts([{ path: '$.pages[0].key', culture: '', text: 'x' }], 'Nothing')).toBe(false);
    expect(edits, 'a refused change is no edit').toHaveLength(1);
  });

  it('hands out a copy of the draft, and saves only what is unsaved, saying when the save was refused', async () => {
    const saved = contextOf(false);
    const copy = designerHost(saved.context).getDefinition() as DesignDefinition;
    (copy.pages[0] as { title?: unknown }).title = { '': 'Changed' };
    expect(saved.state().definition.pages[0]?.title).toEqual({ '': 'About you' });
    expect(await designerHost(saved.context).save()).toBe(true);
    expect(saved.context.requestSubmit).not.toHaveBeenCalled();

    const unsaved = contextOf(true);
    expect(await designerHost(unsaved.context).save()).toBe(true);
    expect(unsaved.context.requestSubmit).toHaveBeenCalledOnce();

    const refused = contextOf(true, async () => Promise.reject(new Error('conflict')));
    expect(await designerHost(refused.context).save()).toBe(false);
  });
});

describe("a site's designer actions and field text actions", () => {
  class TestAction {
    executed = 0;
    execute(): void {
      this.executed++;
    }
  }

  it('are listed as their manifests say, in their order', () => {
    register({
      type: 'jfbDesignerAction',
      alias: 'Test.Low',
      name: 'Low',
      weight: 10,
      api: TestAction as never,
      meta: { label: 'Low' },
    });
    register({
      type: 'jfbDesignerAction',
      alias: 'Test.High',
      name: 'High',
      weight: 90,
      api: TestAction as never,
      meta: { label: 'High' },
    });
    register({
      type: 'jfbFieldTextAction',
      alias: 'Test.Help',
      name: 'Help',
      elementName: 'test-help-action',
      meta: { texts: ['help', 'label'] },
    });

    expect(designerActions().map((manifest) => manifest.alias)).toEqual(['Test.High', 'Test.Low']);
    expect(fieldTextActionsFor('help').map((manifest) => manifest.alias)).toEqual(['Test.Help']);
    expect(fieldTextActionsFor('placeholder')).toEqual([]);
  });

  it("are drawn beside the field's texts their manifest names, with the field, the text and the language, never beside a page's", async () => {
    register({
      type: 'jfbFieldTextAction',
      alias: 'Test.Help',
      name: 'Help',
      elementName: 'test-help-action',
      meta: { texts: ['help', 'label'] },
    });
    const inspector = new InspectorElement();
    Object.assign(inspector, {
      definition: DEFINITION,
      selection: 'dob',
      types: builtInTypes,
      culture: 'en-US',
      languages: [{ culture: 'en-US', name: 'English', isDefault: true }],
    });
    document.body.append(inspector);
    await settle(inspector);
    // A date has a label and help, and no placeholder.
    await vi.waitFor(() => expect(shadow(inspector).querySelectorAll('jfb-field-text-actions')).toHaveLength(2));
    const hosts = [...shadow(inspector).querySelectorAll('jfb-field-text-actions')];
    await vi.waitFor(() =>
      expect(hosts.filter((host) => shadow(host).querySelector('test-help-action'))).toHaveLength(2),
    );

    const drawn = hosts.flatMap((host) => [...shadow(host).querySelectorAll<HelpAction>('test-help-action')]);
    expect(drawn.map((action) => [action.fieldKey, action.text, action.language])).toEqual([
      ['dob', 'label', ''],
      ['dob', 'help', ''],
    ]);

    inspector.selection = 'p';
    await settle(inspector);
    expect(shadow(inspector).querySelector('jfb-field-text-actions'), "a page's title has none").toBeNull();
  });
});
