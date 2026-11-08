// The activity screens' extensions (ROADMAP 12.4b, IA 9, ADR-0219): a delivery's details and an action's details, each a
// sidebar over its list. Deliveries and the audit log themselves are their sidebar items' screens.
import { AUDIT_ENTRY_MODAL_ALIAS } from './audit-entry.modal.token';
import { DELIVERY_MODAL_ALIAS } from './delivery.modal.token';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept for a
// manifest's element (as for the library's sidebars, ADR-0134).
type Element = UmbExtensionManifest extends { element?: infer E } ? E : never;

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'modal',
    alias: DELIVERY_MODAL_ALIAS,
    name: 'Jiron Form Builder delivery modal',
    element: (() => import('./delivery.modal.element')) as unknown as Element,
  },
  {
    type: 'modal',
    alias: AUDIT_ENTRY_MODAL_ALIAS,
    name: 'Jiron Form Builder audit entry modal',
    element: (() => import('./audit-entry.modal.element')) as unknown as Element,
  },
];
