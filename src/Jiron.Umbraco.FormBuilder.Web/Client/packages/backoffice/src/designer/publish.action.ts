// "Publish..." in the form workspace's footer (docs/ux/information-architecture.md, section 5.7): the publish dialog's
// flow (publish-flow.ts), with the button waiting while the form is published and showing a refusal.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbWorkspaceActionBase, type UmbWorkspaceActionArgs } from '@umbraco-cms/backoffice/workspace';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { publishWithDialog } from './publish-flow';

export class PublishFormAction extends UmbWorkspaceActionBase {
  constructor(host: UmbControllerHost, args: UmbWorkspaceActionArgs<never>) {
    super(host, args);
    // The button waits only once the dialog is confirmed, so a cancelled dialog leaves it as it was.
    this.setExecuting(false);
  }

  override async execute() {
    const context = await this.getContext(FORM_WORKSPACE_CONTEXT);
    if (!context) return;
    // The refusal is said already; the button shows that it failed.
    if ((await publishWithDialog(this, context, (busy) => this.setExecuting(busy))) === 'refused')
      throw new Error('The form was not published.');
  }
}

export { PublishFormAction as api };
