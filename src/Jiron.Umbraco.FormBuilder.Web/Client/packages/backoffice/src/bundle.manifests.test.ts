import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import umbracoPackage from '../public/umbraco-package.json';
import { manifests } from './bundle.manifests';
import { FORM_PICKER_MODAL } from './picker/form-picker-modal.token';
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

/** The server's form picker alias, read from its property editor. */
function serverPickerAlias(): string | undefined {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) folder = dirname(folder);
  const source = readFileSync(
    join(folder, 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Picker', 'FormPickerPropertyEditor.cs'),
    'utf8',
  );
  return /EditorAlias = "([^"]+)"/.exec(source)?.[1];
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
  it("names its schema after the server's property editor, with the picker's own UI as the default", () => {
    const [schema] = byType('propertyEditorSchema');
    const [ui] = byType('propertyEditorUi');

    expect(serverPickerAlias()).toBe('Jiron.FormBuilder.FormPicker');
    expect(schema?.alias).toBe(serverPickerAlias());
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
