// The erase dialog of Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): its token, what it is opened with and
// what it answers.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { PersonErasedResponseModel, PersonErasePreviewResponseModel } from '../api';
import type { Person } from './people-api';

export const PEOPLE_ERASE_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.PeopleErase';

/** The person, as the screen names them, and the erase's preview. */
export interface PeopleEraseModalData {
  readonly person: Person;
  readonly who: string;
  readonly preview: PersonErasePreviewResponseModel;
}

/** What the erase took. */
export interface PeopleEraseModalValue {
  readonly erased: PersonErasedResponseModel;
}

export const PEOPLE_ERASE_MODAL = new UmbModalToken<PeopleEraseModalData, PeopleEraseModalValue>(
  PEOPLE_ERASE_MODAL_ALIAS,
  {
    modal: { type: 'dialog', size: 'small' },
  },
);
