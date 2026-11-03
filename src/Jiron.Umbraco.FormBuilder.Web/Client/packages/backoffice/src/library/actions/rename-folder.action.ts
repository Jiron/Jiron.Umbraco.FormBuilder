// "Rename..." on a folder in the tree (docs/ux/information-architecture.md, section 3); the folder's screen renames in its header.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { renameFolder } from './folder-actions';

export class RenameFolderAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await renameFolder(this, this.args.unique);
  }
}

export { RenameFolderAction as api };
