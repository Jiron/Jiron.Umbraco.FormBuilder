// "Publish..." in the form workspace's footer (docs/ux/information-architecture.md, section 5.7): the dialog asks first,
// with an optional comment for the version, then the workspace saves what is unsaved and publishes. What changed and the
// form check join the dialog with their steps.
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbWorkspaceActionBase, type UmbWorkspaceActionArgs } from '@umbraco-cms/backoffice/workspace';
import { FORM_WORKSPACE_CONTEXT } from './constants';
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
    const value = await umbOpenModal(this, PUBLISH_MODAL, { data: { name: context.getName() ?? form.name } }).catch(
      () => undefined,
    );
    if (!value) return;
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
