// What the Recycle Bin's menus do (docs/ux/information-architecture.md, section 3; docs/ux/copy.md, Confirmations; ADR-0166):
// Restore..., which says where the item goes and what comes back with it, and the two deletes for good, which say what goes,
// with numbers, and for a form the pages that still pick it.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  folderDelete,
  folderGet,
  folderRestore,
  formDelete,
  formGet,
  formHoldings,
  formReferenceList,
  formRestore,
  trashEmpty,
  trashHoldings,
} from '../../api';
import { confirmText } from '../../ui/confirm';
import { changed, codeOf, placeOf, RECYCLE_BIN, sayRefusal } from '../library-changes';
import { RESTORE_MODAL } from '../modals/tokens';
import { NAMED_PAGES, usedOn } from '../rows';

/** Takes a form or folder out of the Recycle Bin, once its sidebar says where it goes. */
export async function restore(host: UmbClassInterface, kind: 'form' | 'folder', key: string): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const item =
    kind === 'form'
      ? (await tryExecute(host, formGet({ path: { key } }))).data
      : (await tryExecute(host, folderGet({ path: { key } }))).data;
  if (!item) return false;
  await umbOpenModal(host, RESTORE_MODAL, { data: { kind, key, name: item.name } });

  const run = (widenAccess: boolean) =>
    kind === 'form'
      ? tryExecute(
          host,
          formRestore({
            path: { key },
            body: { concurrencyVersion: (item as { concurrencyVersion: number }).concurrencyVersion, widenAccess },
          }),
          { disableNotifications: true },
        )
      : tryExecute(host, folderRestore({ path: { key }, body: { widenAccess } }), { disableNotifications: true });
  let { error } = await run(false);
  if (codeOf(error) === 'widens_access') {
    await confirmText(host, {
      headline: localize.term('jfbLibrary_widenHeadline', item.name),
      content: localize.term('jfbLibrary_widenRestoreText'),
      confirmLabel: localize.term('jfbLibrary_widenConfirm'),
    });
    ({ error } = await run(true));
  }

  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  const from = 'folderKey' in item ? item.folderKey : 'parentKey' in item ? item.parentKey : null;
  await changed(host, RECYCLE_BIN, placeOf(from), placeOf(null));
  return true;
}

/** Deletes a form in the Recycle Bin for good, once asked with its entries, files and the pages that still pick it. */
export async function deleteForm(host: UmbClassInterface, key: string): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const { data: form } = await tryExecute(host, formGet({ path: { key } }));
  if (!form) return false;
  const [{ data: held }, { data: references }] = await Promise.all([
    tryExecute(host, formHoldings({ path: { key } }), { disableNotifications: true }),
    tryExecute(host, formReferenceList({ path: { key }, query: { take: NAMED_PAGES } }), {
      disableNotifications: true,
    }),
  ]);
  const pages = usedOn(references);
  const content = [
    localize.term('jfbLibrary_deleteFormText', held?.entries ?? 0, held?.files ?? 0),
    pages.count > 0 ? localize.term('jfbLibrary_deleteFormInUse', pages.count, pages.names.join(', '), pages.more) : '',
    localize.term('jfbLibrary_cannotUndo'),
  ].filter(Boolean);
  await confirmText(host, {
    headline: localize.term('jfbLibrary_deleteFormHeadline', form.name),
    content: content.join(' '),
    confirmLabel: localize.term('jfbLibrary_deleteFormConfirm', held?.entries ?? 0),
    color: 'danger',
  });
  const { error } = await tryExecute(
    host,
    formDelete({ path: { key }, query: { concurrencyVersion: form.concurrencyVersion } }),
    {
      disableNotifications: true,
    },
  );
  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  await changed(host, RECYCLE_BIN);
  return true;
}

/** Deletes a folder in the Recycle Bin for good, with everything that went there with it, once asked. */
export async function deleteTrashedFolder(host: UmbClassInterface, key: string): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const { data: folder } = await tryExecute(host, folderGet({ path: { key } }));
  if (!folder) return false;
  await confirmText(host, {
    headline: localize.term('jfbLibrary_deleteTrashedFolderHeadline', folder.name),
    content: `${localize.term('jfbLibrary_deleteTrashedFolderText')} ${localize.term('jfbLibrary_cannotUndo')}`,
    confirmLabel: localize.term('jfbLibrary_deleteTrashedFolderConfirm'),
    color: 'danger',
  });
  const { error } = await tryExecute(host, folderDelete({ path: { key } }), { disableNotifications: true });
  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  await changed(host, RECYCLE_BIN);
  return true;
}

/** Empties the Recycle Bin of what the user may delete, once asked with what goes; what stays is said too. */
export async function emptyBin(host: UmbClassInterface): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const { data: held } = await tryExecute(host, trashHoldings());
  if (!held) return false;
  if (held.forms + held.folders === 0) {
    (await host.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('warning', {
      data: { message: localize.term('jfbLibrary_emptyNothingYours') },
    });
    return false;
  }

  const content = [
    localize.term('jfbLibrary_emptyText', held.forms, held.entries, held.files),
    held.left > 0 ? localize.term('jfbLibrary_emptyLeft', held.left) : '',
    localize.term('jfbLibrary_cannotUndo'),
  ].filter(Boolean);
  await confirmText(host, {
    headline: localize.term('jfbLibrary_emptyHeadline'),
    content: content.join(' '),
    confirmLabel: localize.term('jfbLibrary_emptyConfirm'),
    color: 'danger',
  });
  const { error } = await tryExecute(host, trashEmpty(), { disableNotifications: true });
  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  await changed(host, RECYCLE_BIN);
  return true;
}
