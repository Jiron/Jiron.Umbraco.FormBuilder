// "Restore..." on a form or folder in the Recycle Bin (docs/ux/information-architecture.md, section 3; ADR-0166).
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { TRASHED_FOLDER_ENTITY_TYPE } from '../../section/entity-types';
import { restore } from './trash-actions';

export class RestoreAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique)
      await restore(this, this.args.entityType === TRASHED_FOLDER_ENTITY_TYPE ? 'folder' : 'form', this.args.unique);
  }
}

export { RestoreAction as api };
