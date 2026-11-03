// "Empty recycle bin..." on the Recycle Bin (docs/ux/information-architecture.md, section 3; docs/ux/copy.md).
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { emptyBin } from './trash-actions';

export class EmptyBinAction extends UmbEntityActionBase<never> {
  override async execute() {
    await emptyBin(this);
  }
}

export { EmptyBinAction as api };
