// "Delete..." in an option list's "..." (ROADMAP 12.1, IA 7, ADR-0214): asked first, as a list goes for good. A list that
// forms use is not deleted: a dialog names the forms, whose fields must take their options from elsewhere first. The
// list's workspace goes back to the list screen once the list is gone.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { leaveGone, sayRefusal } from '../library/library-changes';
import { confirmText } from '../ui/confirm';
import { OPTION_LIST_ENTITY_TYPE, OPTION_LISTS_ENTITY_TYPE } from './constants';
import { OptionListDetailRepository } from './option-list-detail.repository';
import { say, sayInUse, term, wordsFor } from './option-list-actions';
import { readList, type DeleteAnswer } from './option-lists-api';

export class DeleteOptionListAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const localize = await wordsFor(this);
    const { data: list, error } = await readList(this, key);
    if (!list) {
      await sayRefusal(this, error);
      return;
    }
    if (await sayInUse(this, key, list.name)) return;
    await confirmText(this, {
      headline: term(localize, 'deleteHeadline', list.name),
      content: term(localize, 'deleteText'),
      confirmLabel: term(localize, 'deleteConfirm'),
      color: 'danger',
    });
    // Through the workspace's repository, so its store lets the open list go and leaving it asks nothing.
    const repository = new OptionListDetailRepository(this);
    const { error: refused } = await repository.delete(key).finally(() => repository.destroy());
    if (refused) {
      const answer = refused.message as DeleteAnswer;
      // A form took the list up since the dialog was read: the forms are named now.
      if (answer === 'inUse' && (await sayInUse(this, key, list.name))) return;
      // Someone saved the list since it was read; anything else is a failure to try again.
      await say(
        this,
        'danger',
        answer === 'conflict' ? term(localize, 'changedMeanwhile') : term(localize, 'deleteFailed'),
      );
      return;
    }
    await say(this, 'positive', term(localize, 'deleted', list.name));
    leaveGone(
      { entityType: OPTION_LIST_ENTITY_TYPE, unique: key },
      { entityType: OPTION_LISTS_ENTITY_TYPE, unique: null },
    );
  }
}

export { DeleteOptionListAction as api };
