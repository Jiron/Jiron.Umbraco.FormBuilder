// "Trash" on a form (docs/ux/information-architecture.md, section 3; ADR-0166): it asks first only when a page picks the form, and
// then names the pages; otherwise it moves the form at once and offers Undo in the toast, which restores it.
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formGet, formReferenceList, formRestore, formTrash } from '../../api';
import { confirmText } from '../../ui/confirm';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import { placeOf, RECYCLE_BIN, refusalText, reloadChildren, sayRefusal } from '../library-changes';
import { NAMED_PAGES, usedOn } from '../rows';

export class TrashFormAction extends UmbEntityActionBase<never> {
  readonly #localize = new UmbLocalizationController(this);

  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;

    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    if (!form.permissions.includes('design')) {
      notifications?.peek('danger', { data: { message: this.#localize.term('jfbLibrary_mayNotChange', form.name) } });
      return;
    }

    const { data: references } = await tryExecute(
      this,
      formReferenceList({ path: { key }, query: { take: NAMED_PAGES } }),
      {
        disableNotifications: true,
      },
    );
    const pages = usedOn(references);
    if (pages.count > 0) {
      await confirmText(this, {
        headline: this.#localize.term('jfbLibrary_trashHeadline', form.name),
        content: this.#localize.term('jfbLibrary_trashInUse', pages.count, pages.names.join(', '), pages.more),
        confirmLabel: this.#localize.term('jfbLibrary_trashConfirm'),
        color: 'danger',
      });
    }

    const { data: trashed, error } = await tryExecute(
      this,
      formTrash({ path: { key }, body: { concurrencyVersion: form.concurrencyVersion } }),
      { disableNotifications: true },
    );
    if (!trashed) {
      await sayRefusal(this, error);
      return;
    }

    const events = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
    reloadChildren(events, placeOf(form.folderKey), RECYCLE_BIN);
    // Undo runs after Umbraco has let go of this action, so it uses what the action holds now, not its contexts.
    const data: UndoNotificationData = {
      message: this.#localize.term('jfbLibrary_trashed', form.name),
      undoLabel: this.#localize.term('jfbLibrary_undo'),
      undo: async () => {
        const { error: refused } = await tryExecute(
          this,
          formRestore({ path: { key }, body: { concurrencyVersion: trashed.concurrencyVersion, widenAccess: false } }),
          { disableNotifications: true },
        );
        if (refused) notifications?.peek('danger', { data: { message: refusalText(this.#localize, refused) } });
        reloadChildren(events, placeOf(form.folderKey), RECYCLE_BIN);
      },
    };
    notifications?.peek('positive', { elementName: UNDO_NOTIFICATION, duration: UNDO_DURATION_MS, data });
  }
}

export { TrashFormAction as api };
