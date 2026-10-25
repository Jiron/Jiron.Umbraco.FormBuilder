import { describe, expect, it } from 'vitest';
import umbracoPackage from '../public/umbraco-package.json';
import { manifests } from './bundle.manifests';

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
