// "Duplicate" in an option list's "..." (ROADMAP 12.1, IA 7, ADR-0214): a copy named after the list, which then opens.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { open, sayRefusal } from '../library/library-changes';
import { listPath } from './constants';
import { say, term, wordsFor } from './option-list-actions';
import { duplicateList, readList } from './option-lists-api';

export class DuplicateOptionListAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const localize = await wordsFor(this);
    const { data: list, error } = await readList(this, key);
    if (!list) {
      await sayRefusal(this, error);
      return;
    }
    const copy = await duplicateList(this, key, term(localize, 'copyName', list.name));
    if (!copy) {
      await say(this, 'danger', term(localize, 'duplicateFailed'));
      return;
    }
    await say(this, 'positive', term(localize, 'duplicated', copy.name));
    open(listPath(copy.unique));
  }
}

export { DuplicateOptionListAction as api };
