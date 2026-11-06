// What History does with a version, wherever it is offered (ROADMAP 10.3a and 10.3b, IA 5.6, ADR-0196, ADR-0198): two
// versions compared in Umbraco's medium sidebar, in words, and a version restored as a new draft. A restore names what
// changed in a toast with Undo, which restores the draft before while the draft is still the restored one; after Restore
// and publish... the caller opens the publish dialog. The History view and Restore an earlier version... in the form's
// "..." share it.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDocumentItemRepository } from '@umbraco-cms/backoffice/document';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { actionTypeList, type ActionTypeResponseModel, type FormVersionEntryResponseModel } from '../../api';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import type { DesignDefinition } from '../definition';
import type { FormWorkspaceContext } from '../form-workspace.context';
import { readLook, word } from '../preview/look';
import { publishWithDialog } from '../publish-flow';
import { targetOf, type DesignOutcome } from '../settings/outcome-model';
import { comparisonWords, type ChangeWordsContext } from './change-words';
import { COMPARISON_MODAL, type ComparisonModalValue, type ComparisonRead } from './comparison.modal.token';
import { headingCase, madeAt, momentText, summaryParts, versionName } from './history-model';

type Version = FormVersionEntryResponseModel['version'];

/** The site's action types, read once per backoffice session; undefined when they can't be read, and tried again. */
let actionTypes: Promise<Readonly<Record<string, ActionTypeResponseModel>> | undefined> | undefined;

export class VersionActions extends UmbControllerBase {
  readonly #context: FormWorkspaceContext;
  readonly #localize: UmbLocalizationController;

  constructor(host: UmbControllerHost, context: FormWorkspaceContext) {
    super(host);
    this.#context = context;
    this.#localize = new UmbLocalizationController(this);
  }

  #full = (key: string, ...args: unknown[]) => this.#localize.term(key, ...args);

  /** A version as a sentence names it: "version 14", "the draft save of today, 09:12". */
  nameOf(version: Version): string {
    return versionName(version, new Date(), this.#localize.lang(), this.#full);
  }

  /** When a version was made and by whom: "Today, 09:12 - Ana Novak". */
  made(entry: FormVersionEntryResponseModel): string {
    const when = momentText(madeAt(entry.version), new Date(), this.#localize.lang(), this.#full);
    return entry.by ? this.#full('jfbHistory_whenBy', when, entry.by) : when;
  }

  // ---- Comparing ----------------------------------------------------------------------------------------------------

  /** Opens two versions compared, the older first; a restorable comparison answers the restore chosen. */
  async compare(
    older: Version,
    newer: Version,
    options: {
      readonly made?: string;
      readonly restorable?: boolean;
      readonly publishable?: boolean;
      readonly unsaved?: boolean;
    } = {},
  ): Promise<ComparisonModalValue | undefined> {
    const read = async (): Promise<ComparisonRead> => {
      const comparison = await this.#context.comparisonOf(older.number, newer.number);
      if (comparison.kind === 'failed') return { kind: 'failed' };
      if (comparison.kind === 'unreadable')
        return {
          kind: 'unreadable',
          name: headingCase(this.nameOf(comparison.number === older.number ? older : newer)),
        };
      return {
        kind: 'compared',
        parts: comparisonWords(comparison.changes, await this.#words(comparison.older, comparison.newer)),
      };
    };
    return umbOpenModal(this, COMPARISON_MODAL, {
      data: {
        headline: this.#full('jfbHistory_compare_headline', headingCase(this.nameOf(older)), this.nameOf(newer)),
        ...(options.made ? { made: options.made } : {}),
        olderName: headingCase(this.nameOf(older)),
        newerName: headingCase(this.nameOf(newer)),
        read,
        ...(options.restorable ? { restorable: true } : {}),
        ...(options.publishable ? { publishable: true } : {}),
        ...(options.restorable && options.unsaved ? { unsaved: true } : {}),
      },
    }).catch(() => undefined);
  }

  /**
   * Opens a version compared with the current draft, the version first, and restores it when that is chosen there; the
   * restore chosen once it is restored, so the caller opens the publish dialog after Restore and publish..., or undefined.
   * The current draft itself has nothing to be compared with.
   */
  async offer(entry: FormVersionEntryResponseModel): Promise<ComparisonModalValue['restore'] | undefined> {
    const draft = this.#context.getData()?.draftVersion;
    if (draft === null || draft === undefined || draft === entry.version.number) return undefined;
    const current: Version = { ...entry.version, number: draft, kind: 'draft', isDraft: true, isLive: false };
    // Restored, the live version is what visitors get already, so publishing it again would change nothing.
    const chosen = await this.compare(entry.version, current, {
      made: this.made(entry),
      restorable: true,
      publishable: !entry.version.isLive,
      unsaved: this.#context.getHasUnpersistedChanges(),
    });
    return chosen && (await this.restore(entry.version)) ? chosen.restore : undefined;
  }

  // ---- Restoring ----------------------------------------------------------------------------------------------------

  /**
   * Restores a version as a new draft, saying what changed with Undo; true once the workspace shows it. A refusal is said
   * by the notification, and nothing changes; a form left meanwhile keeps the restore, and nothing more is said.
   */
  async restore(version: Version): Promise<boolean> {
    const key = this.#context.getUnique();
    if (!key) return false;
    const done = await this.#context.restoreVersion(key, version.number);
    if (!done || this.#context.getUnique() !== key) return false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    if (!done.shown) {
      notifications?.peek('warning', { data: { message: this.#full('jfbHistory_unread') } });
      return false;
    }
    const data: UndoNotificationData = {
      message: await this.#restoredText(headingCase(this.nameOf(version)), done.before, done.after),
      undoLabel: this.#full('jfbDesigner_undo'),
      undo: async () => {
        // Only the draft this restore made goes back, unchanged and in its form: a change since would be lost, so it
        // stays, and the draft before stays in the History.
        const context = this.#context;
        if (
          context.getUnique() !== key ||
          context.getData()?.draftVersion !== done.after ||
          context.getHasUnpersistedChanges()
        ) {
          notifications?.peek('warning', { data: { message: this.#full('jfbHistory_undoStale') } });
          return;
        }
        // The draft before comes back as a new draft too, so the History keeps both steps.
        const undone = await context.restoreVersion(key, done.before);
        if (undone)
          notifications?.peek(undone.shown ? 'positive' : 'warning', {
            data: { message: this.#full(undone.shown ? 'jfbHistory_undone' : 'jfbHistory_unread') },
          });
      },
    };
    notifications?.peek('positive', { elementName: UNDO_NOTIFICATION, duration: UNDO_DURATION_MS, data });
    return true;
  }

  /** Opens the publish dialog for the draft, as Publish... does. */
  async publish(): Promise<void> {
    await publishWithDialog(this, this.#context);
  }

  // "Version 13 is the new draft: 4 fields, After sending, 1 rule and 1 action changed."
  async #restoredText(name: string, before: number, after: number): Promise<string> {
    const comparison = await this.#context.comparisonOf(before, after);
    if (comparison.kind !== 'compared') return this.#full('jfbHistory_restoredPlain', name);
    const parts = summaryParts(comparison.changes, this.#full, true);
    if (parts.length === 0) return this.#full('jfbHistory_restoredSame', name);
    return this.#full('jfbHistory_restored', name, this.#full('jfbHistory_listOf', ...parts));
  }

  // ---- The words' context -------------------------------------------------------------------------------------------

  async #words(older: DesignDefinition, newer: DesignDefinition): Promise<ChangeWordsContext> {
    actionTypes ??= tryExecute(this, actionTypeList(), { disableNotifications: true }).then(({ data }) => {
      if (!data) actionTypes = undefined;
      return data ? Object.fromEntries(data.types.map((type) => [type.alias, type])) : undefined;
    });
    const look = await readLook(this);
    const culture = look?.languages.find((language) => language.isDefault)?.culture ?? look?.culture ?? '';
    return {
      term: this.#full,
      say: (text) => (text ? this.#localize.string(text) : ''),
      culture,
      locale: this.#localize.lang(),
      languages: look?.languages ?? [],
      fieldTypes: this.#context.getFieldTypes(),
      actionTypes: (await actionTypes) ?? {},
      older,
      newer,
      pages: await this.#pages([older, newer], culture),
      defaultMessage: look ? word(look, 'thankYou') : '',
    };
  }

  /** The names of the pages the two versions' redirects lead to, in the site's default language where a page has it. */
  async #pages(definitions: readonly DesignDefinition[], culture: string): Promise<ReadonlyMap<string, string>> {
    const keys = [
      ...new Set(
        definitions.flatMap((definition) =>
          ((definition['outcomes'] ?? []) as readonly DesignOutcome[])
            .map((outcome) => targetOf(outcome))
            .flatMap((target) => (target.kind === 'page' ? [target.key] : [])),
        ),
      ),
    ];
    const names = new Map<string, string>();
    if (keys.length === 0) return names;
    // A page that can't be read is said as "a page of the site".
    const { data } = await new UmbDocumentItemRepository(this).requestItems(keys).catch(() => ({ data: undefined }));
    const wanted = culture.toLowerCase();
    for (const item of data ?? []) {
      const variant =
        item.variants.find((one) => one.culture?.toLowerCase() === wanted) ??
        item.variants.find((one) => !one.culture) ??
        item.variants[0];
      if (variant?.name) names.set(item.unique, variant.name);
    }
    return names;
  }
}
