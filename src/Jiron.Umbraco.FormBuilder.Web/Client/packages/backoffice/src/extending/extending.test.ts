// A site's own setting editors and field previews in the designer (ROADMAP 15.1, ADR-0233): the inspector draws a
// setting whose editor a `jfbSettingEditor` manifest registers with the site's element under its label, and keeps the
// value it gives; a site's field type carries its effective settings to the host of its `jfbFieldPreview`, which draws
// the site's element in the culture shown, also when the manifest registers later.
import type { FieldViewModel } from '@jfb/element/answer';
import type { CustomField } from '@jfb/element/views';
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { FieldTypeResponseModel } from '../api';
import type { DesignField } from '../designer/definition';
import { INSPECTOR_REQUEST, InspectorElement, type InspectorRequest } from '../designer/inspector/inspector.element';
import { fieldModel, type PreviewContext } from '../designer/preview/field-models';
import { builtInTypes, englishLook } from '../designer/preview/fixtures';
import { must, registerEnglish, settle, shadow, text } from '../testing';
import type { JfbPreviewField, JfbSetting } from '../../public/types/jiron-form-builder';
import { editorFor, previewFor } from './extension-registry';
import { FieldPreviewExtensionElement } from './field-preview-extension.element';
import { previewControl, previewField } from './preview-control';

vi.mock('../designer/inspector/option-data', async () => ({
  readOptionSources: async () => (await import('../designer/preview/fixtures')).optionSources,
  readOptionLists: async () => [],
  resolveOptions: async () => undefined,
}));

/** The site's editor of a list of colours, for the tests. */
class SwatchesEditor extends HTMLElement {
  value: unknown;
  setting: JfbSetting | undefined;
  label = '';
  culture = '';
  invalid = false;
}
if (!customElements.get('test-swatches-editor')) customElements.define('test-swatches-editor', SwatchesEditor);

/** The site's preview of a colour field, for the tests. */
class ColourPreview extends HTMLElement {
  field: JfbPreviewField | undefined;
  culture = '';
}
if (!customElements.get('test-colour-preview')) customElements.define('test-colour-preview', ColourPreview);

const COLOURS_SETTING = {
  ...must(builtInTypes['text']?.settings[0], "a text's first setting"),
  name: 'colours',
  kind: 'list',
  itemKind: 'text',
  default: ['#1f6feb'],
  label: 'Colours',
  editor: 'test.swatches',
  group: 'essentials',
};

const COLOUR_TYPE: FieldTypeResponseModel = {
  ...must(builtInTypes['text'], 'the text type'),
  alias: 'site-colour',
  name: 'Colour',
  settings: [COLOURS_SETTING],
};

const COLOUR: DesignField = { key: 'colour', type: 'site-colour', alias: 'colour', label: { '': 'Favourite colour' } };

const registered: string[] = [];

/** A site's field as `<jfb-form>` hands it to a control's writer. */
function custom(model: FieldViewModel | null | undefined): CustomField {
  const field = must(model, 'the model');
  if (field.control !== undefined && field.control.kind !== 'custom')
    throw new Error(`no custom control but ${field.control.kind}`);
  return field as CustomField;
}

function register(manifest: Parameters<typeof umbExtensionsRegistry.register>[0] & { alias: string }) {
  umbExtensionsRegistry.register(manifest);
  registered.push(manifest.alias);
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  for (const alias of registered.splice(0)) umbExtensionsRegistry.unregister(alias);
});

describe("a site's setting editor", () => {
  it('draws under the setting label in the inspector, with the setting, value, label and culture, and keeps its value', async () => {
    register({
      type: 'jfbSettingEditor',
      alias: 'Test.SettingEditor.Swatches',
      name: 'Swatches',
      elementName: 'test-swatches-editor',
      meta: { editor: 'test.swatches' },
    });
    expect(editorFor('test.swatches')?.alias).toBe('Test.SettingEditor.Swatches');
    const inspector = new InspectorElement();
    Object.assign(inspector, {
      definition: {
        pages: [{ key: 'p', groups: [{ key: 'g', fields: [{ ...COLOUR, settings: { colours: ['#cf222e'] } }] }] }],
      },
      selection: 'colour',
      types: { ...builtInTypes, 'site-colour': COLOUR_TYPE },
      culture: 'en-US',
      languages: [{ culture: 'en-US', name: 'English', isDefault: true }],
    });
    const requests: InspectorRequest[] = [];
    inspector.addEventListener(INSPECTOR_REQUEST, (event) =>
      requests.push((event as CustomEvent<InspectorRequest>).detail),
    );
    document.body.append(inspector);
    await settle(inspector);
    await vi.waitFor(() => expect(shadow(inspector).querySelector('test-swatches-editor')).not.toBeNull());
    await settle(inspector);

    const root = shadow(inspector);
    const editor = must(root.querySelector<SwatchesEditor>('test-swatches-editor'), "the site's editor");
    const host = must(editor.closest('jfb-setting-extension'), 'its host');
    expect(text(must(root.querySelector(`label[for="${host.id}"]`), 'the setting label'))).toBe('Colours');
    expect([editor.value, editor.label, editor.culture, editor.setting?.name, editor.setting?.kind]).toEqual([
      ['#cf222e'],
      'Colours',
      'en-US',
      'colours',
      'list',
    ]);

    editor.value = ['#2da44e', '#cf222e'];
    editor.dispatchEvent(new Event('change', { bubbles: true }));
    const change = must(requests.at(-1), 'a change');
    if (change.kind !== 'change') throw new Error(`no change but ${change.kind}`);
    expect((change.update(COLOUR) as { settings?: Record<string, unknown> }).settings).toEqual({
      colours: ['#2da44e', '#cf222e'],
    });
  });

  it('is not drawn for an editor no manifest registers, as before', async () => {
    const inspector = new InspectorElement();
    Object.assign(inspector, {
      definition: { pages: [{ key: 'p', groups: [{ key: 'g', fields: [COLOUR] }] }] },
      selection: 'colour',
      types: { ...builtInTypes, 'site-colour': COLOUR_TYPE },
      culture: 'en-US',
      languages: [{ culture: 'en-US', name: 'English', isDefault: true }],
    });
    document.body.append(inspector);
    await settle(inspector);
    expect(shadow(inspector).querySelector('jfb-setting-extension')).toBeNull();
    expect(text(shadow(inspector).querySelector('.body'))).not.toContain('Colours');
  });
});

describe("a site's field preview", () => {
  const context = (): PreviewContext => ({
    look: englishLook,
    types: { ...builtInTypes, 'site-colour': COLOUR_TYPE },
    culture: 'en-US',
    marker: 'none',
    layout: 'vertical',
    captcha: null,
    prefix: 'jfb-preview',
  });

  it("carries the type's effective settings in its model, the field's own over the defaults", () => {
    const model = must(fieldModel({ ...COLOUR, settings: { colours: ['#cf222e'] } }, context()), 'the model');
    const defaulted = must(fieldModel(COLOUR, context()), 'the model with defaults');
    expect(model.control).toEqual({ kind: 'custom', settings: { colours: ['#cf222e'] }, parts: {} });
    expect(previewField(defaulted)).toEqual({
      key: 'colour',
      type: 'site-colour',
      alias: 'colour',
      id: 'jfb-preview-colour',
      label: 'Favourite colour',
      required: false,
      settings: { colours: ['#1f6feb'] },
    });
  });

  it("draws the site's element with the field and the culture shown, also when its manifest comes later", async () => {
    const sheet = document.createElement('div');
    sheet.lang = 'de-DE';
    document.body.append(sheet);
    const host = must(
      previewControl()(custom(fieldModel(COLOUR, context())), document, englishLook.classes),
      'the host',
    ) as FieldPreviewExtensionElement;
    sheet.append(host);
    await settle(host);
    expect(host.firstElementChild).toBeNull();
    expect(previewFor('site-colour')).toBeUndefined();

    register({
      type: 'jfbFieldPreview',
      alias: 'Test.FieldPreview.Colour',
      name: 'Colour preview',
      elementName: 'test-colour-preview',
      meta: { fieldType: 'site-colour' },
    });
    await vi.waitFor(() => expect(host.querySelector('test-colour-preview')).not.toBeNull());

    const preview = must(host.querySelector<ColourPreview>('test-colour-preview'), "the site's preview");
    expect([preview.field?.label, preview.field?.settings, preview.culture]).toEqual([
      'Favourite colour',
      { colours: ['#1f6feb'] },
      'de-DE',
    ]);
  });
});
