// "Delete..." on an empty folder (docs/ux/information-architecture.md, section 3; ADR-0166): asked once, then gone for good.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { deleteFolder } from './folder-actions';

export class DeleteFolderAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await deleteFolder(this, this.args.unique);
  }
}

export { DeleteFolderAction as api };
