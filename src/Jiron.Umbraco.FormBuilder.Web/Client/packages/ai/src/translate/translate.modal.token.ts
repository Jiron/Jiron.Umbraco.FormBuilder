// The sidebar of "Translate with AI" (ADR-0018).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { JfbFormDesigner } from '../../../backoffice/public/types/jiron-form-builder';

export const TRANSLATE_MODAL_ALIAS = 'Jiron.FormBuilder.AI.Modal.Translate';

export interface TranslateModalData {
  /** The open form's designer, which reads the language shown and applies the translation. */
  readonly designer: JfbFormDesigner;
}

export const TRANSLATE_MODAL = new UmbModalToken<TranslateModalData, never>(TRANSLATE_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'large' },
});
