import { manifests as members } from './members/manifests';
import { manifests as picker } from './picker/manifests';
import { manifests as section } from './section/manifests';

/**
 * Every extension of the Forms section (ADR-0021), registered through the one bundle that umbraco-package.json names.
 * Umbraco's backoffice provides Lit and its own modules at run time, so the bundle imports them and never ships them.
 * The section (ADR-0021, ADR-0165) with its Overview and forms tree; the texts, one dictionary per language, which a regional
 * culture such as de-CH falls back to (ADR-0057); the brand's icon (ROADMAP 17.1, assets/brand.md); the form picker of
 * the content editor (ADR-0134); and the member editor's Form entries view (ADR-0231).
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
  {
    type: 'icons',
    alias: 'Jiron.FormBuilder.Icons',
    name: 'Jiron Form Builder icons',
    js: () => import('./icons/icons'),
  },
  ...section,
  ...picker,
  ...members,
];
