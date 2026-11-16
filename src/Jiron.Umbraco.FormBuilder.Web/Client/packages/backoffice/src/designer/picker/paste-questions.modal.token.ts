// The Paste questions dialog's token (ROADMAP 9.3b).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { PastedQuestion } from '../new-elements';

export const PASTE_QUESTIONS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.PasteQuestions';

/** The field types a question may become: their aliases with the names the dialog shows. */
export interface PasteQuestionsModalData {
  types: Readonly<Record<string, string>>;
}

/** The questions to add, each with its guessed type. */
export interface PasteQuestionsModalValue {
  questions: PastedQuestion[];
}

export const PASTE_QUESTIONS_MODAL = new UmbModalToken<PasteQuestionsModalData, PasteQuestionsModalValue>(
  PASTE_QUESTIONS_MODAL_ALIAS,
  { modal: { type: 'dialog', size: 'medium' } },
);
