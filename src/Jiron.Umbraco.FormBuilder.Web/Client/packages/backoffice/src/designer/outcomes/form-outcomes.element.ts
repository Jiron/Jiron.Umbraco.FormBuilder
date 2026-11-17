// After sending on the Design view (ROADMAP 10.1b and 22.1, IA 5.2, ADR-0185, ADR-0256): the form's outcomes, which
// the view places in the inspector's column beside the sheet, under the column's own heading, or under what visitors
// get, with a heading of its own, where no inspector stands beside the sheet. Above the list, where it has more than one
// outcome, the sentence on their order; each outcome is edited in a sidebar of its own. Update, Add, Delete outcome
// (with Undo in the toast) and every move are each one step of the form's Undo, and a change that changes nothing says
// nothing; on a published form a toast says when visitors get a change. The form check's problems count on each
// outcome's row and those of the list itself show above Add outcome; an outcome whose message lacks the language the
// designer shows says so. The most outcomes a form can have is read from the site.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formSettingsChoices, type FieldTypeResponseModel, type FormSettingsLimitsResponseModel } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import { issueWords, type CheckWordsContext } from '../check/check-words';
import { parsePath, type CheckSeverity } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { EditorView, FormWorkspaceContext } from '../form-workspace.context';
import { routerSettled } from '../form-views';
import { moved } from '../inspector/option-values';
import { same } from '../inspector/setting-values';
import { word, type CanvasLook } from '../preview/look';
import { elementNames, inspectorRuleWords } from '../rules/rule-names';
import { flagWith, type Flag, type Shown } from '../settings/problem-lines';
import './outcome-list.element';
import type { OutcomeListElement } from './outcome-list.element';
import {
  outcomeName,
  outcomesOf,
  settingAtPath,
  withOutcome,
  withOutcomes,
  type OutcomeSetting,
} from './outcome-model';
import { OUTCOME_MODAL } from './outcome.modal.token';

/** The form check's problems in the outcomes: by outcome, for its row, and those of the list itself. */
interface Checked {
  readonly flags: ReadonlyMap<string, Flag>;
  readonly problems: readonly Shown[];
}

const NONE_CHECKED: Checked = { flags: new Map(), problems: [] };

/**
 * The outcome an undo or a redo of the outcomes brought back or changed, by its key: the first one the list holds now
 * that it did not hold before, else the first that is not as it was; null where none is.
 */
export function changedOutcome(before: DesignDefinition, after: DesignDefinition): string | null {
  const was = outcomesOf(before);
  const now = outcomesOf(after);
  const back = now.find((outcome) => !was.some((one) => one.key === outcome.key));
  if (back) return back.key;
  return now.find((outcome, at) => !same(outcome, was[at]))?.key ?? null;
}

export class FormOutcomesElement extends UmbLitElement {
  static override properties = {
    look: { attribute: false },
    headed: { type: Boolean, reflect: true },
    _view: { state: true },
    _check: { state: true },
    _types: { state: true },
    _limits: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      /* Under the sheet it is a box of its own, headed After sending, as wide as the sheet. */
      :host([headed]) {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
      }

      h2 {
        margin: 0;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border-block-end: 1px solid var(--uui-color-border);
        font-size: var(--jfb-bo-type-title);
        font-weight: 700;
        line-height: 1.3;
      }

      .body {
        display: grid;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-4) var(--uui-size-space-5) var(--uui-size-space-6);
      }

      p {
        margin: 0;
      }

      .note {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .failed {
        display: grid;
        gap: var(--uui-size-space-3);
        justify-items: start;
      }
    `,
  ];

  /** The look the designer shows: the language the outcomes are shown and edited in, and the site's languages. */
  declare look: CanvasLook | undefined;
  /** Whether it draws its own heading, After sending: under the sheet, where no inspector's column heads it. */
  declare headed: boolean;
  declare private _view: EditorView | undefined;
  declare private _check: CheckState;
  declare private _types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The bounds of the save checks, as the site sets them; undefined until they are read. */
  declare private _limits: FormSettingsLimitsResponseModel | undefined;
  declare private _failed: boolean;

  #context: FormWorkspaceContext | undefined;
  /** The form whose bounds were read, so another form open reads them again. */
  #form: string | null = null;
  /** Whether visitors get a published version, which an outcome added or changed reaches only once it is published. */
  #published = false;
  /** Whether an outcome's sidebar is open, so a second never opens over it. */
  #editing = false;
  /** The last toast with Undo, closed by the next one: its Undo would take back another change. */
  #undoToast: { close(): void } | undefined;
  /** Settles once the bounds are read, so a Fix it waits for the list it leads to. */
  #ready: Promise<void>;
  #readyNow: () => void = () => undefined;

  constructor() {
    super();
    this.look = undefined;
    this.headed = false;
    this._check = UNCHECKED;
    this._types = {};
    this._failed = false;
    this.#ready = new Promise((done) => (this.#readyNow = done));
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.observe(context.editor, (view) => (this._view = view), 'editor');
      this.observe(context.check, (check) => (this._check = check ?? UNCHECKED), 'check');
      this.observe(context.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context.state, (state) => (this.#published = state === 'published' || state === 'pending'), 'state');
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          this.#form = unique;
          void this.#read();
        },
        'unique',
      );
    });
  }

  async #read() {
    const form = this.#form;
    if (!form) return;
    this._failed = false;
    const { data } = await tryExecute(this, formSettingsChoices({ path: { key: form } }), {
      disableNotifications: true,
    });
    // A form opened meanwhile wins over the answer for the one before it.
    if (form !== this.#form) return;
    if (!data) {
      this._failed = true;
      return;
    }
    this._limits = data.limits;
    await this.updateComplete;
    this.#readyNow();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbOutcomes_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  // A problem's weight in words, which a screen reader says before a warning or a suggestion.
  #weight = (severity: CheckSeverity) => this.localize.term(`jfbSettings_severity_${severity}`);

  #definition(): DesignDefinition {
    return this._view?.state.definition ?? { pages: [] };
  }

  #list(): OutcomeListElement | null {
    return this.shadowRoot?.querySelector('jfb-outcome-list') ?? null;
  }

  #notifications() {
    return this.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
  }

  // A language's name in the user's own: "German", for de-DE in an English backoffice.
  #languageName(culture: string): string {
    try {
      const names = new Intl.DisplayNames([this.localize.lang()], { type: 'language' });
      return names.of(culture.split('-')[0] ?? culture) ?? culture;
    } catch {
      return this.look?.languages.find((language) => language.culture === culture)?.name ?? culture;
    }
  }

  /** The mark of a message the language shown lacks, "No German text yet"; none in the default language (ADR-0228). */
  #untranslated(look: CanvasLook): string {
    const standard = look.languages.find((language) => language.isDefault)?.culture ?? look.culture;
    if (look.culture.toLowerCase() === standard.toLowerCase()) return '';
    return this.localize.term('jfbDesigner_untranslated', this.#languageName(look.culture));
  }

  // The form check's problems in the outcomes: each outcome's on its row, whose sidebar says them; the list's own
  // (a suggestion too) above Add outcome.
  #checked(): Checked {
    const checked = this._check.definition;
    if (!checked) return NONE_CHECKED;
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    const words: CheckWordsContext = {
      term,
      say: this.#say,
      types: this._types,
      names: elementNames(checked, this.look?.culture ?? '', term),
    };
    const outcomes = outcomesOf(checked);
    const flags = new Map<string, Flag>();
    const problems: Shown[] = [];
    for (const issue of this._check.issues) {
      const [top, index] = parsePath(issue.path) ?? [];
      if (top !== 'outcomes') continue;
      if (typeof index !== 'number') {
        problems.push({ text: issueWords(issue, checked, words).said, severity: issue.severity });
        continue;
      }
      const key = outcomes[index]?.key;
      if (issue.severity === 'hint' || !key) continue;
      flags.set(key, flagWith(flags.get(key), issue.severity));
    }
    return { flags, problems };
  }

  // ---- What the Design view asks --------------------------------------------------------------------------------------

  /**
   * Leads a Fix it, or a rule of the rules overview, to an outcome: its sidebar on the setting the path's steps name
   * after the outcome's; a path at the list itself, or an outcome gone since, to Add outcome. `wait` holds the sidebar
   * until the router stays still, for one that came from another view, as a navigation's end closes it (ADR-0227).
   */
  async fix(key: string | undefined, steps: readonly (string | number)[], wait: boolean) {
    await this.#ready;
    await this.updateComplete;
    const outcomes = outcomesOf(this.#definition());
    const known = key ?? (typeof steps[1] === 'number' ? outcomes[steps[1]]?.key : undefined);
    const list = this.#list();
    if (!known || !outcomes.some((outcome) => outcome.key === known)) {
      await list?.focusOutcome(null);
      return;
    }
    await list?.focusOutcome(known);
    if (wait) await routerSettled();
    if (this.isConnected) await this.#open(known, settingAtPath(steps.slice(2)));
  }

  /**
   * After an undo or a redo that changed the outcomes, the focus on the outcome it brought back or changed, else on Add
   * outcome; true where it changed them.
   */
  async focusChange(before: DesignDefinition): Promise<boolean> {
    const after = this.#definition();
    if (same(before['outcomes'], after['outcomes'])) return false;
    await this.updateComplete;
    await this.#list()?.focusOutcome(changedOutcome(before, after));
    return true;
  }

  // ---- Changes --------------------------------------------------------------------------------------------------------

  /** Makes a change of the outcomes, named for Undo; false where it changes nothing or is refused. */
  #change(definition: DesignDefinition, said: string): boolean {
    const context = this.#context;
    const view = this._view;
    if (!context || !view) return false;
    if (same(definition['outcomes'], view.state.definition['outcomes'])) return false;
    const result = context.edit({ ok: true, state: { definition, selection: view.state.selection } }, said);
    if (!result.ok) this.#list()?.announce(this.localize.term(`jfbDesigner_refusal_${result.refusal}`));
    return result.ok;
  }

  #move(from: number, to: number) {
    const definition = this.#definition();
    this.#change(withOutcomes(definition, moved(outcomesOf(definition), from, to)), this.#term('undoMoved'));
  }

  /**
   * An outcome's sidebar, or a new one's with null: Update keeps the outcome at the place the sidebar chose, Add puts a
   * new one there, and Delete outcome takes it away with Undo in the toast. On a published form a toast says when
   * visitors get a change; a change that changed nothing says nothing.
   */
  async #open(key: string | null, focus?: OutcomeSetting) {
    const context = this.#context;
    const look = this.look;
    const limits = this._limits;
    const form = this.#form;
    if (!context || !look || !limits || !form || this.#editing) return;
    const definition = this.#definition();
    const outcomes = outcomesOf(definition);
    const at = key === null ? -1 : outcomes.findIndex((one) => one.key === key);
    const outcome = outcomes[at] ?? null;
    if (key !== null && !outcome) return;
    if (key === null && limits.outcomes > 0 && outcomes.length >= limits.outcomes) return;
    const defaultMessage = word(look, 'thankYou');
    this.#editing = true;
    try {
      // The sidebar answers before it slides out, and its dialog gives the focus back to what opened it as it closes;
      // the focus set here waits until it is gone, so it stays.
      let gone: (() => void) | undefined;
      const closed = new Promise<void>((done) => (gone = done));
      const value = await umbOpenModal(this, OUTCOME_MODAL, {
        data: {
          outcome,
          number: at >= 0 ? at + 1 : outcomes.length + 1,
          count: outcomes.length,
          definition,
          form,
          culture: look.culture,
          languages: look.languages.map((language) => ({
            culture: language.culture,
            name: language.name,
            isDefault: language.isDefault,
          })),
          types: this._types,
          defaultMessage,
          htmlLength: limits.htmlLength,
          check: (asked) => context.checkOf(asked),
          preview: (asked, shown, culture) => context.previewOf(asked, shown, culture),
          stylesheets: look.stylesheets,
          ...(focus ? { focus } : {}),
          closed: () => gone?.(),
        },
      }).catch(() => undefined);
      await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
      const list = this.#list();
      if (!value) {
        await list?.focusOutcome(key);
        return;
      }
      const now = this.#definition();
      const before = outcomesOf(now);
      if ('deleted' in value) {
        const left = before.filter((one) => one.key !== key);
        const deleted = this.#change(withOutcomes(now, left), this.#term('undoDeleted'));
        if (deleted && key !== null) void this.#offerUndo(this.#term('deleted'), key);
        // The list shows the change before the focus goes to an outcome in it.
        await this.updateComplete;
        await list?.focusOutcome(left[Math.max(at, 0)]?.key ?? left.at(-1)?.key ?? null);
        return;
      }
      // The outcome at the place the sidebar chose: a changed one taken from where it was first.
      const others = before.filter((one) => one.key !== value.outcome.key);
      const next = withOutcome(others, value.outcome, Math.min(value.at, others.length));
      const changed = this.#change(withOutcomes(now, next), this.#term(outcome ? 'undoChanged' : 'undoAdded'));
      if (!changed) {
        await list?.focusOutcome(key);
        return;
      }
      const place = next.findIndex((one) => one.key === value.outcome.key);
      const name = outcomeName(value.outcome, look.culture, defaultMessage, new Map(), this.#term);
      if (!outcome) list?.announce(this.#term('added', name, place + 1, next.length));
      else if (place !== at) list?.announce(this.#term('moved', name, place + 1, next.length));
      if (this.#published)
        void this.#notifications().then((notifications) =>
          notifications?.peek('positive', {
            data: { message: this.#term(outcome ? 'updatedPublished' : 'addedPublished') },
          }),
        );
      await this.updateComplete;
      await list?.focusOutcome(value.outcome.key);
    } finally {
      this.#editing = false;
    }
  }

  // A toast with Undo for the outcome just deleted, closed by the next one: its Undo would take back another. Undo puts
  // the focus on the outcome back in the list, as the toast it was on is gone.
  async #offerUndo(message: string, restored: string) {
    const made = this._view?.state;
    this.#undoToast?.close();
    const data: UndoNotificationData = {
      message,
      undoLabel: this.localize.term('jfbDesigner_undo'),
      undo: async () => {
        const context = this.#context;
        if (!context || this._view?.state !== made) return;
        const said = context.undo();
        if (said === undefined) return;
        this.#list()?.announce(this.localize.term('jfbDesigner_saidUndone', said));
        await this.updateComplete;
        await this.#list()?.focusOutcome(restored);
      },
    };
    this.#undoToast = (await this.#notifications())?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  override render() {
    const look = this.look;
    const heading = this.headed ? html`<h2>${this.localize.term('jfbDesigner_afterSending')}</h2>` : nothing;
    if (this._failed)
      return html`${heading}
        <div class="body">
          <div class="failed" role="alert">
            <p>${this.#term('failed')}</p>
            <uui-button look="secondary" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
          </div>
        </div>`;
    const limits = this._limits;
    if (!look || !limits)
      return html`${heading}
        <div class="body"><uui-loader></uui-loader></div>`;
    const definition = this.#definition();
    const outcomes = outcomesOf(definition);
    const checked = this.#checked();
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    return html`${heading}
      <div class="body">
        ${outcomes.length > 1 ? html`<p class="note">${this.#term('listHelp')}</p>` : nothing}
        <jfb-outcome-list
          .outcomes=${outcomes}
          .culture=${look.culture}
          .untranslated=${this.#untranslated(look)}
          .defaultMessage=${word(look, 'thankYou')}
          .words=${inspectorRuleWords(definition, this._types, look.culture, look.languages, term)}
          .flags=${checked.flags}
          .problems=${checked.problems}
          .weight=${this.#weight}
          .limit=${limits.outcomes}
          .open=${(key: string | null) => void this.#open(key)}
          .move=${(from: number, to: number) => this.#move(from, to)}
        ></jfb-outcome-list>
      </div>`;
  }
}

if (!customElements.get('jfb-form-outcomes')) customElements.define('jfb-form-outcomes', FormOutcomesElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-outcomes': FormOutcomesElement;
  }
}
