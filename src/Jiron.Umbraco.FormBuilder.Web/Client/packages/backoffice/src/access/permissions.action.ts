// "Permissions..." on a folder and a form (ROADMAP 12.4a, IA 8, ADR-0218): opens its Permissions sidebar; once it saved,
// the tree shows the place it is in again, so a restriction's lock shows on it at once.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderGet, formGet } from '../api';
import { changed, placeOf } from '../library/library-changes';
import { FOLDER_ENTITY_TYPE } from '../section/entity-types';
import { PERMISSIONS_MODAL } from './permissions.modal.token';

export class PermissionsAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const isFolder = this.args.entityType === FOLDER_ENTITY_TYPE;
    const value = await umbOpenModal(this, PERMISSIONS_MODAL, {
      data: { target: isFolder ? 'folder' : 'form', key },
    }).catch(() => undefined);
    if (!value?.saved) return;
    const parent = isFolder
      ? (await tryExecute(this, folderGet({ path: { key } }), { disableNotifications: true })).data?.parentKey
      : (await tryExecute(this, formGet({ path: { key } }), { disableNotifications: true })).data?.folderKey;
    await changed(this, placeOf(parent));
  }
}

export { PermissionsAction as api };
