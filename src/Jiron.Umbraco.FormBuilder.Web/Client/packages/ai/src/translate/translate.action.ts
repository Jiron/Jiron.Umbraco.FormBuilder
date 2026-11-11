// "Translate with AI..." in the designer toolbar's "..." (docs/ux/information-architecture.md, section 5; ADR-0018): it
// opens the sidebar, which says first whether the AI can translate, and saves what is not saved yet only when the editor
// asks for the translation, as it reads the saved draft; then it previews the translation and applies it to the open
// draft.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { JfbDesignerActionApi, JfbFormDesigner } from '../../../backoffice/public/types/jiron-form-builder';
import { TRANSLATE_MODAL } from './translate.modal.token';

export class TranslateWithAIAction extends UmbControllerBase implements JfbDesignerActionApi {
  async execute(designer: JfbFormDesigner) {
    await umbOpenModal(this, TRANSLATE_MODAL, { data: { designer } }).catch(() => undefined);
  }
}

export { TranslateWithAIAction as api };
