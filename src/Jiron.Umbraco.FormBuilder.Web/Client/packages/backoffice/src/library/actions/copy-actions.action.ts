// "Copy actions to..." on a form (docs/ux/information-architecture.md, section 3; ADR-0046, ADR-0166): its sidebar picks the target
// and the actions, copies them, and lists what the target lacks, each with a link to fix it.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formGet } from '../../api';
import { COPY_ACTIONS_MODAL } from '../modals/tokens';

export class CopyActionsAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;
    await umbOpenModal(this, COPY_ACTIONS_MODAL, { data: { sourceKey: key, sourceName: form.name } });
  }
}

export { CopyActionsAction as api };
