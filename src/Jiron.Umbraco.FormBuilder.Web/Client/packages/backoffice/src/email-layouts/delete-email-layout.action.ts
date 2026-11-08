// "Delete..." in an email layout's "..." (ROADMAP 12.1, IA 7, ADR-0215): asked first. A layout that email actions use is
// not deleted, and neither is the site's default.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { deleteItem } from '../library/items/library-items';
import { EMAIL_LAYOUT_KIND } from './email-layout-kind';

export class DeleteEmailLayoutAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await deleteItem(this, EMAIL_LAYOUT_KIND, this.args.unique);
  }
}

export { DeleteEmailLayoutAction as api };
