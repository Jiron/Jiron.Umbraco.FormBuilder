// The legal hold sidebar of Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0222, ADR-0223): its token, what it is
// opened with and what it answers.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { PersonEntryResponseModel } from '../api';
import type { Person } from './people-api';

export const PEOPLE_HOLD_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.PeopleHold';

/** The person and their entries a hold can keep: those sent, not drafts. */
export interface PeopleHoldModalData {
  readonly person: Person;
  readonly entries: readonly PersonEntryResponseModel[];
}

/** The entries the hold keeps now, and until when. */
export interface PeopleHoldModalValue {
  readonly held: number;
  readonly keys: readonly string[];
  readonly until: string;
}

export const PEOPLE_HOLD_MODAL = new UmbModalToken<PeopleHoldModalData, PeopleHoldModalValue>(PEOPLE_HOLD_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
