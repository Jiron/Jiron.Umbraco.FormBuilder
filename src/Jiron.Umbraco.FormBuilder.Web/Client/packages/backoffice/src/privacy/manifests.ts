// The privacy screens' extensions (ROADMAP 13.1, IA 9, ADR-0223): People's legal hold sidebar and its erase dialog. People
// and Retention themselves are their sidebar items' screens.
import { PEOPLE_ERASE_MODAL_ALIAS } from './people-erase.modal.token';
import { PEOPLE_HOLD_MODAL_ALIAS } from './people-hold.modal.token';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept for a
// manifest's element (as for the library's sidebars, ADR-0134).
type Element = UmbExtensionManifest extends { element?: infer E } ? E : never;

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'modal',
    alias: PEOPLE_HOLD_MODAL_ALIAS,
    name: 'Jiron Form Builder legal hold modal',
    element: (() => import('./people-hold.modal.element')) as unknown as Element,
  },
  {
    type: 'modal',
    alias: PEOPLE_ERASE_MODAL_ALIAS,
    name: 'Jiron Form Builder erase modal',
    element: (() => import('./people-erase.modal.element')) as unknown as Element,
  },
];
