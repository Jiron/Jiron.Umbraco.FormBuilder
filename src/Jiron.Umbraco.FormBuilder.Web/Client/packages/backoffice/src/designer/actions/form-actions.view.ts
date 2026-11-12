// The form's Actions view (ROADMAP 10.2a, IA 5.3, ADR-0188): the actions by trigger, Submitted, Approved, Rejected and
// Verified, each trigger with when it runs; one whose setting is off says so with a link to the setting. Add action
// opens an action's sidebar ready to email the team; a card's name opens its own. Each change is a change of the draft
// with one step to undo: a move within a trigger, a switch, a copy, a move to another trigger, an action added, changed
// or deleted, the last with Undo in its toast too. The form check's problems in an action are counted on its card, and
// its Fix it, or a link's `?action=`, leads to the action. Without `manageActions` the view is read-only. The view's
// words load with it (`action-words.ts`), before it draws.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  actionTypeList,
  formSettingsChoices,
  type ActionTypeResponseModel,
  type FieldTypeResponseModel,
} from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { confirmText } from '../../ui/confirm';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import { parsePath, type FixTarget } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { EditorView, FormWorkspaceContext } from '../form-workspace.context';
import { followFix, routerSettled, viewPath } from '../form-views';
import { same } from '../inspector/setting-values';
import { readLook, type CanvasLook } from '../preview/look';
import { actionLacks } from '../translations';
import { elementNames, inspectorRuleWords } from '../rules/rule-names';
import { ruleWords } from '../rules/rule-words';
import { flagWith, type Flag } from '../settings/problem-lines';
import './action-list.element';
import type { ActionListElement, ActionCard, CardRequest } from './action-list.element';
import {
  TRIGGERS,
  actionsOf,
  approvesEachEntry,
  choicesOf,
  duplicated,
  inTrigger,
  isOn,
  keptIn,
  movedWithin,
  placeOf,
  runRuleOf,
  settingAtPath,
  triggerNeed,
  triggerOf,
  withActions,
  type ActionSetting,
  type DesignAction,
  type Trigger,
  type TriggerNeed,
} from './action-model';
import type { LayoutChoice } from './action-editors';
import { actionLine, actionName, typeOf, type ActionWords } from './action-lines';
import { runTest, testHint } from './action-test';
import { ACTION_MODAL } from './action.modal.token';
import { ACTION_RUN_MODAL } from './action-run.modal.token';
import { runDetails, runLine, type LastRun } from './run-words';
import { loadActionWords } from './action-words';
import { jfbPhoneColumn } from '../../styles/phone';

/** The setting a trigger needs, as a Fix it in the Settings view names it. */
const NEEDED_AT: Readonly<Record<TriggerNeed, string>> = {
  approval: '$.settings.submission.approval',
  verification: '$.settings.submission.verification',
};

export class FormActionsViewElement extends UmbLitElement {
  static override properties = {
    _view: { state: true },
    _types: { state: true },
    _actionTypes: { state: true },
    _limit: { state: true },
    _layouts: { state: true },
    _look: { state: true },
    _failed: { state: true },
    _check: { state: true },
    _manage: { state: true },
    _said: { state: true },
    _runs: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-6);
        max-inline-size: 760px;
        margin-inline: auto;
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .note,
      .none,
      .empty {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .note {
        font-size: var(--uui-type-small-size);
      }

      .read-only {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-1);
      }

      .read-only uui-icon {
        flex: none;
        margin-block-start: 2px;
        color: var(--jfb-bo-color-text-muted);
      }

      .trigger {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .trigger-head {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        column-gap: var(--uui-size-space-3);
      }

      .trigger-head h2 {
        margin: 0;
        font-size: var(--uui-type-h5-size);
        font-weight: 700;
      }

      .when {
        color: var(--jfb-bo-color-text-muted);
      }

      /* A trigger that holds nothing, or does not run: one line in a dashed box, never a card. */
      .off,
      .none {
        margin: 0;
        padding: var(--uui-size-space-4);
        border: 1px dashed var(--uui-color-border-standalone);
        border-radius: var(--uui-border-radius);
        color: var(--jfb-bo-color-text-muted);
      }

      .off a {
        color: var(--uui-color-interactive);
        font-weight: 600;
      }

      .off a:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .skeleton {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .skeleton span {
        display: block;
        block-size: 64px;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
      }

      .skeleton span:first-child {
        inline-size: 20%;
        block-size: 24px;
      }

      .failed {
        display: grid;
        justify-items: start;
        gap: var(--uui-size-space-4);
      }

      .failed p {
        margin: 0;
      }
    `,
    jfbPhoneColumn,
  ];

  declare private _view: EditorView | undefined;
  declare private _types: Readonly<Record<string, FieldTypeResponseModel>>;
  declare private _actionTypes: readonly ActionTypeResponseModel[] | undefined;
  declare private _limit: number;
  /** The email layouts a mail may name and the site's default, from the form's setting choices (ADR-0189). */
  declare private _layouts: { readonly items: readonly LayoutChoice[]; readonly defaultKey: string };
  declare private _look: CanvasLook | undefined;
  declare private _failed: boolean;
  declare private _check: CheckState;
  /** Whether the user may change the form's actions. */
  declare private _manage: boolean;
  declare private _said: string;
  /** Each action's last run, by its key (ADR-0195). */
  declare private _runs: ReadonlyMap<string, LastRun>;

  #context: FormWorkspaceContext | undefined;
  /** The form whose action types and choices were read, so another form open reads them again. */
  #form: string | null = null;
  /** Whether visitors get a published version, which an action added or changed reaches only once it is published. */
  #published = false;
  /** Whether an action's sidebar is open, so a second never opens over it. */
  #editing = false;
  /** How many reads of the last runs were made, so only the latest one's answer is said. */
  #runReads = 0;
  /** The last toast with Undo, closed by the next one: its Undo would take back another change. */
  #undoToast: { close(): void } | undefined;

  constructor() {
    super();
    this._types = {};
    this._limit = 0;
    this._layouts = { items: [], defaultKey: '' };
    this._failed = false;
    this._check = UNCHECKED;
    this._manage = false;
    this._said = '';
    this._runs = new Map();
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.observe(context.editor, (view) => (this._view = view), 'editor');
      this.observe(context.check, (check) => (this._check = check ?? UNCHECKED), 'check');
      this.observe(context.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context.state, (state) => (this.#published = state === 'published' || state === 'pending'), 'state');
      this.observe(
        context.form,
        (form) => (this._manage = form?.permissions.includes('manageActions') ?? false),
        'permissions',
      );
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          this.#form = unique;
          // Another form's runs are never said on this one's cards, even while its own are not read yet.
          this._runs = new Map();
          void this.#read();
        },
        'unique',
      );
      // A Fix it asked from the publish dialog while this view is shown, or from the view that led here.
      this.observe(
        context.fixRequest,
        (target) => {
          if (target) void this.#fixPending();
        },
        'fix',
      );
    });
    this.addEventListener('keydown', (event) => this.#onKey(event));
  }

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    this._failed = false;
    const form = this.#form;
    if (!form) return;
    const culture = this.#context?.getLanguage() ?? undefined;
    const [words, types, choices, look] = await Promise.all([
      loadActionWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      tryExecute(this, actionTypeList(), { disableNotifications: true }),
      tryExecute(this, formSettingsChoices({ path: { key: form } }), { disableNotifications: true }),
      readLook(this, culture),
    ]);
    // A form chosen meanwhile wins over the answer for the one before it.
    if (form !== this.#form) return;
    if (!words || !types.data || !choices.data || !look) {
      this._failed = true;
      return;
    }
    this._actionTypes = types.data.types;
    this._limit = choices.data.limits.actions;
    this._layouts = { items: choices.data.emailLayouts, defaultKey: choices.data.defaults.emailLayout };
    this._look = look;
    void this.#readRuns();
    await this.updateComplete;
    await this.#fixPending();
  }

  // The last runs, which change as entries come in: read with the view, and again when its page is shown again. Only the
  // latest read's answer counts, so one that comes late never takes newer runs back.
  async #readRuns() {
    const form = this.#form;
    const read = ++this.#runReads;
    const runs = await this.#context?.lastRunsOf();
    if (!runs || form !== this.#form || read !== this.#runReads) return;
    this._runs = new Map(runs.map((run) => [run.actionKey, run]));
  }

  #shown = () => {
    if (document.visibilityState === 'visible' && this._actionTypes) void this.#readRuns();
  };

  override connectedCallback() {
    super.connectedCallback();
    document.addEventListener('visibilitychange', this.#shown);
  }

  override disconnectedCallback() {
    document.removeEventListener('visibilitychange', this.#shown);
    super.disconnectedCallback();
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  #announce(text: string) {
    // The same words twice in a row are said again: the live region hears a change.
    this._said = this._said === text ? `${text} ` : text;
  }

  #typeOf(alias: string): ActionTypeResponseModel | undefined {
    return typeOf(this.#words(), alias);
  }

  /** An action's name as it shows: its own, else its type's, else its type's alias. */
  #nameOf(action: DesignAction): string {
    return actionName(this.#words(), action);
  }

  #words(): ActionWords {
    return { types: this._actionTypes, localize: this.localize };
  }

  /** When a trigger runs, in words: Approved, on a form without manual approval, as each entry comes in. */
  #when(trigger: Trigger, definition: DesignDefinition): string {
    return this.#term(`when_${trigger === 'approved' && approvesEachEntry(definition) ? 'approvedEach' : trigger}`);
  }

  // ---- Changes ------------------------------------------------------------------------------------------------------

  #definition(): DesignDefinition {
    return this._view?.state.definition ?? { pages: [] };
  }

  /** Makes a change of the actions, named for Undo; false where it changes nothing or is refused. */
  #change(actions: readonly DesignAction[], said: string): boolean {
    const context = this.#context;
    const view = this._view;
    if (!context || !view || !this._manage) return false;
    const definition = withActions(view.state.definition, actions);
    if (same(definition['actions'], view.state.definition['actions'])) return false;
    const result = context.edit({ ok: true, state: { definition, selection: view.state.selection } }, said);
    if (!result.ok) this.#announce(this.localize.term(`jfbDesigner_refusal_${result.refusal}`));
    else this.#closeUndo();
    return result.ok;
  }

  /** Whether the form holds the most actions it can have. */
  #full(): boolean {
    return this._limit > 0 && actionsOf(this.#definition()).length >= this._limit;
  }

  /**
   * Undoes or redoes the last change, its toast closed; the focus stays on the card it was on, wherever the step put
   * it, or goes to Add action where the step took the card away.
   */
  async #travel(way: 'undo' | 'redo') {
    const context = this.#context;
    if (!context) return;
    const focused = this.#focusedKey();
    const said = way === 'undo' ? context.undo() : context.redo();
    if (said === undefined) return;
    this.#closeUndo();
    this.#announce(this.localize.term(way === 'undo' ? 'jfbDesigner_saidUndone' : 'jfbDesigner_saidRedone', said));
    if (focused) await this.#focusAction(focused);
  }

  /** The action whose card holds the focus, or null. */
  #focusedKey(): string | null {
    for (const list of this.shadowRoot?.querySelectorAll<ActionListElement>('jfb-action-list') ?? []) {
      const key = list.focusedKey();
      if (key) return key;
    }
    return null;
  }

  #onKey(event: KeyboardEvent) {
    const target = event.composedPath()[0];
    // A box that takes text has its own Undo.
    const entry =
      (target instanceof HTMLInputElement && target.type === 'text') ||
      target instanceof HTMLTextAreaElement ||
      (target instanceof HTMLElement && target.isContentEditable);
    if (entry || !(event.ctrlKey || event.metaKey) || event.altKey) return;
    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) void this.#travel('undo');
    else if (key === 'y' || (key === 'z' && event.shiftKey)) void this.#travel('redo');
    else return;
    event.preventDefault();
  }

  #list(trigger: Trigger): ActionListElement | null {
    return this.shadowRoot?.querySelector<ActionListElement>(`jfb-action-list[data-trigger="${trigger}"]`) ?? null;
  }

  /**
   * Puts the focus on an action's card; where it is gone, on Add action, or on the first card where Add action is not
   * offered (read-only, or at the most actions).
   */
  async #focusAction(key: string | null) {
    await this.updateComplete;
    const actions = actionsOf(this.#definition());
    const action = key ? actions.find((one) => one.key === key) : undefined;
    if (action && (await this.#list(triggerOf(action))?.focusAction(action.key))) return;
    const add = this.shadowRoot?.querySelector<HTMLElement>('#add');
    if (add) {
      add.focus();
      return;
    }
    const first = TRIGGERS.flatMap((trigger) => inTrigger(actions, trigger))[0];
    if (first) await this.#list(triggerOf(first))?.focusAction(first.key);
  }

  #move(trigger: Trigger, from: number, to: number) {
    this.#change(movedWithin(actionsOf(this.#definition()), trigger, from, to), this.#term('undoMoved'));
  }

  #toggle(key: string, on: boolean) {
    const actions = actionsOf(this.#definition());
    const action = actions.find((one) => one.key === key);
    if (!action) return;
    // The switch says its own state; the change is one step to undo.
    this.#change(
      actions.map((one) => (one.key === key ? { ...one, enabled: on } : one)),
      this.#term('undoSwitched'),
    );
  }

  async #request(key: string, request: CardRequest) {
    const actions = actionsOf(this.#definition());
    const action = actions.find((one) => one.key === key);
    if (!action) return;
    switch (request) {
      case 'duplicate': {
        // At the most actions "..." offers no Duplicate, and a request made anyway makes none.
        if (this.#full()) return;
        const next = duplicated(
          actions,
          key,
          () => crypto.randomUUID(),
          (name) => this.#term('copyName', name),
        );
        const copy = next[next.findIndex((one) => one.key === key) + 1];
        if (!copy || !this.#change(next, this.#term('undoDuplicated'))) return;
        this.#announce(this.#term('duplicated', this.#nameOf(copy)));
        await this.#focusAction(copy.key);
        return;
      }
      case 'moveTrigger':
        await this.#openAction(key, { kind: 'trigger' });
        return;
      case 'delete':
        await this.#delete(action);
        return;
    }
  }

  /** Takes an action away with Undo in the toast; the focus goes to the card after it in its trigger, or before it. */
  async #delete(action: DesignAction) {
    const actions = actionsOf(this.#definition());
    const trigger = triggerOf(action);
    const siblings = inTrigger(actions, trigger);
    const at = siblings.findIndex((one) => one.key === action.key);
    if (
      !this.#change(
        actions.filter((one) => one.key !== action.key),
        this.#term('undoDeleted'),
      )
    )
      return;
    void this.#offerUndo(this.#term('deleted', this.#nameOf(action)), action.key);
    const next = siblings[at + 1] ?? siblings[at - 1];
    await this.#focusAction(next?.key ?? null);
  }

  #getNotifications() {
    return this.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
  }

  // ---- Test ---------------------------------------------------------------------------------------------------------

  /**
   * Tests an action as this definition holds it, saved or not (ADR-0190): a type that reaches what the action is set up
   * to reach asks first, and a toast says what it came to.
   */
  async #test(definition: DesignDefinition, key: string, started?: () => void): Promise<void> {
    const context = this.#context;
    const action = actionsOf(definition).find((one) => one.key === key);
    if (!context || !action || !this._manage) return;
    const type = this._actionTypes?.find((one) => one.alias === action.type);
    // Where it goes, as the card says it.
    const line = this.#line(
      action,
      elementNames(definition, this._look?.culture ?? '', (term: string, ...args: unknown[]) =>
        this.localize.term(term, ...args),
      ),
    );
    await runTest({
      name: this.#nameOf(action),
      type: action.type,
      asksFirst: type?.test === 'afterConfirmation',
      started,
      confirm: () =>
        confirmText(this, {
          headline: this.#term('testConfirmHeadline'),
          content: line ? this.#term('testConfirmWhere', line) : this.#term('testConfirmText'),
          confirmLabel: this.#term('testConfirmButton'),
        }).then(
          () => true,
          () => false,
        ),
      ask: (confirmed) => context.testActionOf(definition, key, confirmed),
      say: async (toast) => {
        const notifications = await this.#getNotifications();
        if (toast.stays) notifications?.stay(toast.color, { data: { message: toast.message } });
        else notifications?.peek(toast.color, { data: { message: toast.message } });
      },
      term: this.#term,
    });
  }

  #closeUndo() {
    this.#undoToast?.close();
    this.#undoToast = undefined;
  }

  // A toast with Undo for the action just deleted, closed by the next one: its Undo would take back another. Undo puts
  // the focus on the action back in the list, as the toast it was on is gone.
  async #offerUndo(message: string, restored: string) {
    const made = this._view?.state;
    this.#closeUndo();
    const data: UndoNotificationData = {
      message,
      undoLabel: this.localize.term('jfbDesigner_undo'),
      undo: async () => {
        if (this._view?.state !== made) return;
        this.#travel('undo');
        await this.#focusAction(restored);
      },
    };
    this.#undoToast = (await this.#getNotifications())?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  // ---- The sidebar --------------------------------------------------------------------------------------------------

  /**
   * An action's sidebar, or a new one's with null: Update keeps the action in its place (at the end of its new
   * trigger's where its trigger changed), Add puts a new one at the end of its trigger's, and Delete action takes it
   * away with Undo in the toast. On a published form a toast says when new entries get a change; a change that changed
   * nothing says nothing.
   */
  async #openAction(key: string | null, focus?: ActionSetting) {
    const context = this.#context;
    const look = this._look;
    const types = this._actionTypes;
    const form = this.#form;
    if (!context || !look || !types || !form || this.#editing) return;
    const definition = this.#definition();
    const actions = actionsOf(definition);
    const action = key === null ? null : (actions.find((one) => one.key === key) ?? null);
    if (key !== null && !action) return;
    if (key === null && (!this._manage || (this._limit > 0 && actions.length >= this._limit))) return;
    this.#editing = true;
    try {
      // The sidebar answers before it slides out, and its dialog gives the focus back to what opened it as it closes;
      // the focus set here waits until it is gone, so it stays.
      let gone: (() => void) | undefined;
      const closed = new Promise<void>((done) => (gone = done));
      // An email shows its mail beside its settings, in Umbraco's large sidebar (ADR-0189); a new action starts as one.
      const email = (action?.type ?? choicesOf(types)[0]?.type) === 'email';
      const value = await umbOpenModal(this, ACTION_MODAL, {
        modal: { type: 'sidebar', size: email ? 'large' : 'medium' },
        data: {
          action,
          definition,
          form,
          culture: look.culture,
          languages: look.languages.map((language) => ({
            culture: language.culture,
            name: language.name,
            isDefault: language.isDefault,
          })),
          fieldTypes: this._types,
          actionTypes: types,
          choices: choicesOf(types),
          needs: Object.fromEntries(TRIGGERS.map((trigger) => [trigger, triggerNeed(definition, trigger)])) as Record<
            Trigger,
            TriggerNeed | null
          >,
          readOnly: !this._manage,
          check: (asked) => context.checkOf(asked),
          layouts: this._layouts.items,
          defaultLayout: this._layouts.defaultKey,
          preview: (asked, key, culture) => context.emailPreviewOf(asked, key, culture),
          test: (asked, key, started) => this.#test(asked, key, started),
          documentType: (alias) => context.documentTypeOf(alias),
          ...(focus ? { focus } : {}),
          closed: () => gone?.(),
        },
      }).catch(() => undefined);
      await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
      if (!value) {
        await this.#focusAction(key);
        return;
      }
      if ('deleted' in value) {
        if (action) await this.#delete(action);
        return;
      }
      const now = actionsOf(this.#definition());
      const kept = value.action;
      const trigger = triggerOf(kept);
      const next = keptIn(now, kept, value.at);
      const changed = this.#change(next, this.#term(action ? 'undoChanged' : 'undoAdded'));
      if (!changed) {
        await this.#focusAction(key);
        return;
      }
      const name = this.#nameOf(kept);
      if (!action) this.#announce(this.#term('added', name, this.#when(trigger, this.#definition())));
      else {
        // Another trigger or another place: where it went, as a move says it.
        const before = placeOf(now, kept.key, trigger);
        const after = placeOf(next, kept.key, trigger);
        if (triggerOf(action) !== trigger || before.at !== after.at)
          this.#announce(this.#term('moved', name, after.at + 1, after.count));
      }
      if (this.#published)
        void this.#getNotifications().then((notifications) =>
          notifications?.peek('positive', {
            data: { message: this.#term(action ? 'updatedPublished' : 'addedPublished', name) },
          }),
        );
      await this.#focusAction(kept.key);
    } finally {
      this.#editing = false;
    }
  }

  // ---- Fix it -------------------------------------------------------------------------------------------------------

  // A Fix it in the form's actions, or a link's `?action=`, made once the view shows them.
  async #fixPending() {
    if (!this._actionTypes || !this._look || !this._view) return;
    const target = this.#context?.takeFix((one) => one.kind === 'view' && one.view === 'actions');
    if (target) await this.#fix(target);
  }

  /**
   * Leads to the action a problem's path names: its card with the focus on it, and its sidebar on the box the path
   * names where it names one; Add action for a path at the list itself, or an action gone since.
   */
  async #fix(target: FixTarget) {
    if (target.kind !== 'view') return;
    const steps = parsePath(target.path) ?? [];
    const actions = actionsOf(this.#definition());
    const key = target.key ?? (typeof steps[1] === 'number' ? actions[steps[1]]?.key : undefined);
    if (!key || !actions.some((action) => action.key === key)) {
      await this.#focusAction(null);
      return;
    }
    await this.#focusAction(key);
    const setting = settingAtPath(steps.slice(2));
    if (setting.kind === 'action') return;
    // Every Fix it here came from another view: the sidebar opens once the router that showed this one stays still,
    // as a navigation's end closes it (ADR-0227).
    await routerSettled();
    if (this.isConnected) await this.#openAction(key, setting);
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  /**
   * The form check's problems in each action, by its key, for its card; a hint shows only in its sidebar, as an outcome's
   * does in its own (ADR-0185).
   */
  #flags(): ReadonlyMap<string, Flag> {
    const flags = new Map<string, Flag>();
    const checked = this._check.definition;
    if (!checked) return flags;
    const actions = actionsOf(checked);
    for (const issue of this._check.issues) {
      const steps = parsePath(issue.path);
      if (issue.severity === 'hint' || !steps || steps[0] !== 'actions' || typeof steps[1] !== 'number') continue;
      const key = actions[steps[1]]?.key;
      if (!key) continue;
      flags.set(key, flagWith(flags.get(key), issue.severity));
    }
    return flags;
  }

  // What an action does in one line, as its card says it; empty for an action whose settings say nothing yet.
  #line(action: DesignAction, names: ReturnType<typeof elementNames>): string {
    return actionLine(this.#words(), action, names);
  }

  #cards(definition: DesignDefinition, trigger: Trigger, flags: ReadonlyMap<string, Flag>): ActionCard[] {
    const look = this._look;
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    const names = elementNames(definition, look?.culture ?? '', term);
    const words = look ? inspectorRuleWords(definition, this._types, look.culture, look.languages, term) : null;
    const untranslated = look ? this.#untranslated(look) : '';
    return inTrigger(actionsOf(definition), trigger).map((action) => {
      const type = this.#typeOf(action.type);
      const line = this.#line(action, names);
      const rule = runRuleOf(action);
      const said = rule && words ? ruleWords(words, rule) : null;
      return {
        key: action.key,
        name: this.#nameOf(action),
        icon: type?.icon ?? 'icon-lightning',
        summary: line ? line : action.name?.trim() ? this.#say(type?.name) || action.type : '',
        rule: said && rule?.enabled === false ? this.#term('ruleOff', said) : said,
        on: isOn(action),
        flag: flags.get(action.key),
        untranslated:
          untranslated && actionLacks(action.settings, type?.settings ?? [], look?.culture ?? '')
            ? untranslated
            : undefined,
        testHint: testHint(type?.test, this.#term),
        run: this.#runOf(action),
      };
    });
  }

  /** The mark of an action whose texts lack the language shown, "No German text yet"; none in the default (ADR-0228). */
  #untranslated(look: CanvasLook): string {
    const standard = look.languages.find((language) => language.isDefault)?.culture ?? look.culture;
    if (look.languages.length < 2 || standard.toLowerCase() === look.culture.toLowerCase()) return '';
    let name = look.culture;
    try {
      name =
        new Intl.DisplayNames([this.localize.lang()], { type: 'language' }).of(
          look.culture.split('-')[0] ?? look.culture,
        ) ?? name;
    } catch {
      name = look.languages.find((language) => language.culture === look.culture)?.name ?? name;
    }
    return this.localize.term('jfbDesigner_untranslated', name);
  }

  // An action's last run in the reader's language, as its card says it.
  #runOf(action: DesignAction) {
    const run = this._runs.get(action.key);
    return run ? (runLine(run, action.type, new Date(), this.localize.lang(), this.#term) ?? undefined) : undefined;
  }

  // The Details of a failed or uncertain last run, with Test for who may change actions (ADR-0195).
  async #details(key: string) {
    const action = actionsOf(this.#definition()).find((one) => one.key === key);
    const run = this._runs.get(key);
    if (!action || !run) return;
    const details = runDetails(run, action.type, new Date(), this.localize.lang(), this.#term);
    if (!details) return;
    await umbOpenModal(this, ACTION_RUN_MODAL, {
      data: {
        name: this.#nameOf(action),
        details,
        webhook: action.type === 'webhook',
        ...(this._manage ? { test: (started: () => void) => this.#test(this.#definition(), key, started) } : {}),
      },
    }).catch(() => undefined);
  }

  #trigger(definition: DesignDefinition, trigger: Trigger, flags: ReadonlyMap<string, Flag>): TemplateResult {
    const need = triggerNeed(definition, trigger);
    const cards = this.#cards(definition, trigger, flags);
    const heading = this.#term(`trigger_${trigger}`);
    const when = this.#when(trigger, definition);
    const link = (need: TriggerNeed) =>
      html`<a
        href=${viewPath(window.location.pathname, 'settings')}
        @click=${(event: Event) => {
          event.preventDefault();
          followFix({ kind: 'view', view: 'settings', path: NEEDED_AT[need] }, (fix) => this.#context?.requestFix(fix));
        }}
        >${this.#term('turnOn')}</a
      >`;
    return html`<section class="trigger" aria-labelledby=${`trigger-${trigger}`}>
      <div class="trigger-head">
        <h2 id=${`trigger-${trigger}`}>${heading}</h2>
        ${need ? nothing : html`<span class="when">${when}</span>`}
      </div>
      ${
        need
          ? html`<p class="off">${this.#term(cards.length > 0 ? `held_${need}` : `needs_${need}`)} ${link(need)}</p>`
          : nothing
      }
      ${
        cards.length > 0
          ? html`<jfb-action-list
              data-trigger=${trigger}
              .cards=${cards}
              .label=${heading}
              ?read-only=${!this._manage}
              ?full=${this.#full()}
              .open=${(key: string) => void this.#openAction(key)}
              .move=${(from: number, to: number) => this.#move(trigger, from, to)}
              .toggle=${(key: string, on: boolean) => this.#toggle(key, on)}
              .request=${(key: string, request: CardRequest) => void this.#request(key, request)}
              .test=${this._manage ? (key: string, started: () => void) => this.#test(this.#definition(), key, started) : undefined}
              .details=${(key: string) => void this.#details(key)}
            ></jfb-action-list>`
          : need
            ? nothing
            : html`<p class="none">${this.#term('none')}</p>`
      }
    </section>`;
  }

  #toolbar(count: number): TemplateResult {
    if (!this._manage)
      return html`<p class="read-only">
        <uui-icon name="icon-lock" aria-hidden="true"></uui-icon><span>${this.#term('readOnly')}</span>
      </p>`;
    if (this._limit > 0 && count >= this._limit) return html`<p class="note">${this.#term('limit', this._limit)}</p>`;
    return html`<div class="toolbar">
      <uui-button
        id="add"
        look="secondary"
        label=${this.#term('addAction')}
        @click=${() => void this.#openAction(null)}
      >
        <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.#term('addAction')}
      </uui-button>
    </div>`;
  }

  override render() {
    if (this._failed)
      return html`<div class="page">
        <div class="failed" role="alert">
          <p>${this.localize.term('jfbDesigner_actionsFailed')}</p>
          <uui-button
            look="secondary"
            label=${this.localize.term('jfbSettings_retry')}
            @click=${() => void this.#read()}
          ></uui-button>
        </div>
      </div>`;
    const view = this._view;
    if (!view || !this._actionTypes || !this._look)
      return html`<div class="page">
        <div class="skeleton" aria-hidden="true"><span></span><span></span><span></span></div>
        <p class="visually-hidden" role="status">${this.localize.term('jfbDesigner_actionsLoading')}</p>
      </div>`;
    const definition = view.state.definition;
    const actions = actionsOf(definition);
    const flags = this.#flags();
    return html`<div class="page">
        ${this.#toolbar(actions.length)}
        ${
          actions.length === 0
            ? html`<p class="empty">${this.#term('empty')}</p>`
            : TRIGGERS.map((trigger) => this.#trigger(definition, trigger, flags))
        }
      </div>
      <div class="visually-hidden" role="status" aria-live="polite">${this._said}</div>`;
  }
}

if (!customElements.get('jfb-form-actions-view'))
  customElements.define('jfb-form-actions-view', FormActionsViewElement);

export { FormActionsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-actions-view': FormActionsViewElement;
  }
}
