// "Restore an earlier version..." (ROADMAP 10.3b, IA 5.6, ADR-0198): first in the form's "..." in the workspace, from every
// view. It opens the sidebar of earlier versions once their words are loaded; choosing one compares it with the current
// draft, which can restore it, as History's own comparison does (version-actions.ts). A restore closes both sidebars, and
// Restore and publish... then opens the publish dialog.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { ComparisonModalValue } from './comparison.modal.token';
import { loadHistoryWords } from './history-words';
import { VersionActions } from './version-actions';
import { VERSION_PICKER_MODAL } from './version-picker.modal.token';

export class RestoreEarlierAction extends UmbEntityActionBase<never> {
  override async execute() {
    const context = await this.getContext(FORM_WORKSPACE_CONTEXT);
    if (!context) return;
    const localize = new UmbLocalizationController(this);
    // Without its words the sidebar would show keys: a load that fails is said in the start words, which are there.
    if (
      !(await loadHistoryWords(localize.lang()).then(
        () => true,
        () => false,
      ))
    ) {
      (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('danger', {
        data: { message: localize.term('jfbDesigner_historyFailed') },
      });
      return;
    }
    const actions = new VersionActions(this, context);
    let restored: ComparisonModalValue['restore'] | undefined;
    await umbOpenModal(this, VERSION_PICKER_MODAL, {
      data: {
        read: (includeDrafts, skip) => context.versionsOf(includeDrafts, skip),
        choose: async (entry) => {
          restored = await actions.offer(entry);
          return restored !== undefined;
        },
      },
    }).catch(() => undefined);
    // The publish dialog opens once the sidebar of earlier versions is closed, so nothing stale stays under it.
    if (restored === 'publish') await actions.publish();
  }
}

export { RestoreEarlierAction as api };
