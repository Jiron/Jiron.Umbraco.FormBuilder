// "Delete..." in an option list's "..." (ROADMAP 12.1, IA 7, ADR-0214): asked first, as a list goes for good. A list that
// forms use is not deleted: a dialog names the forms, whose fields must take their options from elsewhere first.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { deleteItem } from '../library/items/library-items';
import { OPTION_LIST_KIND } from './option-list-kind';

export class DeleteOptionListAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await deleteItem(this, OPTION_LIST_KIND, this.args.unique);
  }
}

export { DeleteOptionListAction as api };
