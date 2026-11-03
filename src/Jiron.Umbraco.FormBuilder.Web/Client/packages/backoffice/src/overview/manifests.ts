// The Overview (docs/ux/information-architecture.md, section 2): the section's one dashboard, so Umbraco shows it with no tab bar.
import { SECTION_ALIAS } from '../section/entity-types';
import { FORM_BLOCK_MODAL_ALIAS } from './form-block-modal.element';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept for a
// manifest's element (as for the picker's sidebar, ADR-0134).
const dialog = (() => import('./form-block-modal.element')) as unknown as UmbExtensionManifest extends {
  element?: infer E;
}
  ? E
  : never;

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'dashboard',
    alias: 'Jiron.FormBuilder.Dashboard.Overview',
    name: 'Jiron Form Builder Overview',
    element: () => import('./overview.element'),
    meta: { label: '#jfbSection_overview', pathname: 'overview' },
    conditions: [{ alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS }],
  },
  { type: 'modal', alias: FORM_BLOCK_MODAL_ALIAS, name: 'Jiron Form Builder form block dialog', element: dialog },
];
