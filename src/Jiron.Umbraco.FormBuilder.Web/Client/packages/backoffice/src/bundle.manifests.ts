/**
 * Every extension of the Forms section (ADR-0021), registered through the one bundle that umbraco-package.json names.
 * Umbraco's backoffice provides Lit and its own modules at run time, so the bundle imports them and never ships them.
 * The section arrives in 9.1; the texts are here from 5.1c (ADR-0057), one dictionary per language, which a regional
 * culture such as de-CH falls back to.
 */
export const manifests: UmbExtensionManifest[] = [
  {
    type: 'localization',
    alias: 'Jiron.FormBuilder.Localization.En',
    name: 'Jiron Form Builder English',
    meta: { culture: 'en' },
    js: () => import('./lang/en'),
  },
  {
    type: 'localization',
    alias: 'Jiron.FormBuilder.Localization.De',
    name: 'Jiron Form Builder German',
    meta: { culture: 'de' },
    js: () => import('./lang/de'),
  },
];
