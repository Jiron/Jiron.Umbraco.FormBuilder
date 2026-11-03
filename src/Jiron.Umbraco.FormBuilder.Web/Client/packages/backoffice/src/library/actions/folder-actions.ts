// What a folder's "..." does besides moving (docs/ux/information-architecture.md, section 3; ADR-0166): Rename..., which the
// folder's own screen does in its header instead, and Delete..., only for an empty folder, which goes at once and for good.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderCreate, folderDelete, folderGet, folderRename } from '../../api';
import { confirmText } from '../../ui/confirm';
import { changed, leaveGone, placeOf, sayRefusal } from '../library-changes';
import { FOLDER_NAME_MODAL } from '../modals/tokens';

/** Asks for a new folder's name and creates it in a folder, or at the top of Forms. */
export async function createFolder(host: UmbClassInterface, parentKey: string | null): Promise<void> {
  const localize = new UmbLocalizationController(host);
  const { name } = await umbOpenModal(host, FOLDER_NAME_MODAL, {
    data: { headline: localize.term('jfbLibrary_newFolder'), confirmLabel: localize.term('jfbLibrary_createFolder') },
  });
  const { error } = await tryExecute(host, folderCreate({ body: { name, parentKey } }), { disableNotifications: true });
  if (error) {
    await sayRefusal(host, error);
    return;
  }

  await changed(host, placeOf(parentKey));
}

/** Renames a folder: the dialog of the tree's "...", or the folder screen's header, which passes the name. */
export async function renameFolder(host: UmbClassInterface, key: string, typed?: string): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const { data: folder } = await tryExecute(host, folderGet({ path: { key } }));
  if (!folder) return false;
  const name =
    typed ??
    (
      await umbOpenModal(host, FOLDER_NAME_MODAL, {
        data: {
          headline: localize.term('jfbLibrary_renameFolder', folder.name),
          confirmLabel: localize.term('jfbLibrary_rename'),
          name: folder.name,
        },
      })
    ).name;
  if (name === folder.name) return true;
  const { error } = await tryExecute(host, folderRename({ path: { key }, body: { name } }), {
    disableNotifications: true,
  });
  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  await changed(host, placeOf(folder.parentKey), placeOf(key));
  return true;
}

/** Deletes an empty folder at once and for good, once asked; one that holds anything is refused, with what to do. */
export async function deleteFolder(host: UmbClassInterface, key: string): Promise<boolean> {
  const localize = new UmbLocalizationController(host);
  const { data: folder } = await tryExecute(host, folderGet({ path: { key } }));
  if (!folder) return false;
  await confirmText(host, {
    headline: localize.term('jfbLibrary_deleteFolderHeadline', folder.name),
    content: localize.term('jfbLibrary_deleteFolderText'),
    confirmLabel: localize.term('jfbLibrary_deleteFolderConfirm'),
    color: 'danger',
  });
  const { error } = await tryExecute(host, folderDelete({ path: { key } }), { disableNotifications: true });
  if (error) {
    await sayRefusal(host, error);
    return false;
  }

  await changed(host, placeOf(folder.parentKey));
  leaveGone(placeOf(key), placeOf(folder.parentKey));
  return true;
}
