// "Duplicate" in an email layout's "..." (ROADMAP 12.1, IA 7, ADR-0215): a copy under the first free alias, which opens.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { duplicateItem } from '../library/items/library-items';
import { EMAIL_LAYOUT_KIND } from './email-layout-kind';

export class DuplicateEmailLayoutAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await duplicateItem(this, EMAIL_LAYOUT_KIND, this.args.unique);
  }
}

export { DuplicateEmailLayoutAction as api };
