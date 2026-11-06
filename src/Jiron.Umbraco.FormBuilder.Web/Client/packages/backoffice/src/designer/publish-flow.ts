// The publish dialog's flow (docs/ux/information-architecture.md, section 5.7; ADR-0198): Publish... in the footer and
// Restore and publish... in the History open it alike. The dialog asks first, with the form check's result and an
// optional comment for the version, then the workspace saves what is unsaved and publishes. A Fix it chosen in the
// dialog leads to the view that makes it: the Settings view for a problem in the form's settings (ADR-0184), the Design
// view for any other (ADR-0181).
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { FormWorkspaceContext } from './form-workspace.context';
import { followFix } from './form-views';
import { PUBLISH_MODAL } from './publish.modal.token';

/** How the dialog ended: published, refused (said already), or closed without publishing (a Fix it among them). */
export type PublishOutcome = 'published' | 'refused' | 'closed';

/** Opens the publish dialog for the form's draft and publishes it once confirmed; `busy` follows the publish itself. */
export async function publishWithDialog(
  host: UmbControllerHost,
  context: FormWorkspaceContext,
  busy: (busy: boolean) => void = () => undefined,
): Promise<PublishOutcome> {
  const form = context.getData()?.form;
  if (!form) return 'closed';
  // The draft as it is now is checked as the dialog opens, which shows the answer once it comes.
  void context.checkNow();
  let gone: (() => void) | undefined;
  const closed = new Promise<void>((done) => (gone = done));
  const value = await umbOpenModal(host, PUBLISH_MODAL, {
    data: {
      name: context.getName() ?? form.name,
      check: context.check,
      types: context.getFieldTypes(),
      // The site's default texts name the elements, as everywhere outside the designer's language switch.
      culture: '',
      closed: () => gone?.(),
    },
  }).catch(() => undefined);
  if (!value) return 'closed';
  if ('fix' in value) {
    // The view that makes the Fix it takes it up once it is shown: the Settings view one in the form's settings, the
    // Design view any other. The dialog answers before it closes and gives the focus back to what opened it, so the
    // Fix it waits.
    await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
    followFix(value.fix, (fix) => context.requestFix(fix));
    return 'closed';
  }
  busy(true);
  try {
    // A refusal is said already.
    return (await context.publish(value.comment.trim() || null)) ? 'published' : 'refused';
  } finally {
    busy(false);
  }
}
