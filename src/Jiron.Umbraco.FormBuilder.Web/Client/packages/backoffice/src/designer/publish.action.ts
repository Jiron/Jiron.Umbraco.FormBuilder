// "Publish..." in the form workspace's footer (docs/ux/information-architecture.md, section 5.7): the dialog asks first,
// with the form check's result and an optional comment for the version, then the workspace saves what is unsaved and
// publishes. A Fix it chosen in the dialog leads to the Design view, which makes it (ADR-0181). What changed joins the
// dialog with its step.
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbWorkspaceActionBase, type UmbWorkspaceActionArgs } from '@umbraco-cms/backoffice/workspace';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { goToView } from './form-views';
import { PUBLISH_MODAL } from './publish.modal.token';

export class PublishFormAction extends UmbWorkspaceActionBase {
  constructor(host: UmbControllerHost, args: UmbWorkspaceActionArgs<never>) {
    super(host, args);
    // The button waits only once the dialog is confirmed, so a cancelled dialog leaves it as it was.
    this.setExecuting(false);
  }

  override async execute() {
    const context = await this.getContext(FORM_WORKSPACE_CONTEXT);
    const form = context?.getData()?.form;
    if (!context || !form) return;
    // The draft as it is now is checked as the dialog opens, which shows the answer once it comes.
    void context.checkNow();
    let gone: (() => void) | undefined;
    const closed = new Promise<void>((done) => (gone = done));
    const value = await umbOpenModal(this, PUBLISH_MODAL, {
      data: {
        name: context.getName() ?? form.name,
        check: context.check,
        types: context.getFieldTypes(),
        // The site's default texts name the elements, as everywhere outside the designer's language switch.
        culture: '',
        closed: () => gone?.(),
      },
    }).catch(() => undefined);
    if (!value) return;
    if ('fix' in value) {
      // A view is shown at once; the Design view makes any other Fix it. The dialog answers before it closes and gives
      // the focus back to Publish... as it does, so the Fix it waits.
      if (value.fix.kind === 'view') {
        goToView(value.fix.view);
        return;
      }
      await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
      context.requestFix(value.fix);
      goToView('design');
      return;
    }
    this.setExecuting(true);
    try {
      // The refusal is said already; the button shows that it failed.
      if (!(await context.publish(value.comment.trim() || null))) throw new Error('The form was not published.');
    } finally {
      this.setExecuting(false);
    }
  }
}

export { PublishFormAction as api };
