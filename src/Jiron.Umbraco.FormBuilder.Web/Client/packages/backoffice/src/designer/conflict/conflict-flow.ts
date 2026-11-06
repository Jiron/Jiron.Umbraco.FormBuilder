// A save someone else's save refused (ROADMAP 10.3c, IA 5.7, ADR-0200). Umbraco's dialog says who saved, and when, after
// the user opened the form, and that the user's changes are still here. Review both versions reads the draft saved
// meanwhile, compares the user's with it, and both with the draft they started from, which tells who made each
// difference, then opens the review; its Save merges the choices into the draft and saves it. Nothing is overwritten
// unseen: Cancel, here or there, leaves the user's changes and the conflict as they are. One review at a time for a
// workspace; a draft changed while the versions were read is compared again, and a form left is left alone.
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formDraft } from '../../api';
import { confirmText } from '../../ui/confirm';
import { normalise } from '../definition';
import type { FormWorkspaceContext } from '../form-workspace.context';
import { differenceWords } from '../history/change-words';
import { madeAt } from '../history/history-model';
import { loadHistoryWords } from '../history/history-words';
import { changesBetween, compareWithDraft, readVersions } from '../history/versions';
import { wordsContextOf } from '../history/words-context';
import { differencesOf, merged, startingSide } from './review-model';
import { REVIEW_MODAL, type ReviewDifference } from './review.modal.token';

/** Who saved the form after the user opened it, and when, where the History says; null where it doesn't. */
interface Saver {
  readonly who: string;
  readonly at: Date;
}

/** The workspaces whose conflict is being said or reviewed: a second save meanwhile adds nothing. */
const saying = new WeakSet<FormWorkspaceContext>();

/** Says the conflict, and offers to review both versions; a review's Save saves the draft it makes. */
export async function sayConflict(context: FormWorkspaceContext): Promise<void> {
  const key = context.getUnique();
  if (!key || saying.has(context)) return;
  saying.add(context);
  const taken = await sayAndReview(context, key).finally(() => saying.delete(context));
  // A save refused again, by another save meanwhile, says itself as this one did.
  if (taken) await context.requestSubmit().catch(() => undefined);
}

/** The dialog, then the review; true once the workspace took the draft the review made. */
async function sayAndReview(context: FormWorkspaceContext, key: string): Promise<boolean> {
  const localize = new UmbLocalizationController(context);
  const saver = await saverOf(context, key);
  try {
    await confirmText(context, {
      headline: localize.term('jfbDesigner_conflict_headline'),
      content: saver
        ? localize.term('jfbDesigner_conflict_text', saver.who, whenText(saver.at, localize))
        : localize.term('jfbDesigner_conflict_textUnknown'),
      confirmLabel: localize.term('jfbDesigner_conflict_review'),
      color: 'positive',
    });
  } catch {
    return false;
  }
  return reviewBoth(context, key, saver, localize);
}

/**
 * Who saved the draft that is the form's now, where it is newer than the one the user started from and the History
 * names who made it; a publish or a move made since saves no draft.
 */
async function saverOf(context: FormWorkspaceContext, key: string): Promise<Saver | null> {
  const base = context.getBase()?.draftVersion ?? null;
  const draft = (await readVersions(context, key, true, 0))?.items.find((entry) => entry.version.isDraft);
  if (!draft?.by || base === null || draft.version.number <= base) return null;
  return { who: draft.by, at: madeAt(draft.version) };
}

/** "at 14:02" for today, "on 3 Nov at 14:02" for another day. */
function whenText(at: Date, localize: UmbLocalizationController): string {
  const locale = localize.lang();
  const time = new Intl.DateTimeFormat(locale, { timeStyle: 'short' }).format(at);
  if (at.toDateString() === new Date().toDateString()) return localize.term('jfbDesigner_conflict_at', time);
  const day = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short' }).format(at);
  return localize.term('jfbDesigner_conflict_on', day, time);
}

/** How often a draft that keeps changing while the versions are read is compared again. */
const COMPARES = 3;

/** Reviews both versions; true once the workspace took the draft the review made, which is then to be saved. */
async function reviewBoth(
  context: FormWorkspaceContext,
  key: string,
  saver: Saver | null,
  localize: UmbLocalizationController,
  compares = 1,
): Promise<boolean> {
  const term = (name: string, ...args: unknown[]) => localize.term(name, ...args);
  const failed = async () => {
    (await context.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('danger', {
      data: { message: term('jfbDesigner_conflict_reviewFailed') },
    });
    return false;
  };
  const words = await loadHistoryWords(localize.lang()).then(
    () => true,
    () => false,
  );
  const base = context.getBase();
  const yours = context.getData();
  if (!words || !base || !yours) return failed();
  const again = () => (compares < COMPARES ? reviewBoth(context, key, saver, localize, compares + 1) : failed());
  const { data: theirs } = await tryExecute(context, formDraft({ path: { key } }), { disableNotifications: true });
  if (!theirs || theirs.draftVersion === null || theirs.draftVersion === undefined) return failed();

  const from = base.draftVersion;
  const [toYours, baseToTheirs, baseToYours] = await Promise.all([
    compareWithDraft(context, key, theirs.draftVersion, yours.definition),
    // Theirs is the draft both started from where only the name or the form moved: nothing changed since.
    from === null
      ? undefined
      : from === theirs.draftVersion
        ? []
        : changesBetween(context, key, from, theirs.draftVersion),
    from === null ? undefined : compareWithDraft(context, key, from, yours.definition),
  ]);
  if (!toYours) return failed();
  const theirsDefinition = normalise(theirs.definition);
  const yoursName = yours.name.trim();
  // Without the draft both started from, every difference waits for a choice.
  const differences = differencesOf(
    toYours.changes,
    baseToTheirs && baseToYours ? { theirs: baseToTheirs, yours: baseToYours.changes } : null,
    { theirs: theirs.form.name, yours: yoursName, base: base.name },
  );
  const wordsContext = await wordsContextOf(
    context,
    localize,
    context.getFieldTypes(),
    theirsDefinition,
    toYours.definition,
  );
  const shown: ReviewDifference[] = differences.map((difference) => {
    const start = startingSide(difference);
    const said =
      difference.id === 'name'
        ? {
            title: term('jfbHistory_review_form'),
            rows: [{ what: term('jfbHistory_review_name'), older: theirs.form.name, newer: yoursName }],
          }
        : differenceWords(difference.changes, wordsContext);
    return {
      id: difference.id,
      ...said,
      ...(start ? { start } : {}),
      ...(difference.by === 'both' ? { both: true } : {}),
    };
  });

  // The form left meanwhile is left alone; a draft changed meanwhile is compared again.
  if (context.getUnique() !== key) return false;
  if (context.getData() !== yours) return again();
  const chosen = await umbOpenModal(context, REVIEW_MODAL, {
    data: {
      theirs: saver ? term('jfbHistory_review_theirs', saver.who) : term('jfbHistory_review_theirsUnknown'),
      differences: shown,
    },
  }).catch(() => undefined);
  if (!chosen || context.getUnique() !== key) return false;
  if (context.getData() !== yours) return again();
  const fromYours = new Set(chosen.fromYours);
  context.takeReview(
    theirs,
    merged(theirsDefinition, toYours.definition, differences, fromYours),
    fromYours.has('name') ? yoursName : theirs.form.name,
  );
  return true;
}
