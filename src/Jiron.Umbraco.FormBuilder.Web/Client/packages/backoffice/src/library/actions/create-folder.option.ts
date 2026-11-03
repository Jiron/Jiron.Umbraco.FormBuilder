// The create dialog's "Folder..." (docs/ux/information-architecture.md, section 4; ADR-0166).
import { UmbEntityCreateOptionActionBase } from '@umbraco-cms/backoffice/entity-create-option-action';
import { createFolder } from './folder-actions';

export class CreateFolderOption extends UmbEntityCreateOptionActionBase {
  override async execute() {
    await createFolder(this, this.args.unique ?? null);
  }
}

export { CreateFolderOption as api };
