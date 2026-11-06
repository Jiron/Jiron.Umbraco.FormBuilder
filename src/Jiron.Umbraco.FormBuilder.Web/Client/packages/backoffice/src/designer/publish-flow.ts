// The publish dialog's flow (docs/ux/information-architecture.md, section 5.7; ADR-0198): Publish... in the footer and
// Restore and publish... in the History open it alike. The dialog asks first, with what changed since the version
// visitors get (ROADMAP 10.3d), the form check's result and an optional comment for the version, then the workspace
// saves what is unsaved and publishes. A Fix it chosen in the dialog leads to the view that makes it: the Settings view
// for a problem in the form's settings (ADR-0184), the Design view for any other (ADR-0181).
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { FormWorkspaceContext } from './form-workspace.context';
import { followFix } from './form-views';
import type { PublishChanges } from './history/publish-changes';
import { loadHistoryWords } from './history/history-words';
import { PUBLISH_MODAL } from './publish.modal.token';

/** How the dialog ended: published, refused (said already), or closed without publishing (a Fix it among them). */
export type PublishOutcome = 'published' | 'refused' | 'closed';

/** The workspaces whose publish dialog is opening or open: a second Publish... meanwhile opens none. */
const publishing = new WeakSet<FormWorkspaceContext>();

/**
 * Opens the publish dialog for the form's draft and publishes it once confirmed; `busy` follows the publish itself. The
 * caller's localization says the dialog's words: a host holds one, and a second would take the first one's place.
 */
export async function publishWithDialog(
  host: UmbClassInterface,
  context: FormWorkspaceContext,
  busy: (busy: boolean) => void = () => undefined,
  localize: UmbLocalizationController = new UmbLocalizationController(host),
): Promise<PublishOutcome> {
  const data = context.getData();
  if (!data || publishing.has(context)) return 'closed';
  publishing.add(context);
  try {
    return await publishOnce(host, context, busy, localize, data.form.name, data.publishedVersion);
  } finally {
    publishing.delete(context);
  }
}

async function publishOnce(
  host: UmbClassInterface,
  context: FormWorkspaceContext,
  busy: (busy: boolean) => void,
  localize: UmbLocalizationController,
  name: string,
  published: number | null,
): Promise<PublishOutcome> {
  // The draft as it is now is checked as the dialog opens, and compared with the version visitors get, in History's
  // words, which load first, so the dialog's first frame says it in words; it shows each answer once it comes.
  void context.checkNow();
  const words = await loadHistoryWords(localize.lang()).then(
    () => true,
    () => false,
  );
  const failed: PublishChanges = { headline: null, parts: [], said: localize.term('jfbHistory_publish_failed') };
  const changes = words
    ? {
        // Code that doesn't load, as anything else that fails, leaves the dialog saying so, never waiting.
        said: import('./history/publish-changes')
          .then(({ changesSinceLive }) => changesSinceLive(host, context, localize))
          .catch(() => failed),
        comparing: localize.term(published === null ? 'jfbHistory_publish_reading' : 'jfbHistory_publish_comparing'),
      }
    : undefined;
  let gone: (() => void) | undefined;
  const closed = new Promise<void>((done) => (gone = done));
  const value = await umbOpenModal(host, PUBLISH_MODAL, {
    data: {
      name: context.getName() ?? name,
      check: context.check,
      types: context.getFieldTypes(),
      // The site's default texts name the elements, as everywhere outside the designer's language switch.
      culture: '',
      ...(changes ? { changes } : {}),
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
