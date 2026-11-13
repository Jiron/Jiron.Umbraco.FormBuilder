// The form workspace (ROADMAP 9.3a, docs/ux/information-architecture.md section 5): Umbraco's named detail workspace
// over the draft endpoint, so its header edits the name, its footer saves, and leaving with unsaved changes asks first.
// It holds the editor model's state with its undo history: each edit changes the draft the workspace saves, and a load
// starts the history over, selecting the field a link names (`?field=`, ADR-0166).
import { UmbBasicState, UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import {
  UmbEntityNamedDetailWorkspaceContextBase,
  type UmbRoutableWorkspaceContext,
} from '@umbraco-cms/backoffice/workspace';
import { FORMULAS } from '@jfb/logic/calc';
import {
  fieldTypeChanges,
  fieldTypeList,
  formCheckCheck,
  actionTestTest,
  actionLastRunList,
  contentDocumentTypeGet,
  emailPreviewRender,
  outcomePreviewRender,
  formPreviewRender,
  formPreviewRunActions,
  formPreviewTestEntry,
  formPreviewTicket,
  formPublish,
  type FieldGroupResponseModel,
  type FormDraftResponseModel,
  type FieldTypeChangeResponseModel,
  type FieldTypeResponseModel,
  type FormVersionResponseModel,
} from '../api';
import { codeOf, sayRefusal } from '../library/library-changes';
import { testAnswer, type TestAnswer } from './actions/action-test';
import { contentTypeOf, type ContentType } from './actions/content-model';
import type { LastRun } from './actions/run-words';
import {
  commentVersion,
  compareVersions,
  pinVersion,
  readVersions,
  restoreVersion,
  type VersionComparison,
  type VersionPage,
} from './history/versions';
import { oneAtATime, restoreDraft, type RestoredDraft } from './history/restore-draft';
import { previewAnswer, type EmailPreview } from './actions/email-preview';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import { issuesOf, type FixTarget } from './check/form-check';
import { FormChecker, UNCHECKED, type CheckState } from './check/form-checker';
import { FORM_DETAIL_REPOSITORY_ALIAS, FORM_WORKSPACE_ALIAS } from './constants';
import { locate, normalise, type DesignDefinition } from './definition';
import { select, type Edit, type EditorState, type FieldTraits, type ModelContext } from './editor-model';
import { detailOf, keptAlias, SAVE_CONFLICT, type FormDetailModel } from './form-detail.data-source';
import { actionOfQuery, fieldOfQuery, formState, type FormState } from './form-state';
import { followFix } from './form-views';
import { EditorHistory } from './history';
import { afterNameFocus } from './name-focus';
import { loadWorkspaceWords } from './workspace-words';
import {
  FAILED,
  previewRead,
  runAnswer,
  testEntryRead,
  ticketRead,
  type PreviewAsk,
  type PreviewRead,
  type TicketAnswer,
  type RunAnswer,
  type TestEntryRead,
} from './visitor-preview/preview-read';

/** What the views draw: the editor's state and whether undo and redo can do anything. */
export interface EditorView {
  readonly state: EditorState;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
}

const EMPTY: EditorState = { definition: { pages: [] }, selection: null };

/** The name guard's rule for who may not design the form. */
const NO_DESIGN = 'Jiron.FormBuilder.NameGuard.NoDesign';

/** What the editor model needs to know of a field type, from the field types endpoint. */
export function traitsOf(type: FieldTypeResponseModel): FieldTraits {
  return {
    kind: type.ruleKind ?? null,
    onePerForm: type.onePerForm,
    childFields: type.features.includes('childFields'),
  };
}

/**
 * A read by key that is made once: every later ask for the key gets the same answer, while one that found nothing
 * (undefined) is made again at the next ask.
 */
export function readOnce<T>(read: (key: string) => Promise<T | undefined>): (key: string) => Promise<T | undefined> {
  const kept = new Map<string, Promise<T | undefined>>();
  return (key) => {
    const found = kept.get(key);
    if (found) return found;
    const reading = read(key).then((value) => {
      if (value === undefined) kept.delete(key);
      return value;
    });
    kept.set(key, reading);
    return reading;
  };
}

export class FormWorkspaceContext
  extends UmbEntityNamedDetailWorkspaceContextBase<FormDetailModel>
  implements UmbRoutableWorkspaceContext
{
  readonly #history = new EditorHistory(EMPTY);
  readonly #editor = new UmbObjectState<EditorView>({ state: EMPTY, canUndo: false, canRedo: false });
  /** The field the address named when the form was opened, until the load that selects it. */
  #wanted: string | null = null;
  /** Whether the field types were read, without which the model can check nothing. */
  #typesRead = false;
  /** Whether the context has ended (destroy). */
  #ended = false;
  // A record, not a Map: the state compares values as JSON, which writes every Map as {}.
  readonly #types = new UmbObjectState<Readonly<Record<string, FieldTypeResponseModel>>>({});
  readonly #groups = new UmbObjectState<readonly FieldGroupResponseModel[]>([]);

  /** The field types by alias, as the views name and draw them; empty until they are read. */
  readonly fieldTypes = this.#types.asObservable();

  /** The palette's groups, in its order; empty until the field types are read. */
  readonly paletteGroups = this.#groups.asObservable();

  readonly #check = new UmbObjectState<CheckState>(UNCHECKED);
  /** The form the check's answers are about, so another form read starts it over. */
  #checkedForm: string | null = null;
  readonly #checker = new FormChecker(
    (definition) => this.#askCheck(definition),
    (state) => this.#check.setValue(state),
  );

  /** What the form check found in the draft (ADR-0181), as the badge, its sidebar and the publish dialog show it. */
  readonly check = this.#check.asObservable();

  readonly #fix = new UmbBasicState<FixTarget | null>(null);

  /** A Fix it asked from outside the view that makes it, which that view takes up once it is shown (ADR-0181). */
  readonly fixRequest = this.#fix.asObservable();

  /** The language the views show the form's texts in, as its switch chose it; null for the site's default. */
  #language: string | null = null;

  /** Whether the save on its way was refused as a conflict, as the data source says (ADR-0200). */
  #conflicted = false;

  readonly #onConflict = () => {
    this.#conflicted = true;
  };

  /** Restores a version as a new draft, one at a time (ADR-0198). */
  readonly #restore = oneAtATime((key: string, number: number) =>
    restoreDraft(this, key, number, (form, version, concurrencyVersion) =>
      restoreVersion(this, form, version, concurrencyVersion),
    ),
  );

  /** What the editor model asks of the workspace: the field types, fresh keys and the calculation language. */
  readonly model: ModelContext = {
    traitsOf: (type) => {
      const types = this.#types.getValue();
      const found = Object.hasOwn(types, type) ? types[type] : undefined;
      return found ? traitsOf(found) : undefined;
    },
    newKey: () => crypto.randomUUID(),
    formulas: FORMULAS,
  };

  /** The editor's state, as the views draw it. */
  readonly editor = this.#editor.asObservable();

  /** The form as the last answer gave it. */
  readonly form = this._data.createObservablePartOfCurrent((data) => data?.form);

  /** The draft's version number, which History compares a version with (ADR-0196); null without the draft. */
  readonly draftVersion = this._data.createObservablePartOfCurrent((data) => data?.draftVersion ?? null);

  /** The live version's number, which a publish moves; null with nothing published, undefined before the form is read. */
  readonly publishedVersion = this._data.createObservablePartOfCurrent((data) =>
    data ? data.publishedVersion : undefined,
  );

  /** The state chip's state. */
  readonly state = this._data.createObservablePartOfCurrent((data): FormState | undefined =>
    data ? formState(data.form, data.hasUnpublishedChanges) : undefined,
  );

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: FORM_WORKSPACE_ALIAS,
      entityType: FORM_ENTITY_TYPE,
      detailRepositoryAlias: FORM_DETAIL_REPOSITORY_ALIAS,
    });
    this.routes.setRoutes([
      {
        path: 'edit/:unique',
        // The workspace's own words come with it, before anything of it draws (ADR-0201); without them it still opens.
        component: async () => {
          await loadWorkspaceWords(new UmbLocalizationController(this).lang()).catch(() => undefined);
          return import('./form-workspace-editor.element');
        },
        setup: async (_component, info) => {
          // Read before the router moves on to a view, which drops the query.
          const wanted = fieldOfQuery(window.location.search);
          const action = actionOfQuery(window.location.search);
          const unique = info.match.params['unique'] ?? '';
          // The form open already is not read again: a link to one of its fields selects it here.
          if (unique === this.getUnique() && this.getData()) {
            if (wanted && locate(this.#history.state.definition, wanted)?.kind === 'field')
              this.edit(select(this.#history.state, wanted), '');
            if (action) this.#showAction(action);
            return;
          }
          this.#wanted = wanted;
          await this.load(unique);
          if (action) this.#showAction(action);
        },
      },
    ]);
    void this.#loadTypes();
    this.getHostElement().addEventListener(SAVE_CONFLICT, this.#onConflict);
  }

  /**
   * A link's `?action=` (ADR-0166, ADR-0188): the Actions view shows that action's card with the focus on it, as a Fix
   * it at the action. It is asked once the router has shown the view it routes to, which it would otherwise follow, and
   * once the name box holds the focus Umbraco gives it as the workspace opens, which it would otherwise take back. A press
   * before then is the user's move, which it leaves alone; and who may not design the form has no Actions view to go to.
   */
  #showAction(key: string) {
    setTimeout(
      () =>
        void afterNameFocus().then((free) => {
          if (free && this.canDesign())
            followFix({ kind: 'view', view: 'actions', path: '$.actions', key }, (fix) => this.requestFix(fix));
        }),
    );
  }

  async #loadTypes() {
    const { data } = await tryExecute(this, fieldTypeList(), { disableNotifications: true });
    if (!data) return;
    this.#types.setValue(Object.fromEntries(data.types.map((type) => [type.alias, type])));
    this.#groups.setValue(data.paletteGroups);
    this.#typesRead = true;
  }

  /**
   * The changes of type a field of the type may make (ADR-0059), in the palette's order, with the host's locks of each
   * new type applied (ADR-0060); undefined when they can't be read.
   */
  // Each type's changes are read once while the form is open (ADR-0174), and again after a read that failed.
  readonly changesOf: (type: string) => Promise<readonly FieldTypeChangeResponseModel[] | undefined> = readOnce(
    async (type) =>
      (await tryExecute(this, fieldTypeChanges({ path: { alias: type } }), { disableNotifications: true })).data
        ?.changes,
  );

  /** The field types by alias, as read; empty until they are. */
  getFieldTypes(): Readonly<Record<string, FieldTypeResponseModel>> {
    return this.#types.getValue();
  }

  /** Whether the user may design the open form. */
  canDesign(): boolean {
    return this.getData()?.form.permissions.includes('design') ?? false;
  }

  /**
   * The alias a field is saved with when entries may name it (section 4.3.1): the form has entries and the field is in
   * the saved draft. Changing it away from that alias asks first and keeps it for merge tags; null for a field whose
   * alias no entry knows yet, which changes freely.
   */
  keptAliasOf(key: string): string | null {
    return keptAlias(this._data.getPersisted(), key);
  }

  // Every load starts the history over, with the field a link named selected when the form has it, else what was
  // selected before when it is still there, as after a publish.
  protected override async _processIncomingData(data: FormDetailModel): Promise<FormDetailModel> {
    const kept = this.#wanted ?? this.#history.state.selection;
    this.#wanted = null;
    const selection = kept && locate(data.definition, kept) ? kept : null;
    this.#history.reset({ definition: data.definition, selection });
    this.#guardName(data);
    this.#publish();
    this.#checkChanged(data);
    return data;
  }

  // Who may not design reads the name and can't change it; Umbraco's name field honours the guard.
  #guardName(data: FormDetailModel) {
    if (data.form.permissions.includes('design')) this.nameWriteGuard.removeRule(NO_DESIGN);
    else this.nameWriteGuard.addRule({ unique: NO_DESIGN, permitted: false });
  }

  /**
   * Saves, as Umbraco's base does, then puts the saved definition in the history's present place, unless the draft
   * changed while the save was on its way: that change, an edit or an undo, stays, unsaved, where Umbraco's base put the
   * saved copy in its place. A save refused because someone saved meanwhile offers to review both versions (ADR-0200);
   * the form stays as it was read, so every later save is refused the same way until the review.
   */
  override async submit(): Promise<void> {
    const sent = this.#history.state.definition;
    this.#conflicted = false;
    try {
      await super.submit();
    } catch (error) {
      // Umbraco's base doesn't pass a save's error on: the data source said a conflict as it answered.
      if (this.#conflicted) void import('./conflict/conflict-flow').then(({ sayConflict }) => sayConflict(this));
      throw error;
    }
    const saved = this._data.getPersisted()?.definition;
    if (saved && !this.#history.settle(sent, saved))
      this._data.updateCurrent({ definition: this.#history.state.definition });
    this.#publish();
  }

  /** The form as it was last read or saved: the draft the user's changes start from, and its name. */
  getBase(): { readonly draftVersion: number | null; readonly name: string } | undefined {
    const persisted = this._data.getPersisted();
    return persisted ? { draftVersion: persisted.draftVersion, name: persisted.name } : undefined;
  }

  /**
   * Takes Review both versions' draft (ADR-0200): the draft saved meanwhile becomes the one the user's changes start
   * from, and the draft the review made, with the name chosen, the user's changes, which a save then saves. It is a
   * step to undo, so Undo takes the user's own version back.
   */
  takeReview(theirs: FormDraftResponseModel, definition: DesignDefinition, name: string) {
    const saved = detailOf(theirs);
    const kept = this.#history.state.selection;
    const draft = normalise(definition);
    this._data.setPersisted(saved);
    const said = new UmbLocalizationController(this).term('jfbDesigner_saidReviewed');
    this.#history.apply(
      { ok: true, state: { definition: draft, selection: kept && locate(draft, kept) ? kept : null } },
      said,
    );
    this._data.setCurrent({ ...saved, name, definition: draft });
    this.#publish();
  }

  /**
   * Takes an edit of the editor model: a change to the definition goes into the draft and the undo history, a selection
   * only into the view. A change with the same `merge` as the one before joins its step to undo. Returns the edit,
   * whose refusal the caller says.
   */
  edit(edit: Edit, said: string, merge?: string): Edit {
    const before = this.#history.state.definition;
    // A change checked without the field types was checked against nothing: it waits for them.
    if (edit.ok && edit.state.definition !== before && !this.#typesRead) {
      void this.#loadTypes();
      return { ok: false, refusal: 'notReady' };
    }
    const result = this.#history.apply(edit, said, merge);
    if (result.ok && result.state.definition !== before)
      this._data.updateCurrent({ definition: result.state.definition });
    this.#publish();
    return result;
  }

  /** Takes the last change back; what it was, or undefined with nothing to undo. */
  undo(): string | undefined {
    return this.#travel(this.#history.undo());
  }

  /** Does the last undone change again; what it was, or undefined with nothing to redo. */
  redo(): string | undefined {
    return this.#travel(this.#history.redo());
  }

  /** The editor's state now. */
  getEditorState(): EditorState {
    return this.#history.state;
  }

  /**
   * Publishes the form, saving its draft first when it has unsaved changes. True once it is published; a refusal is
   * said, the form check's problems among them.
   */
  async publish(comment: string | null): Promise<boolean> {
    if (this.getHasUnpersistedChanges()) {
      try {
        await this.requestSubmit();
      } catch {
        return false;
      }
    }

    const form = this.getData()?.form;
    if (!form) return false;
    const { data, error } = await tryExecute(
      this,
      formPublish({ path: { key: form.key }, body: { concurrencyVersion: form.concurrencyVersion, comment } }),
      { disableNotifications: true },
    );
    if (!data) {
      await sayRefusal(this, error);
      return false;
    }

    await this.reload();
    const localize = new UmbLocalizationController(this);
    (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('positive', {
      data: { message: localize.term('jfbDesigner_published', data.form.name) },
    });
    return true;
  }

  #travel(said: string | undefined): string | undefined {
    if (said !== undefined) this._data.updateCurrent({ definition: this.#history.state.definition });
    this.#publish();
    return said;
  }

  #publish() {
    this.#editor.setValue({
      state: this.#history.state,
      canUndo: this.#history.canUndo,
      canRedo: this.#history.canRedo,
    });
    this.#checkChanged();
  }

  // ---- The form check (ADR-0181) ------------------------------------------------------------------------------------

  // The draft as it is now is checked once the changes pause, for who may design the form, whose draft it is. A load
  // passes the form it read, which the workspace holds only once the load is done.
  #checkChanged(data: FormDetailModel | undefined = this.getData()) {
    const form = data?.form.key ?? null;
    if (form !== this.#checkedForm) {
      this.#checkedForm = form;
      this.#checker.reset();
    }
    if (form && data?.form.permissions.includes('design')) this.#checker.changed(this.#history.state.definition);
  }

  async #askCheck(definition: DesignDefinition) {
    const key = this.getUnique();
    if (!key) return undefined;
    const { data } = await tryExecute(this, formCheckCheck({ path: { key }, body: { definition } }), {
      disableNotifications: true,
    });
    return data ? issuesOf(data.issues) : undefined;
  }

  /** Asks the form check about a definition other than the draft: an outcome's sidebar asks about its outcome. */
  checkOf(definition: DesignDefinition) {
    return this.#askCheck(definition);
  }

  /** The preview of an outcome of a definition, as visitors get it with the sample entry (ADR-0187); undefined without an answer. */
  async previewOf(definition: DesignDefinition, outcome: string, culture: string) {
    const key = this.getUnique();
    if (!key) return undefined;
    const { data } = await tryExecute(
      this,
      outcomePreviewRender({ path: { key }, body: { definition, outcome, culture } }),
      {
        disableNotifications: true,
      },
    );
    return data;
  }

  /**
   * The mail of an email action of a definition, as the team gets it for the sample entry (ADR-0189); that its view is
   * missing, where it has no body of its own; undefined without an answer.
   */
  async emailPreviewOf(
    definition: DesignDefinition,
    action: string,
    culture: string,
  ): Promise<EmailPreview | undefined> {
    const key = this.getUnique();
    if (!key) return undefined;
    const { data, error } = await tryExecute(
      this,
      emailPreviewRender({ path: { key }, body: { definition, action, culture } }),
      { disableNotifications: true },
    );
    return previewAnswer(data, codeOf(error));
  }

  /** Tests an action as the definition holds it, saved or not (ADR-0190), confirmed or not. */
  async testActionOf(definition: DesignDefinition, action: string, confirmed: boolean): Promise<TestAnswer> {
    const key = this.getUnique();
    if (!key) return { kind: 'noAnswer' };
    const { data, error } = await tryExecute(
      this,
      actionTestTest({ path: { key, actionKey: action }, body: { confirmed, definition } }),
      { disableNotifications: true },
    );
    return testAnswer(data, codeOf(error));
  }

  /**
   * The saved draft as visitors would get it, in a language of the site, light or dark, empty or with the sample answers,
   * its test entries running their actions or not (ADR-0203, ADR-0204).
   */
  async visitorPreviewOf(culture: string | null, ask: PreviewAsk): Promise<PreviewRead> {
    const key = this.getUnique();
    if (!key) return FAILED;
    const { data, error } = await tryExecute(
      this,
      formPreviewRender({
        path: { key },
        query: { ...(culture ? { culture } : {}), dark: ask.dark, sample: ask.sample, actions: ask.actions },
      }),
      { disableNotifications: true },
    );
    return previewRead(data, codeOf(error));
  }

  /**
   * A new ticket for the version of the form the preview shows, whose test entries run their actions or not (ADR-0203,
   * ADR-0204); `refused` for a content action's rights; undefined without an answer.
   */
  async previewTicketOf(version: number, runActions: boolean): Promise<TicketAnswer> {
    const key = this.getUnique();
    if (!key) return undefined;
    const { data, error } = await tryExecute(
      this,
      formPreviewTicket({ path: { key }, body: { version, runActions } }),
      { disableNotifications: true },
    );
    return ticketRead(data, codeOf(error));
  }

  /** The test entry the preview sent with a submission id (ADR-0204). */
  async testEntryOf(submissionId: string): Promise<TestEntryRead> {
    const key = this.getUnique();
    if (!key) return { kind: 'missing' };
    const { data } = await tryExecute(this, formPreviewTestEntry({ path: { key, submissionId } }), {
      disableNotifications: true,
    });
    return testEntryRead(data);
  }

  /** Runs the actions of the test entry the preview sent with a submission id, once (ADR-0204). */
  async runTestEntryActions(submissionId: string): Promise<RunAnswer> {
    const key = this.getUnique();
    if (!key) return 'failed';
    const { error } = await tryExecute(this, formPreviewRunActions({ path: { key, submissionId } }), {
      disableNotifications: true,
    });
    return runAnswer(!error, codeOf(error));
  }

  /** The last run of each action of the stored draft that has run (ADR-0195); undefined without an answer. */
  async lastRunsOf(): Promise<readonly LastRun[] | undefined> {
    const key = this.getUnique();
    if (!key) return undefined;
    const { data } = await tryExecute(this, actionLastRunList({ path: { key } }), { disableNotifications: true });
    return data?.runs;
  }

  /** A page of the form's versions, newest first, the draft saves too when asked (ADR-0196); undefined without an answer. */
  versionsOf(includeDrafts: boolean, skip: number): Promise<VersionPage | undefined> {
    const key = this.getUnique();
    return key ? readVersions(this, key, includeDrafts, skip) : Promise.resolve(undefined);
  }

  /** Two versions compared, the older first, with both definitions (ADR-0196). */
  comparisonOf(from: number, to: number): Promise<VersionComparison> {
    const key = this.getUnique();
    return key ? compareVersions(this, key, from, to) : Promise.resolve({ kind: 'failed' });
  }

  /** Pins or unpins a version; the version as it is now, or undefined after a refusal, which is said. */
  pinVersion(number: number, pinned: boolean): Promise<FormVersionResponseModel | undefined> {
    const key = this.getUnique();
    return key ? pinVersion(this, key, number, pinned) : Promise.resolve(undefined);
  }

  /** Sets a version's comment, empty to remove it; the version as it is now, or undefined after a refusal. */
  commentVersion(number: number, comment: string): Promise<FormVersionResponseModel | undefined> {
    const key = this.getUnique();
    return key ? commentVersion(this, key, number, comment) : Promise.resolve(undefined);
  }

  /**
   * Restores a version of the form `key` as a new draft (ADR-0198): changes not saved yet are saved first, as Publish...
   * saves them, so they stay in the History; then the form is read again, so every view shows the new draft. The draft's
   * number before, the new draft's, and whether the workspace shows it now; undefined after a refusal, which is said,
   * where the workspace holds another form, or while another restore runs, as a second one would be refused as a change
   * made meanwhile.
   */
  restoreVersion(key: string, number: number): Promise<RestoredDraft | undefined> {
    return this.#restore(key, number);
  }

  /**
   * A document type a content action can create, with the properties a form can set (ADR-0192): null where the site has
   * none of that alias, undefined where it could not be read.
   */
  async documentTypeOf(alias: string): Promise<ContentType | null | undefined> {
    const { data, error } = await tryExecute(this, contentDocumentTypeGet({ path: { alias } }), {
      disableNotifications: true,
    });
    if (data) return contentTypeOf(data);
    return codeOf(error) === 'document_type_not_found' ? null : undefined;
  }

  /** Checks the draft as it is now, as Publish does and Try again; it settles once the check shows the answer. */
  checkNow(): Promise<void> {
    return this.canDesign() ? this.#checker.now(this.#history.state.definition) : Promise.resolve();
  }

  /**
   * Asks for a Fix it, from the publish dialog or from a view that leads to another: the view that makes it takes it up
   * once it is shown, the Settings view a problem in the form's settings, the Design view any other.
   */
  requestFix(target: FixTarget) {
    this.#fix.setValue(target);
  }

  /** The Fix it asked, if it is one the view asking makes, which it takes once, so it is made once. */
  takeFix(makes: (target: FixTarget) => boolean): FixTarget | null {
    const target = this.#fix.getValue();
    if (!target || !makes(target)) return null;
    this.#fix.setValue(null);
    return target;
  }

  /** The language the views show the form's texts in, as the switch of either chose it; null for the site's default. */
  getLanguage(): string | null {
    return this.#language;
  }

  /** Keeps the language a view's switch chose, so the other view shows the texts in it too (ADR-0184). */
  setLanguage(culture: string | null) {
    this.#language = culture;
  }

  override destroy() {
    // Umbraco ends the context as its workspace closes, and the AI add-on's workspace registry ends it again, by when its
    // host is gone: it ends once.
    if (this.#ended) return;
    this.#ended = true;
    this.getHostElement()?.removeEventListener(SAVE_CONFLICT, this.#onConflict);
    this.#checker.stop();
    this.#editor.destroy();
    this.#types.destroy();
    this.#groups.destroy();
    this.#check.destroy();
    this.#fix.destroy();
    super.destroy();
  }
}

export { FormWorkspaceContext as api };
