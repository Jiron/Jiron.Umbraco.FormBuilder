// The create menu's "Draft a form with AI" (docs/ux/information-architecture.md, sections 3 and 4; ADR-0018): its
// sidebar drafts the form in the folder the "+" was on, or at the top of the library; then the place's list is read
// again and the new form opens in the designer, as for the core's other ways to a new form.
import { UmbEntityCreateOptionActionBase } from '@umbraco-cms/backoffice/entity-create-option-action';
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { FOLDER, formPath, LIBRARY_ROOT } from '../names';
import { DRAFT_MODAL } from './draft.modal.token';

export class DraftWithAIOption extends UmbEntityCreateOptionActionBase {
  override async execute() {
    const folderKey = this.args.unique ?? null;
    const { formKey } = await umbOpenModal(this, DRAFT_MODAL, { data: { folderKey } });
    const events = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
    events?.dispatchEvent(
      new UmbRequestReloadChildrenOfEntityEvent({ entityType: folderKey ? FOLDER : LIBRARY_ROOT, unique: folderKey }),
    );
    history.pushState(null, '', new URL(formPath(formKey), document.baseURI).href);
  }
}

export { DraftWithAIOption as api };
