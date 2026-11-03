// "Delete..." on a form or folder in the Recycle Bin (docs/ux/information-architecture.md, section 3; docs/ux/copy.md): for good,
// once asked with what goes.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { TRASHED_FOLDER_ENTITY_TYPE } from '../../section/entity-types';
import { deleteForm, deleteTrashedFolder } from './trash-actions';

export class DeleteTrashedAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    await (this.args.entityType === TRASHED_FOLDER_ENTITY_TYPE
      ? deleteTrashedFolder(this, key)
      : deleteForm(this, key));
  }
}

export { DeleteTrashedAction as api };
