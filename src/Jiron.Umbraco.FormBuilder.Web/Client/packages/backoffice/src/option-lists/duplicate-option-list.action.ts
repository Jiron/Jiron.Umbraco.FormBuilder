// "Duplicate" in an option list's "..." (ROADMAP 12.1, IA 7, ADR-0214): a copy named after the list, which then opens.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { duplicateItem } from '../library/items/library-items';
import { OPTION_LIST_KIND } from './option-list-kind';

export class DuplicateOptionListAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await duplicateItem(this, OPTION_LIST_KIND, this.args.unique);
  }
}

export { DuplicateOptionListAction as api };
