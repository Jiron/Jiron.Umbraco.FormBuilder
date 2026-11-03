// "Duplicate..." on a form (docs/ux/information-architecture.md, section 3; ADR-0046, ADR-0166): the dialog asks for the copy's
// name and whether it takes the actions, and says for a restricted form that the copy takes its folder's access. The copy
// lands in the form's folder, and its screen opens.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderGet, formExchangeDuplicate, formGet, type FolderResponseModel } from '../../api';
import { screenPath, FORM_ENTITY_TYPE } from '../../section/entity-types';
import { myPermissions } from '../../section/my-permissions';
import type { MyPermissions } from '../../section/sidebar';
import { changed, open, placeOf, sayRefusal } from '../library-changes';
import { DUPLICATE_MODAL } from '../modals/tokens';

/**
 * Whether the copy may take the form's actions where it lands, as the server checks: manageActions in the form's folder,
 * or at the top level among the user's own permissions. A folder that did not read says no; the copy comes without them.
 */
export function mayTakeActions(
  folderKey: string | null | undefined,
  folder: Pick<FolderResponseModel, 'permissions'> | undefined,
  mine: Pick<MyPermissions, 'global'> | undefined,
): boolean {
  const held = folderKey ? folder?.permissions : mine?.global;
  return held?.includes('manageActions') ?? false;
}

export class DuplicateFormAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;
    const folder = form.folderKey
      ? (await tryExecute(this, folderGet({ path: { key: form.folderKey } }), { disableNotifications: true })).data
      : undefined;

    const mine = form.folderKey ? undefined : await myPermissions(this);

    const value = await umbOpenModal(this, DUPLICATE_MODAL, {
      data: {
        form,
        folderName: folder?.name ?? null,
        inFolder: Boolean(form.folderKey),
        mayTakeActions: mayTakeActions(form.folderKey, folder, mine),
      },
    });
    const { data: copy, error } = await tryExecute(
      this,
      formExchangeDuplicate({ path: { key }, body: { name: value.name, includeActions: value.includeActions } }),
      { disableNotifications: true },
    );
    if (!copy) {
      await sayRefusal(this, error);
      return;
    }

    await changed(this, placeOf(copy.folderKey));
    open(screenPath(FORM_ENTITY_TYPE, copy.key));
  }
}

export { DuplicateFormAction as api };
