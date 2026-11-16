// The Change type dialog's token (ROADMAP 9.4d, ADR-0174).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const CHANGE_TYPE_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.ChangeType';

/** What a change does to the field, in the backoffice's language, each part in the order the dialog shows it. */
export interface ChangeTypeWords {
  /** What goes: values of settings, properties the field uses, its own messages. */
  readonly goes: readonly string[];
  /** The settings a lock of the new type sets instead, each with the lock's value where the site shows it. */
  readonly locked: readonly string[];
  /** What it needs before it publishes: options, settings, and the conditions to change. */
  readonly needs: readonly string[];
  /** What the entries screen does with older answers, where the form has entries and the columns differ. */
  readonly entries: string | null;
}

/** A type the field may become. */
export interface ChangeTypeTarget {
  readonly alias: string;
  readonly name: string;
  /** The name of the group the type is listed under, which the list groups it under too. */
  readonly group: string;
  /** Why it can't be chosen, as the form or the site refuses it; null where it can. */
  readonly refusal: string | null;
  readonly words: ChangeTypeWords;
}

/** The field's name and type, and the types it may become in the server's order. */
export interface ChangeTypeModalData {
  readonly name: string;
  readonly type: string;
  readonly targets: readonly ChangeTypeTarget[];
}

/** The type chosen. */
export interface ChangeTypeModalValue {
  to: string;
}

export const CHANGE_TYPE_MODAL = new UmbModalToken<ChangeTypeModalData, ChangeTypeModalValue>(CHANGE_TYPE_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
