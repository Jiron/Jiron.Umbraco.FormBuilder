// The Move to... dialog's token (ROADMAP 9.3c, ADR-0169).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const MOVE_TO_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.MoveTo';

/** A place the element can go to the end of, as the dialog shows it. */
export interface MoveToPlace {
  readonly key: string;
  readonly name: string;
  /** Why the form can't take it there; null where it can, or where it is at that end already. */
  readonly refusal: string | null;
  /** Whether it can't be chosen: the form refuses it, or the element is at its end already. */
  readonly disabled: boolean;
  /** Whether it lies inside the place before it: a repeatable group in its group. */
  readonly inside: boolean;
}

/** A page's places under its name, or the pages themselves without a heading. */
export interface MoveToSection {
  readonly heading: string | null;
  readonly places: readonly MoveToPlace[];
}

/** The element's name and the places it can go. */
export interface MoveToModalData {
  readonly name: string;
  readonly sections: readonly MoveToSection[];
}

/** The place chosen. */
export interface MoveToModalValue {
  key: string;
}

export const MOVE_TO_MODAL = new UmbModalToken<MoveToModalData, MoveToModalValue>(MOVE_TO_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
