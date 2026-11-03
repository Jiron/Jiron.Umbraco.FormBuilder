// "Trash" on a form in the tree (ADR-0165): it reads the form for its name and version, asks with the name as text, and moves it to
// the Recycle Bin. Step 9.2 adds the pages that use it to the question.
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbEntityActionBase, UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formGet, formTrash } from '../api';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE } from '../section/entity-types';
import { confirmText } from '../ui/confirm';

export class TrashFormAction extends UmbEntityActionBase<never> {
  readonly #localize = new UmbLocalizationController(this);

  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;

    if (!form.permissions.includes('design')) {
      (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('danger', {
        data: { message: this.#localize.term('jfbSection_trashNotAllowed', form.name) },
      });
      return;
    }

    await confirmText(this, {
      headline: this.#localize.term('jfbSection_trashHeadline', form.name),
      content: this.#localize.term('jfbSection_trashText', form.name),
      confirmLabel: this.#localize.term('jfbSection_trashConfirm'),
      color: 'danger',
    });

    const { error } = await tryExecute(
      this,
      formTrash({ path: { key }, body: { concurrencyVersion: form.concurrencyVersion } }),
    );
    if (error) return;

    const parent = form.folderKey
      ? { unique: form.folderKey, entityType: FOLDER_ENTITY_TYPE }
      : { unique: null, entityType: LIBRARY_ROOT_ENTITY_TYPE };
    (await this.getContext(UMB_ACTION_EVENT_CONTEXT))?.dispatchEvent(new UmbRequestReloadChildrenOfEntityEvent(parent));
  }
}

export { TrashFormAction as api };
