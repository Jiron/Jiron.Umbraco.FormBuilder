// The dialogs of a form's Entries view (ROADMAP 11.1, ADR-0207); the view itself is one of the form workspace's views
// (designer/manifests.ts).
import {
  ENTRY_COLUMNS_MODAL_ALIAS,
  ENTRY_FILTER_MODAL_ALIAS,
  ENTRY_KEYS_MODAL_ALIAS,
  ENTRY_SET_NAME_MODAL_ALIAS,
} from './entry-modals';

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'modal',
    alias: ENTRY_FILTER_MODAL_ALIAS,
    name: 'Jiron Form Builder entries filter',
    element: (() => import('./entry-filter.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: ENTRY_COLUMNS_MODAL_ALIAS,
    name: 'Jiron Form Builder entries columns',
    element: (() => import('./entry-columns.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: ENTRY_SET_NAME_MODAL_ALIAS,
    name: 'Jiron Form Builder filter set name',
    element: (() => import('./entry-set-name.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: ENTRY_KEYS_MODAL_ALIAS,
    name: 'Jiron Form Builder entries keyboard shortcuts',
    element: (() => import('./entry-keys.modal.element')) as never,
  },
];
