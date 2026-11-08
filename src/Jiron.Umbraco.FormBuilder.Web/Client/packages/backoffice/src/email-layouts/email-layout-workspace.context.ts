// The email layout workspace (ROADMAP 12.1, IA 7, ADR-0215): the library's workspace over the layout endpoints; its view
// edits the design, and Send test email saves it first, so the test mail is what forms send.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { LibraryItemWorkspaceContext } from '../library/items/library-item-workspace';
import { EMAIL_LAYOUT_DETAIL_REPOSITORY_ALIAS, EMAIL_LAYOUT_WORKSPACE_ALIAS } from './constants';
import { EMAIL_LAYOUT_KIND } from './email-layout-kind';
import type { EmailLayoutDesign, EmailLayoutDetailModel } from './email-layout-model';

export class EmailLayoutWorkspaceContext extends LibraryItemWorkspaceContext<EmailLayoutDetailModel> {
  readonly design = this._data.createObservablePartOfCurrent((data) => data?.design);

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: EMAIL_LAYOUT_WORKSPACE_ALIAS,
      detailRepositoryAlias: EMAIL_LAYOUT_DETAIL_REPOSITORY_ALIAS,
      kind: EMAIL_LAYOUT_KIND,
    });
  }

  setDesign(design: EmailLayoutDesign) {
    this.change({ design });
  }

  /** Saves the layout when it holds changes or is new; whether it is saved and holds what was saved after. */
  async saveFirst(): Promise<boolean> {
    if (this.isSavedAsShown()) return true;
    try {
      await this.requestSubmit();
    } catch {
      return false;
    }
    return this.isSavedAsShown();
  }
}

export { EmailLayoutWorkspaceContext as api };
