// "Move to..." on a form or a folder (docs/ux/information-architecture.md, section 3; ADR-0051, ADR-0166): Umbraco's tree picker
// with the folders only, the top of Forms included. A move that gives other user groups access asks first, saying how many;
// one the user may not widen says who can.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UMB_TREE_PICKER_MODAL, type UmbTreeItemModel } from '@umbraco-cms/backoffice/tree';
import { folderGet, folderMove, formGet, formMove } from '../../api';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE } from '../../section/entity-types';
import { LIBRARY_TREE_ALIAS } from '../../tree/aliases';
import { confirmText } from '../../ui/confirm';
import { changed, codeOf, placeOf, sayRefusal } from '../library-changes';

/** How many user groups a refusal says would gain access. */
const gainsOf = (error: unknown): number =>
  ((error as { problemDetails?: { gains?: unknown[] } } | undefined)?.problemDetails?.gains ?? []).length;

export class MoveAction extends UmbEntityActionBase<never> {
  readonly #localize = new UmbLocalizationController(this);

  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const isFolder = this.args.entityType === FOLDER_ENTITY_TYPE;
    const { data: item } = isFolder
      ? await tryExecute(this, folderGet({ path: { key } }))
      : await tryExecute(this, formGet({ path: { key } }));
    if (!item) return;
    const from = 'parentKey' in item ? item.parentKey : 'folderKey' in item ? item.folderKey : null;

    const picked = await umbOpenModal(this, UMB_TREE_PICKER_MODAL, {
      data: {
        treeAlias: LIBRARY_TREE_ALIAS,
        foldersOnly: true,
        hideTreeRoot: false,
        expandTreeRoot: true,
        // Not where it is already, and a folder not into itself; the server refuses a folder below it.
        pickableFilter: (target: UmbTreeItemModel) =>
          (target.entityType === FOLDER_ENTITY_TYPE || target.entityType === LIBRARY_ROOT_ENTITY_TYPE) &&
          target.unique !== key &&
          target.unique !== (from ?? null),
      },
    });
    const destination = picked.selection[0] ?? null;

    const move = (widenAccess: boolean) =>
      isFolder
        ? tryExecute(this, folderMove({ path: { key }, body: { parentKey: destination, widenAccess } }), {
            disableNotifications: true,
          })
        : tryExecute(
            this,
            formMove({
              path: { key },
              body: {
                concurrencyVersion: (item as { concurrencyVersion: number }).concurrencyVersion,
                folderKey: destination,
                widenAccess,
              },
            }),
            { disableNotifications: true },
          );

    let { error } = await move(false);
    if (codeOf(error) === 'widens_access') {
      await confirmText(this, {
        headline: this.#localize.term('jfbLibrary_widenHeadline', item.name),
        content: this.#localize.term('jfbLibrary_widenText', gainsOf(error)),
        confirmLabel: this.#localize.term('jfbLibrary_widenConfirm'),
      });
      ({ error } = await move(true));
    }

    if (error) {
      await sayRefusal(this, error);
      return;
    }

    await changed(this, placeOf(from), placeOf(destination));
  }
}

export { MoveAction as api };
