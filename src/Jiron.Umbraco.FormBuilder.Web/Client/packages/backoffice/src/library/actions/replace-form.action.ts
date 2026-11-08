// "Replace from a file..." on a form (docs/ux/information-architecture.md, section 3; ADR-0220): the import sidebar, for this
// form, makes a JSON file's form its draft, and the form keeps its name, entries and access. In the form's own workspace with
// changes not saved yet, the sidebar says those are discarded. The form's screen opens and reads the new draft, also when it
// was open already.
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbEntityActionBase, UmbEntityUpdatedEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formGet } from '../../api';
import { FORM_WORKSPACE_CONTEXT } from '../../designer/constants';
import { FORM_ENTITY_TYPE, screenPath } from '../../section/entity-types';
import { say } from '../items/library-items';
import { open } from '../library-changes';
import { IMPORT_MODAL } from '../modals/tokens';
import { holdsUnsaved } from './save-as-template.action';

export class ReplaceFormAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;
    // The tree has no workspace around it, which the request learns within a frame.
    const unsaved = holdsUnsaved(await this.getContext(FORM_WORKSPACE_CONTEXT).catch(() => undefined), key);
    await umbOpenModal(this, IMPORT_MODAL, {
      data: {
        folderKey: form.folderKey ?? null,
        replace: { formKey: key, name: form.name, concurrencyVersion: form.concurrencyVersion, unsaved },
      },
    });

    // The form's workspace, wherever it is open, reads the new draft on this event.
    (await this.getContext(UMB_ACTION_EVENT_CONTEXT))?.dispatchEvent(
      new UmbEntityUpdatedEvent({ unique: key, entityType: FORM_ENTITY_TYPE }),
    );
    open(screenPath(FORM_ENTITY_TYPE, key));
    await say(this, 'positive', new UmbLocalizationController(this).term('jfbLibrary_replaced', form.name));
  }
}

export { ReplaceFormAction as api };
