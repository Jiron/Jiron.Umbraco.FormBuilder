// "Preview" in the form workspace's footer (docs/ux/information-architecture.md 5.7, ADR-0203): saves what is not saved
// yet, as Publish... does, since the preview shows the draft as it was saved; then opens the preview at the designer's
// width and in its language. A save refused, a conflict among them, opens nothing, and says why itself. One preview at a
// time for a workspace.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UmbWorkspaceActionBase, type UmbWorkspaceActionArgs } from '@umbraco-cms/backoffice/workspace';
import { readWidth } from '../canvas/widths';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import { readLook } from '../preview/look';
import { PREVIEW_MODAL } from './preview.modal.token';

// The workspaces whose preview is opening or open.
const open = new WeakSet<object>();

export class PreviewFormAction extends UmbWorkspaceActionBase {
  constructor(host: UmbControllerHost, args: UmbWorkspaceActionArgs<never>) {
    super(host, args);
    // The button waits only while the draft is saved.
    this.setExecuting(false);
  }

  override async execute() {
    const context = await this.getContext(FORM_WORKSPACE_CONTEXT);
    if (!context || open.has(context)) return;
    open.add(context);
    try {
      if (context.getHasUnpersistedChanges()) {
        this.setExecuting(true);
        try {
          await context.requestSubmit();
        } catch {
          return;
        } finally {
          this.setExecuting(false);
        }
      }
      const form = context.getUnique();
      if (!form) return;
      const culture = context.getLanguage();
      const look = await readLook(this, culture ?? undefined);
      await umbOpenModal(this, PREVIEW_MODAL, {
        data: {
          name: context.getName() ?? '',
          form,
          languages: look?.languages ?? [],
          culture,
          width: readWidth(),
          read: (shown, dark) => context.visitorPreviewOf(shown, dark),
          ticket: (version) => context.previewTicketOf(version),
        },
      }).catch(() => undefined);
    } finally {
      open.delete(context);
    }
  }
}

export { PreviewFormAction as api };
