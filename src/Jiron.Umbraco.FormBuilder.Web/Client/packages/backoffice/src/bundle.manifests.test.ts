import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import umbracoPackage from '../public/umbraco-package.json';
import { manifests } from './bundle.manifests';
import { FORM_PICKER_MODAL } from './picker/form-picker-modal.token';
import { FORM_PICKER_ALLOWED_UI_ALIAS, FORM_PICKER_THEME_UI_ALIAS } from './picker/manifests';
import { must } from './testing';

interface PackageManifest {
  id: string;
  name: string;
  version: string;
  allowTelemetry?: boolean;
  extensions: { type: string; alias: string; js?: string }[];
}

const packageManifest: PackageManifest = umbracoPackage;

describe('umbraco-package.json', () => {
  it('names the package sites install, so Umbraco shows one Jiron Form Builder', () => {
    expect(packageManifest.id).toBe('Jiron.Umbraco.FormBuilder');
    expect(packageManifest.name).toBe('Jiron Form Builder');
  });

  it('turns package telemetry off, explicitly (ADR-0025)', () => {
    expect(packageManifest.allowTelemetry).toBe(false);
  });

  it('registers exactly one bundle, the built file under /App_Plugins/JironFormBuilder/', () => {
    expect(packageManifest.extensions).toEqual([
      expect.objectContaining({
        type: 'bundle',
        alias: 'Jiron.FormBuilder.Bundle',
        js: '/App_Plugins/JironFormBuilder/jfb-backoffice.js',
      }),
    ]);
  });
});

describe('the bundle', () => {
  it('uses the Jiron.FormBuilder. alias prefix for every extension, each alias once', () => {
    const aliases = manifests.map((manifest) => manifest.alias);
    expect(aliases.every((alias) => alias.startsWith('Jiron.FormBuilder.'))).toBe(true);
    expect(new Set(aliases).size).toBe(aliases.length);
  });

  it('registers a dictionary for English and one for German, each loading its own texts (ADR-0057)', async () => {
    interface Localization {
      meta: { culture: string };
      js: () => Promise<{ default: unknown }>;
    }
    const localizations = manifests.filter((manifest) => manifest.type === 'localization') as unknown as Localization[];
    expect(localizations.map((manifest) => manifest.meta.culture)).toEqual(['en', 'de']);
    const [english, german] = await Promise.all(localizations.map(async (manifest) => (await manifest.js()).default));
    expect(english).toEqual((await import('./lang/en')).default);
    expect(german).toEqual((await import('./lang/de')).default);
    expect(german).not.toEqual(english);
  });
});

/** A source file of the server's form picker. */
function serverPickerSource(file: string): string {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) folder = dirname(folder);
  return readFileSync(join(folder, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Picker', file), 'utf8');
}

/** A constant of the server's form picker, read from its property editor. */
function serverPicker(constant: 'EditorAlias' | 'EditorUiAlias'): string | undefined {
  return new RegExp(`const string ${constant} = "([^"]+)"`).exec(
    serverPickerSource('FormPickerPropertyEditor.cs'),
  )?.[1];
}

interface Loadable {
  type: string;
  alias: string;
  meta?: Record<string, unknown>;
  element?: () => Promise<Record<string, unknown>>;
  api?: () => Promise<Record<string, unknown>>;
}

const byType = (type: string) => (manifests as unknown as Loadable[]).filter((manifest) => manifest.type === type);

describe('the form picker (ADR-0134)', () => {
  it("names its schema and UI as the server's property editor does, with the picker's own UI as the default", () => {
    const [schema] = byType('propertyEditorSchema');
    const [ui] = byType('propertyEditorUi');

    expect(serverPicker('EditorAlias')).toBe('Jiron.FormBuilder.FormPicker');
    expect(schema?.alias).toBe(serverPicker('EditorAlias'));
    expect(ui?.alias, 'the UI the server names for the data types it makes').toBe(serverPicker('EditorUiAlias'));
    expect(schema?.meta?.['defaultPropertyEditorUiAlias']).toBe(ui?.alias);
    expect(ui?.meta).toMatchObject({
      propertyEditorSchemaAlias: schema?.alias,
      group: 'pickers',
      supportsReadOnly: true,
    });
  });

  it("registers the sidebar under its token's alias and the block label's component as jfbFormName", () => {
    const [modal] = byType('modal');
    const [ufm] = byType('ufmComponent');

    expect(modal?.alias).toBe(FORM_PICKER_MODAL.toString());
    expect(ufm?.meta).toEqual({ alias: 'jfbFormName' });
  });

  it('loads each element and the component from its own module', { timeout: 60_000 }, async () => {
    const [ui] = byType('propertyEditorUi');
    const [modal] = byType('modal');
    const [ufm] = byType('ufmComponent');

    const load = (loader: (() => Promise<Record<string, unknown>>) | undefined, what: string) => must(loader, what)();
    expect(typeof (await load(ui?.element, "the picker's element"))['element']).toBe('function');
    expect(typeof (await load(modal?.element, "the sidebar's element"))['element']).toBe('function');
    expect(typeof (await load(ufm?.api, "the label's component"))['api']).toBe('function');
    expect(customElements.get('jfb-form-picker')).toBeDefined();
    expect(customElements.get('jfb-form-picker-modal')).toBeDefined();
    expect(customElements.get('ufm-jfb-form-name')).toBeDefined();
  });
});

interface Setting {
  alias: string;
  label: string;
  description?: string;
  propertyEditorUiAlias: string;
  config?: { alias: string; value: unknown }[];
}

describe("the form picker's settings (ADR-0135)", () => {
  const [schema] = byType('propertyEditorSchema');
  const settings = schema?.meta?.['settings'] as { properties: Setting[]; defaultData: unknown[] } | undefined;
  const setting = (alias: string) =>
    must(
      settings?.properties.find((candidate) => candidate.alias === alias),
      alias,
    );

  it("declares the server's settings on the schema, in the server's order, each with a label and help from the dictionaries", () => {
    const server = [
      ...serverPickerSource('FormPickerConfiguration.cs').matchAll(/\[ConfigurationField\("([^"]+)"\)\]/g),
    ].map((match) => match[1]);

    expect(server).toEqual(['multiple', 'allowedFolders', 'allowedForms', 'theme', 'redirect']);
    expect(settings?.properties.map((candidate) => candidate.alias)).toEqual(server);
    for (const candidate of settings?.properties ?? []) {
      expect(candidate.label, candidate.alias).toMatch(/^#jfbFormPicker_setting[A-Za-z]+$/);
      if (candidate.alias !== 'multiple') {
        expect(candidate.description, candidate.alias).toMatch(/^\{#jfbFormPicker_setting[A-Za-z]+Help\}$/);
      }
    }
  });

  it("takes one form by default, with Umbraco's toggle named as its label", () => {
    expect(setting('multiple').propertyEditorUiAlias).toBe('Umb.PropertyEditorUi.Toggle');
    expect(setting('multiple').config).toEqual([{ alias: 'ariaLabel', value: setting('multiple').label }]);
    expect(settings?.defaultData).toEqual([{ alias: 'multiple', value: false }]);
  });

  it("chooses the allowed folders and forms with the picker's own UI, set to each, and the theme with its own", () => {
    expect(setting('allowedFolders').propertyEditorUiAlias).toBe(FORM_PICKER_ALLOWED_UI_ALIAS);
    expect(setting('allowedFolders').config).toEqual([{ alias: 'pick', value: 'folders' }]);
    expect(setting('allowedForms').propertyEditorUiAlias).toBe(FORM_PICKER_ALLOWED_UI_ALIAS);
    expect(setting('allowedForms').config).toEqual([{ alias: 'pick', value: 'forms' }]);
    expect(setting('theme').propertyEditorUiAlias).toBe(FORM_PICKER_THEME_UI_ALIAS);
  });

  it("chooses the page after a send with Umbraco's document picker, one page at most", () => {
    expect(setting('redirect').propertyEditorUiAlias).toBe('Umb.PropertyEditorUi.DocumentPicker');
    expect(setting('redirect').config).toEqual([{ alias: 'validationLimit', value: { min: 0, max: 1 } }]);
  });

  it(
    'registers the settings UIs without a schema, so no data type offers them, each loading its element',
    { timeout: 60_000 },
    async () => {
      const uis = byType('propertyEditorUi').filter((ui) =>
        [FORM_PICKER_ALLOWED_UI_ALIAS, FORM_PICKER_THEME_UI_ALIAS].includes(ui.alias),
      );

      expect(uis).toHaveLength(2);
      for (const ui of uis) {
        expect(ui.meta?.['propertyEditorSchemaAlias'], ui.alias).toBeUndefined();
        expect(typeof (await must(ui.element, ui.alias)())['element']).toBe('function');
      }
      expect(customElements.get('jfb-form-picker-allowed')).toBeDefined();
      expect(customElements.get('jfb-form-picker-theme')).toBeDefined();
    },
  );
});
