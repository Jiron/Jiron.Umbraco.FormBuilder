// The create dialog of option lists (ROADMAP 12.1, IA 7, ADR-0214), as a token the list screen opens without loading the
// element.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { NEW_OPTION_LIST_MODAL_ALIAS } from './constants';
import type { ListKind } from './option-list-model';

/** Nothing: the dialog offers every kind. */
export type NewOptionListModalData = Record<string, never>;

/** The kind chosen. */
export interface NewOptionListModalValue {
  readonly kind: ListKind;
}

export const NEW_OPTION_LIST_MODAL = new UmbModalToken<NewOptionListModalData, NewOptionListModalValue>(
  NEW_OPTION_LIST_MODAL_ALIAS,
  { modal: { type: 'dialog', size: 'small' } },
);
