// The form's Settings view (ROADMAP 10.1a, 10.1b, IA 5.2, ADR-0184, ADR-0185): one page of the groups of section 4.3.4,
// Submission and After sending open and each other group closed with a line that says what is set, as this browser left
// them. After sending lists the outcomes, each edited in a sidebar of its own. Each setting changes the
// draft as an inspector box does: what is typed into one box is one step to undo, a value that can't be kept is said
// under its box once the box is left, and outside a text box Ctrl+Z and Ctrl+Y undo and redo. The form check's
// problems in the settings show under their setting, and its Fix it to one opens the group, and More options where the
// setting is there, with the focus on the setting. The site's defaults, labels, time zone, themes, member groups and
// captcha providers and the bounds of the save checks come from the server; the texts are shown in the Design view's
// language.
import { html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  captchaProviderList,
  formSettingsChoices,
  type CaptchaProviderResponseModel,
  type FieldSettingResponseModel,
  type FieldTypeResponseModel,
  type FormSettingsChoicesResponseModel,
} from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { issueWords, type CheckWordsContext } from '../check/check-words';
import { parsePath, type CheckSeverity, type FixTarget } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { EditorView, FormWorkspaceContext } from '../form-workspace.context';
import { routerSettled } from '../form-views';
import { boxValue } from '../inspector/box-value';
import { controlStyles } from '../inspector/control.styles';
import { focusableIn } from '../inspector/item-focus';
import { moved } from '../inspector/option-values';
import '../inspector/merge-tag-picker.element';
import { withTag } from '../inspector/merge-tags';
import '../inspector/rich-text.element';
import { problemText, settingEditor, type EditorContext, type Found } from '../inspector/setting-editor';
import { ownText, same, withText, type Parsed, type Problem } from '../inspector/setting-values';
import { textIn } from '../preview/field-models';
import { readLook, word, type CanvasLook } from '../preview/look';
import { elementNames, inspectorRuleWords } from '../rules/rule-names';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../../ui/undo-notification.element';
import {
  DOT,
  GROUPS,
  anchorOf,
  captchaBox,
  choiceValue,
  flagIn,
  groupOf,
  lineIn,
  namesIn,
  numberIn,
  objectIn,
  parseNames,
  parseWhole,
  protectionOf,
  referenceOf,
  settingAt,
  siteProtection,
  summaryOf,
  textsIn,
  themeName,
  verificationFields,
  withProtection,
  withSettingAt,
  wordIn,
  type Anchor,
  type GroupId,
  type ProtectionChoice,
} from './settings-model';
import { choiceStyles } from './choice.styles';
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
import { described, flagLine, flagWith, NO_FLAG, whyLine, whys, type Flag, type Shown } from './problem-lines';
import { settingsStyles } from './settings.styles';
import { isInstant, toInstant, toLocal, zoneOf } from './time-zone';

const OPEN_KEY = 'jfb.designer.settings.open';

/** The groups this browser left open; Submission and After sending at first (IA 5.2). */
export function readOpenGroups(): ReadonlySet<GroupId> {
  try {
    const kept: unknown = JSON.parse(localStorage.getItem(OPEN_KEY) ?? 'null');
    if (Array.isArray(kept)) return new Set(GROUPS.filter((group) => kept.includes(group)));
  } catch {
    // A browser that keeps nothing opens Submission and After sending.
  }
  return new Set<GroupId>(['submission', 'after']);
}

function keepOpenGroups(open: ReadonlySet<GroupId>) {
  try {
    localStorage.setItem(OPEN_KEY, JSON.stringify([...open]));
  } catch {
    // Kept for this page only.
  }
}

/** The boxes whose keys are no typing: Ctrl+Z there is the form's Undo, not the box's own. */
const NOT_TEXT: ReadonlySet<string> = new Set([
  'checkbox',
  'radio',
  'button',
  'submit',
  'reset',
  'range',
  'color',
  'file',
]);

/** The defaults a definition's settings take where it holds none (FormSettings in Core). */
const VERIFICATION_DAYS = 7;
/** The days Store entries "For a number of days" starts with. */
const STORE_DAYS = 30;
/** The most days entries are kept, as the save checks hold them, until the site's answer says. */
const MOST_DAYS = 3_650;
/** The days of a state the site keeps for good (ADR-0222). */
const FOR_GOOD = 0;
/** The states whose days a form may set, in the order the view shows them. */
const RETENTION_STATES = ['submitted', 'approved', 'rejected', 'spam'] as const;
const REFERENCE_DIGITS = 5;
/** The most entries a form can be limited to, as the definition stores a number. */
const MOST_ENTRIES = 2_147_483_647;
/** The number the reference's example shows. */
const EXAMPLE_NUMBER = 42;
/** What Submission's More options hold: the page buttons, the summary page, the progress and the reference. */
const MORE_OPTIONS = 4;

/** The form check's problems as this view shows them: by the box they show under, and counted where they hide. */
interface Checked {
  readonly found: ReadonlyMap<string, readonly Shown[]>;
  /** The problems of each group, for its line while it is closed. */
  readonly flagged: ReadonlyMap<GroupId, Flag>;
  /** The problems under More options, for its toggle while it is closed. */
  readonly more: Flag;
  /** The problems in each outcome, by its key, for its row in After sending. */
  readonly outcomes: ReadonlyMap<string, Flag>;
}

const NONE_CHECKED: Checked = { found: new Map(), flagged: new Map(), more: NO_FLAG, outcomes: new Map() };

/** A button's label in the definition, and the visitor's word for it. */
type ButtonLabel = 'submitLabel' | 'nextLabel' | 'backLabel';

export class FormSettingsViewElement extends UmbLitElement {
  static override properties = {
    _view: { state: true },
    _choices: { state: true },
    _providers: { state: true },
    _look: { state: true },
    _failed: { state: true },
    _check: { state: true },
    _types: { state: true },
    _open: { state: true },
    _more: { state: true },
    _problems: { state: true },
    _said: { state: true },
  };

  static override styles = [jfbBackofficeTokens, jfbVisuallyHidden, controlStyles, choiceStyles, settingsStyles];

  declare private _view: EditorView | undefined;
  declare private _choices: FormSettingsChoicesResponseModel | undefined;
  declare private _providers: readonly CaptchaProviderResponseModel[] | undefined;
  declare private _look: CanvasLook | undefined;
  declare private _failed: boolean;
  declare private _check: CheckState;
  declare private _types: Readonly<Record<string, FieldTypeResponseModel>>;
  declare private _open: ReadonlySet<GroupId>;
  declare private _more: boolean;
  /** Why a box's last value couldn't be kept, by the box's id, until it can, or Undo or another language comes. */
  declare private _problems: ReadonlyMap<string, string>;
  declare private _said: string;

  #context: FormWorkspaceContext | undefined;
  /** The form whose choices were read, so another form open reads them again. */
  #form: string | null = null;
  /** The language asked for, which the look read last may not be in yet; null for the site's default. */
  #culture: string | null = null;
  /** Counts the runs of typing: a box left, or any other change, ends one, so typing again is a step of its own. */
  #visit = 0;
  /** The form check's problems as this render placed them. */
  #checkedNow: Checked = NONE_CHECKED;
  /** Whether visitors get a published version, which an outcome added or changed reaches only once it is published. */
  #published = false;
  /** Whether an outcome's sidebar is open, so a second never opens over it. */
  #editingOutcome = false;
  /** The last toast with Undo, closed by the next one: its Undo would take back another change. */
  #undoToast: { close(): void } | undefined;

  constructor() {
    super();
    this._failed = false;
    this._check = UNCHECKED;
    this._types = {};
    this._open = readOpenGroups();
    this._more = false;
    this._problems = new Map();
    this._said = '';
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      if (!context) return;
      this.#culture = context.getLanguage();
      this.observe(context.editor, (view) => (this._view = view), 'editor');
      this.observe(context.check, (check) => (this._check = check ?? UNCHECKED), 'check');
      this.observe(context.fieldTypes, (types) => (this._types = types ?? {}), 'types');
      this.observe(context.state, (state) => (this.#published = state === 'published' || state === 'pending'), 'state');
      // The choices are the form's: they are read once the workspace has the form, again for another.
      this.observe(
        context.unique,
        (unique) => {
          if (!unique || unique === this.#form) return;
          this.#form = unique;
          void this.#read();
        },
        'unique',
      );
      // A Fix it asked from the publish dialog while this view is shown, or from the Design view that led here.
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
    const culture = this.#culture;
    const form = this.#form;
    if (!form) return;
    const [choices, providers, look] = await Promise.all([
      tryExecute(this, formSettingsChoices({ path: { key: form } }), { disableNotifications: true }),
      tryExecute(this, captchaProviderList(), { disableNotifications: true }),
      readLook(this, culture ?? undefined),
    ]);
    // A language or a form chosen meanwhile wins over the answer for the one before it.
    if (culture !== this.#culture || form !== this.#form) return;
    if (!choices.data || !providers.data || !look) {
      this._failed = true;
      return;
    }
    this._choices = choices.data;
    this._providers = providers.data;
    this._look = look;
    await this.updateComplete;
    await this.#fixPending();
  }

  async #showLanguage(culture: string) {
    this.#culture = culture;
    this.#context?.setLanguage(culture);
    this._problems = new Map();
    const look = await readLook(this, culture);
    if (culture !== this.#culture) return;
    if (look) this._look = look;
    else this._failed = true;
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbSettings_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  #number = (value: number) => this.localize.number(value);

  #problemWords(problem: Problem): string {
    return problemText((key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args), problem);
  }

  #zone(): string {
    return zoneOf(this._choices?.timeZone);
  }

  #date = (instant: string) =>
    this.localize.date(instant, { dateStyle: 'medium', timeStyle: 'short', timeZone: this.#zone() });

  #defaultCulture(): string {
    const look = this._look;
    return look?.languages.find((language) => language.isDefault)?.culture ?? look?.culture ?? '';
  }

  #isDefault(): boolean {
    return (this._look?.culture ?? '').toLowerCase() === this.#defaultCulture().toLowerCase();
  }

  // A language's name in the user's own: "German", for de-DE in an English backoffice.
  #languageName(culture: string): string {
    try {
      const names = new Intl.DisplayNames([this.localize.lang()], { type: 'language' });
      return names.of(culture.split('-')[0] ?? culture) ?? culture;
    } catch {
      return this._look?.languages.find((language) => language.culture === culture)?.name ?? culture;
    }
  }

  #announce(text: string) {
    // The same words twice in a row are said again: the live region hears a change.
    this._said = this._said === text ? `${text} ` : text;
  }

  // ---- Changes ------------------------------------------------------------------------------------------------------

  #definition(): DesignDefinition {
    return this._view?.state.definition ?? { pages: [] };
  }

  #problem(id: string, text: string | null) {
    if ((this._problems.get(id) ?? null) === text) return;
    const next = new Map(this._problems);
    if (text === null) next.delete(id);
    else next.set(id, text);
    this._problems = next;
  }

  /**
   * Makes a change of the draft, named by the setting's label for Undo: `typed` names the box typed in, whose changes
   * until the box is left are one step. A change that changes nothing is none.
   */
  #change(definition: DesignDefinition, name: string, typed?: string) {
    const context = this.#context;
    const view = this._view;
    if (!context || !view) return;
    const merge = typed === undefined ? undefined : `settings:${typed}:${this.#visit}`;
    if (typed === undefined) this.#visit++;
    if (same(definition['settings'], view.state.definition['settings'])) return;
    const result = context.edit(
      { ok: true, state: { definition, selection: view.state.selection } },
      this.localize.term('jfbInspector_saidChanged', name),
      merge,
    );
    if (!result.ok) this.#announce(this.localize.term(`jfbDesigner_refusal_${result.refusal}`));
  }

  /** Sets one setting, at its steps under `settings`; undefined takes it away. */
  #set(steps: readonly string[], value: unknown, name: string, typed?: string) {
    this.#change(withSettingAt(this.#definition(), steps, value), name, typed);
  }

  #travel(way: 'undo' | 'redo') {
    const context = this.#context;
    if (!context) return;
    const said = way === 'undo' ? context.undo() : context.redo();
    if (said === undefined) return;
    this._problems = new Map();
    this.#announce(this.localize.term(way === 'undo' ? 'jfbDesigner_saidUndone' : 'jfbDesigner_saidRedone', said));
  }

  #onKey(event: KeyboardEvent) {
    const target = event.composedPath()[0];
    // A box that takes text has its own Undo.
    const entry =
      (target instanceof HTMLInputElement && !NOT_TEXT.has(target.type)) ||
      target instanceof HTMLTextAreaElement ||
      (target instanceof HTMLElement && target.isContentEditable);
    if (entry || !(event.ctrlKey || event.metaKey) || event.altKey) return;
    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) this.#travel('undo');
    else if (key === 'y' || (key === 'z' && event.shiftKey)) this.#travel('redo');
    else return;
    event.preventDefault();
  }

  #toggle(group: GroupId) {
    const next = new Set(this._open);
    if (next.has(group)) next.delete(group);
    else next.add(group);
    this._open = next;
    keepOpenGroups(next);
  }

  // ---- Fix it -------------------------------------------------------------------------------------------------------

  // A Fix it in the form's settings, made once the view shows them.
  async #fixPending() {
    if (!this._choices || !this._providers || !this._look || !this._view) return;
    const target = this.#context?.takeFix((one) => one.kind === 'view' && one.view === 'settings');
    if (target) await this.#fix(target);
  }

  /**
   * Opens the group of the setting a problem's path names, and More options where it is there, and puts the focus on
   * the setting's box, in the language of its text; on the first group's header for a path the view doesn't show.
   */
  async #fix(target: FixTarget) {
    if (target.kind !== 'view') return;
    const steps = parsePath(target.path);
    if (steps?.[0] === 'outcomes') {
      await this.#fixOutcome(target.key, steps);
      return;
    }
    const anchor = anchorOf(target.path);
    const root = this.shadowRoot;
    if (!anchor) {
      await this.updateComplete;
      root?.querySelector<HTMLElement>('.group-head button')?.focus();
      return;
    }
    const culture = this.#languageOf(target.path);
    if (culture !== null) await this.#showLanguage(culture);
    if (!this._open.has(anchor.group)) this.#toggle(anchor.group);
    if (anchor.more) this._more = true;
    await this.updateComplete;
    for (const id of [this.#boxOf(anchor, this.#definition()), ...anchor.boxes]) {
      const box = root?.getElementById(id);
      if (!box) continue;
      const focus =
        box.localName === 'fieldset'
          ? (box.querySelector<HTMLElement>('input:checked') ?? focusableIn(box))
          : focusableIn(box);
      if (!focus) continue;
      focus.focus();
      focus.scrollIntoView?.({ block: 'center' });
      return;
    }
    root?.getElementById(`group-${anchor.group}`)?.focus();
  }

  /**
   * A Fix it in an outcome, or the canvas's Change, leads to it: After sending open, the outcome's sidebar on the setting
   * its path names, in the language of its message; a path at the list itself, or an outcome gone since, to Add outcome.
   */
  async #fixOutcome(key: string | undefined, steps: readonly (string | number)[]) {
    if (!this._open.has('after')) this.#toggle('after');
    const outcomes = outcomesOf(this.#definition());
    const known = key ?? (typeof steps[1] === 'number' ? outcomes[steps[1]]?.key : undefined);
    const [, , property, language] = steps;
    const culture = property === 'message' ? this.#languageAt(language) : null;
    if (culture !== null) await this.#showLanguage(culture);
    await this.updateComplete;
    const list = this.#list();
    if (!known || !outcomes.some((outcome) => outcome.key === known)) {
      await list?.focusOutcome(null);
      return;
    }
    await list?.focusOutcome(known);
    // The Fix it, or the canvas's Change, came from another view: the sidebar opens once the router that showed this one
    // stays still, as a navigation's end closes it (ADR-0227).
    await routerSettled();
    if (this.isConnected) await this.#openOutcome(known, settingAtPath(steps.slice(2)));
  }

  // The language of the text a path names, on a site with more than one, where the view shows another; null otherwise.
  #languageOf(path: string): string | null {
    const steps = parsePath(path) ?? [];
    const [, group, name, language] = steps;
    const texts =
      (group === 'submission' && (name === 'submitLabel' || name === 'nextLabel' || name === 'backLabel')) ||
      (group === 'availability' && name === 'closedMessage');
    return texts ? this.#languageAt(language) : null;
  }

  // The site's language a text's key names ('' or none for the default), where the view shows another; null otherwise.
  #languageAt(language: string | number | undefined): string | null {
    const languages = this._look?.languages ?? [];
    if (languages.length < 2) return null;
    const wanted =
      typeof language === 'string' && language !== ''
        ? languages.find((one) => one.culture.toLowerCase() === language.toLowerCase())
        : languages.find((one) => one.isDefault);
    if (!wanted || wanted.culture.toLowerCase() === this._look?.culture.toLowerCase()) return null;
    return wanted.culture;
  }

  // ---- Problems -----------------------------------------------------------------------------------------------------

  /**
   * The box a setting shows under, of those the view draws now: a box drawn only with another setting's choice (the
   * member groups, verification's field and days, a provider's own setting, the days of storage) falls back to that
   * setting's.
   */
  #boxOf(anchor: Anchor, definition: DesignDefinition): string {
    const box = anchor.boxes[0] ?? '';
    if (box === 'member-groups' && wordIn(groupOf(definition, 'availability'), 'members', 'anyone') !== 'groups')
      return 'access';
    if (
      (box === 'verification-field' || box === 'verification-days') &&
      !objectIn(groupOf(definition, 'submission'), 'verification')
    )
      return 'verification';
    if (box === 'store-days' && wordIn(groupOf(definition, 'submission'), 'store', 'always') !== 'days') return 'store';
    if (box.startsWith('setting-captcha-')) {
      const choice = protectionOf(definition);
      const provider =
        choice.kind === 'captcha' ? this._providers?.find((one) => one.name === choice.provider) : undefined;
      if (!provider?.settings.some((setting) => captchaBox(setting.name) === box)) return 'protection';
    }
    return box;
  }

  // The form check's problems by the box they show under, and how many hide in each closed group and More options.
  #checked(definition: DesignDefinition): Checked {
    const checked = this._check.definition;
    if (!checked) return NONE_CHECKED;
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    const words: CheckWordsContext = {
      term,
      say: this.#say,
      types: this._types,
      names: elementNames(checked, this._look?.culture ?? '', term),
    };
    const found = new Map<string, Shown[]>();
    const flagged = new Map<GroupId, Flag>();
    const outcomes = new Map<string, Flag>();
    const checkedOutcomes = outcomesOf(checked);
    let more = NO_FLAG;
    for (const issue of this._check.issues) {
      // A provider without its keys says so on its own line among the choices.
      if (issue.code === 'captcha_not_configured') continue;
      const anchor = anchorOf(issue.path);
      if (!anchor) continue;
      // An outcome's problems count on its row, whose sidebar says them, and on After sending's line while it is closed.
      const [top, index] = parsePath(issue.path) ?? [];
      if (top === 'outcomes' && typeof index === 'number') {
        const key = checkedOutcomes[index]?.key;
        if (issue.severity === 'hint' || !key) continue;
        outcomes.set(key, flagWith(outcomes.get(key), issue.severity));
        flagged.set('after', flagWith(flagged.get('after'), issue.severity));
        continue;
      }
      const box = this.#boxOf(anchor, definition);
      found.set(box, [
        ...(found.get(box) ?? []),
        { text: issueWords(issue, checked, words).said, severity: issue.severity },
      ]);
      if (issue.severity === 'hint') continue;
      flagged.set(anchor.group, flagWith(flagged.get(anchor.group), issue.severity));
      if (anchor.more) more = flagWith(more, issue.severity);
    }
    return { found, flagged, more, outcomes };
  }

  #problemsAt(id: string): readonly Shown[] {
    const typed = this._problems.get(id);
    return [...(typed ? [{ text: typed, severity: 'error' as const }] : []), ...(this.#checkedNow.found.get(id) ?? [])];
  }

  // A problem's weight in words, which a screen reader says before a warning or a suggestion.
  #weight = (severity: CheckSeverity) => this.#term(`severity_${severity}`);

  #whys(id: string, problems: readonly Shown[]) {
    return whys(id, problems, this.#weight);
  }

  #described(id: string, help: boolean, problems: readonly Shown[], besides: readonly string[] = []) {
    return described(id, help, problems, besides);
  }

  // ---- Items --------------------------------------------------------------------------------------------------------

  /** A setting's label, help and problems around its box, and what goes under it ("Insert merge tag"). */
  #frame(
    id: string,
    label: string,
    help: string | null,
    box: (described: string | undefined, invalid: boolean) => TemplateResult,
    options: { readonly after?: TemplateResult | typeof nothing; readonly besides?: readonly string[] } = {},
  ): TemplateResult {
    const problems = this.#problemsAt(id);
    const invalid = problems.some((problem) => problem.severity === 'error');
    return html`<div class="item">
      <label class="label" for=${id}>${label}</label>
      ${help ? html`<p class="help" id=${`${id}-help`}>${help}</p>` : nothing}
      ${box(this.#described(id, help !== null, problems, options.besides), invalid)} ${this.#whys(id, problems)}
      ${options.after ?? nothing}
    </div>`;
  }

  #switch(
    id: string,
    label: string,
    help: string | null,
    on: boolean,
    change: (on: boolean) => void,
    disabled = false,
  ): TemplateResult {
    const problems = this.#problemsAt(id);
    return html`<div class="item">
      <div class="item-switch">
        <input
          type="checkbox"
          role="switch"
          id=${id}
          .checked=${live(on)}
          ?disabled=${disabled}
          aria-describedby=${this.#described(id, help !== null, problems) ?? nothing}
          @change=${(event: Event) => change((event.target as HTMLInputElement).checked)}
        />
        <label class="label" for=${id}>${label}</label>
      </div>
      ${help ? html`<p class="help" id=${`${id}-help`}>${help}</p>` : nothing} ${this.#whys(id, problems)}
    </div>`;
  }

  // A choice of the setting's words; its value follows the draft, through Undo and Redo too.
  #select(
    id: string,
    label: string,
    help: string | null,
    value: string,
    options: readonly { readonly value: string; readonly label: string; readonly disabled?: boolean }[],
    change: (value: string) => void,
  ): TemplateResult {
    return this.#frame(
      id,
      label,
      help,
      (described, invalid) =>
        html`<select
          id=${id}
          class="short"
          .value=${live(value)}
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          @change=${(event: Event) => change((event.target as HTMLSelectElement).value)}
        >
          ${options.map(
            (option) =>
              html`<option
                value=${option.value}
                ?selected=${option.value === value}
                ?disabled=${option.disabled ?? false}
              >
                ${option.label}
              </option>`,
          )}
        </select>`,
    );
  }

  /**
   * A box for a whole number: an empty one leaves the setting to `empty`, one out of bounds says why once it is left. A
   * unit, such as days, stands after the box, and a placeholder says what an empty box stands for.
   */
  #whole(
    id: string,
    label: string,
    help: string | null,
    value: number | undefined,
    least: number,
    most: number,
    write: (value: number | undefined, typed: boolean) => void,
    empty: 'none' | 'refused' = 'none',
    options: { readonly unit?: string; readonly placeholder?: string } = {},
  ): TemplateResult {
    const typed = (raw: string, done: boolean) => {
      const parsed = parseWhole(raw, least, most);
      if (parsed === null || (parsed === undefined && empty === 'refused')) {
        if (done) this.#problem(id, this.#term('wholeNumber', this.#number(least), this.#number(most)));
        return;
      }
      this.#problem(id, null);
      write(parsed, !done);
    };
    return this.#frame(id, label, help, (described, invalid) => {
      const unit = options.unit ? `${id}-unit` : undefined;
      const box = html`<input
        type="text"
        id=${id}
        class="number"
        inputmode="numeric"
        autocomplete="off"
        placeholder=${options.placeholder ?? nothing}
        aria-describedby=${[described, unit].filter(Boolean).join(' ') || nothing}
        aria-invalid=${invalid ? 'true' : 'false'}
        .value=${boxValue(value === undefined ? '' : String(value), this._problems.has(id))}
        @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
        @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
      />`;
      return unit ? html`<div class="with-unit">${box}<span id=${unit}>${options.unit}</span></div>` : box;
    });
  }

  /** A box for a line of text, at most `limit` characters; `write` gets it without its spaces at the ends. */
  #line(
    id: string,
    label: string,
    help: string | null,
    value: string,
    limit: number,
    write: (text: string, typed: boolean) => void,
    options: {
      readonly code?: boolean;
      readonly wide?: boolean;
      readonly placeholder?: string;
      readonly lang?: string;
      readonly after?: TemplateResult | typeof nothing;
    } = {},
  ): TemplateResult {
    const typed = (raw: string, done: boolean) => {
      const text = raw.trim();
      if (text.length > limit) {
        if (done) this.#problem(id, this.#problemWords({ kind: 'tooLong', limit }));
        return;
      }
      this.#problem(id, null);
      write(text, !done);
    };
    const classes = [options.code ? 'code' : '', options.wide ? '' : 'short'].filter(Boolean).join(' ');
    return this.#frame(
      id,
      label,
      help,
      (described, invalid) =>
        html`<input
          type="text"
          id=${id}
          class=${classes || nothing}
          lang=${options.lang || nothing}
          spellcheck=${options.code ? 'false' : nothing}
          autocomplete="off"
          placeholder=${options.placeholder || nothing}
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          .value=${boxValue(value, this._problems.has(id))}
          @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
          @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
        />`,
      options.after === undefined ? {} : { after: options.after },
    );
  }

  /** What visitors get for a button the form labels not itself: the site's label in the language shown, else the word. */
  #standardLabel(name: ButtonLabel, visitorWord: string): string {
    const site = this._choices?.defaults[name] ?? undefined;
    return textIn(site, this._look?.culture ?? '') || visitorWord;
  }

  /** A button's label in the language shown, what visitors get without one as its placeholder. */
  #buttonLabel(id: string, name: ButtonLabel, label: string, help: string | null, visitorWord: string) {
    const look = this._look;
    if (!look) return nothing;
    const texts = textsIn(groupOf(this.#definition(), 'submission'), name);
    const isDefault = this.#isDefault();
    const standard = this.#standardLabel(name, visitorWord);
    return this.#line(
      id,
      label,
      help,
      isDefault ? textIn(texts, look.culture) : ownText(texts, look.culture),
      this._choices?.limits.lineLength ?? 0,
      (text, typed) =>
        this.#set(
          ['submission', name],
          withText(textsIn(groupOf(this.#definition(), 'submission'), name), look.culture, text, isDefault),
          label,
          typed ? id : undefined,
        ),
      {
        placeholder: isDefault ? standard : textIn(texts, this.#defaultCulture()) || standard,
        lang: look.culture,
      },
    );
  }

  // ---- Submission ---------------------------------------------------------------------------------------------------

  #submission(definition: DesignDefinition, look: CanvasLook): TemplateResult {
    const submission = groupOf(definition, 'submission');
    const store = wordIn(submission, 'store', 'always');
    const days = numberIn(submission['storeDays']);
    const title = this.#term('entryTitle');
    const hidden = this._more ? NO_FLAG : this.#checkedNow.more;
    return html`
      ${this.#buttonLabel(
        'submit-label',
        'submitLabel',
        this.#term('submitLabel'),
        this.#term('submitLabelHelp'),
        word(look, 'send'),
      )}
      ${this.#store(definition, store, days)}
      ${this.#switch(
        'approval',
        this.#term('approval'),
        this.#term('approvalHelp'),
        wordIn(submission, 'approval', 'automatic') === 'manual',
        (on) => this.#set(['submission', 'approval'], on ? 'manual' : 'automatic', this.#term('approval')),
      )}
      ${this.#verification(definition, look)}
      ${this.#line(
        'entry-title',
        title,
        this.#term('entryTitleHelp'),
        lineIn(submission, 'entryTitle'),
        this._choices?.limits.lineLength ?? 0,
        (text, typed) =>
          this.#set(['submission', 'entryTitle'], text || undefined, title, typed ? 'entry-title' : undefined),
        { wide: true, after: this.#tagPicker('entry-title') },
      )}
      <button
        type="button"
        class="more-toggle"
        aria-expanded=${this._more ? 'true' : 'false'}
        aria-controls=${this._more ? 'submission-more' : nothing}
        @click=${() => (this._more = !this._more)}
      >
        <uui-icon name=${this._more ? 'icon-navigation-down' : 'icon-navigation-right'} aria-hidden="true"></uui-icon>
        <span
          >${
            this._more
              ? this.localize.term('jfbInspector_fewerOptions')
              : this.localize.term('jfbInspector_moreOptions', MORE_OPTIONS)
          }</span
        >
        ${flagLine(hidden, (count) => this.#term('problems', count))}
      </button>
      ${
        this._more
          ? html`<div class="more-body" id="submission-more">${this.#moreOptions(definition, look)}</div>`
          : nothing
      }
    `;
  }

  // Store entries, and for a number of days the days under it (ADR-0222): one template whatever the choice, so the list
  // keeps the focus as it changes. Choosing it starts at STORE_DAYS.
  #store(definition: DesignDefinition, store: string, days: number | undefined): TemplateResult {
    const label = this.#term('store');
    const daysLabel = this.#term('storeDays');
    const chosen = this.#select(
      'store',
      label,
      this.#term('storeHelp'),
      store,
      [
        { value: 'always', label: this.#term('store_always') },
        { value: 'days', label: this.#term('store_daysChoice') },
        { value: 'never', label: this.#term('store_never') },
      ],
      (value) =>
        this.#change(
          withSettingAt(
            withSettingAt(definition, ['submission', 'store'], value),
            ['submission', 'storeDays'],
            value === 'days' ? (days ?? STORE_DAYS) : undefined,
          ),
          label,
        ),
    );
    return html`<div class="item with-detail">
      ${chosen}
      ${
        store === 'days'
          ? html`<div class="detail">
              ${this.#whole(
                'store-days',
                daysLabel,
                this.#term('storeDaysHelp'),
                days,
                1,
                this._choices?.limits.retentionDays ?? MOST_DAYS,
                (value, typed) =>
                  this.#set(['submission', 'storeDays'], value, daysLabel, typed ? 'store-days' : undefined),
                'refused',
                { unit: this.#term('daysUnit') },
              )}
            </div>`
          : nothing
      }
    </div>`;
  }

  // Email verification, and once it is on the field it confirms and the link's days, under it: one template whether
  // on or off, so the switch keeps the focus as it turns.
  #verification(definition: DesignDefinition, look: CanvasLook): TemplateResult {
    const label = this.#term('verification');
    const verification = objectIn(groupOf(definition, 'submission'), 'verification');
    const fields = verificationFields(definition);
    const switched = this.#switch(
      'verification',
      label,
      fields.length === 0 && !verification
        ? `${this.#term('verificationHelp')} ${this.#term('verificationNeedsField')}`
        : this.#term('verificationHelp'),
      verification !== undefined,
      (on) => {
        const first = fields[0];
        this.#set(
          ['submission', 'verification'],
          on && first ? { emailField: first.key, days: VERIFICATION_DAYS } : undefined,
          label,
        );
      },
      fields.length === 0 && !verification,
    );
    return html`<div class="item with-detail">
      ${switched}
      ${verification ? html`<div class="detail">${this.#verifying(definition, look, verification)}</div>` : nothing}
    </div>`;
  }

  #verifying(
    definition: DesignDefinition,
    look: CanvasLook,
    verification: Readonly<Record<string, unknown>>,
  ): TemplateResult {
    const fields = verificationFields(definition);
    const chosen = lineIn(verification, 'emailField');
    const names = elementNames(definition, look.culture, (key, ...args) => this.localize.term(key, ...args));
    const fieldLabel = this.#term('verificationField');
    const daysLabel = this.#term('verificationDays');
    return html`
      ${this.#select(
        'verification-field',
        fieldLabel,
        null,
        chosen,
        [
          ...fields.map((field) => ({ value: field.key, label: names[field.key] ?? field.key })),
          // A field it can't confirm stays chosen, by its name, until another; the form check says why.
          ...(fields.some((field) => field.key === chosen)
            ? []
            : [{ value: chosen, label: names[chosen] ?? this.#term('verificationFieldGone'), disabled: true }]),
        ],
        (key) => this.#set(['submission', 'verification', 'emailField'], key, fieldLabel),
      )}
      ${this.#whole(
        'verification-days',
        daysLabel,
        null,
        numberIn(verification['days']) ?? VERIFICATION_DAYS,
        1,
        this._choices?.limits.verificationDays ?? VERIFICATION_DAYS,
        (days, typed) =>
          this.#set(['submission', 'verification', 'days'], days, daysLabel, typed ? 'verification-days' : undefined),
        'refused',
      )}
    `;
  }

  #moreOptions(definition: DesignDefinition, look: CanvasLook): TemplateResult {
    const submission = groupOf(definition, 'submission');
    const progress = this.#term('progress');
    return html`
      <fieldset class="item">
        <legend class="label">${this.#term('pageButtons')}</legend>
        <p class="help">${this.#term('pageButtonsHelp')}</p>
        <div class="pair">
          ${this.#buttonLabel('next-label', 'nextLabel', this.#term('nextLabel'), null, word(look, 'next'))}
          ${this.#buttonLabel('back-label', 'backLabel', this.#term('backLabel'), null, word(look, 'back'))}
        </div>
      </fieldset>
      ${this.#switch(
        'summary-page',
        this.#term('summaryPage'),
        this.#term('summaryPageHelp'),
        flagIn(submission, 'summaryPage', false),
        (on) => this.#set(['submission', 'summaryPage'], on, this.#term('summaryPage')),
      )}
      ${this.#select(
        'progress',
        progress,
        this.#term('progressHelp'),
        wordIn(submission, 'progress', 'steps'),
        ['steps', 'bar', 'none'].map((value) => ({ value, label: this.#term(`progress_${value}`) })),
        (value) => this.#set(['submission', 'progress'], value, progress),
      )}
      ${this.#reference(definition)}
    `;
  }

  #reference(definition: DesignDefinition): TemplateResult {
    const label = this.#term('reference');
    const reference = objectIn(groupOf(definition, 'submission'), 'entryReference') ?? {};
    const prefix = lineIn(reference, 'prefix');
    const year = flagIn(reference, 'year', true);
    const digits = numberIn(reference['digits']) ?? REFERENCE_DIGITS;
    // The reference as visitors get it: left out where it is the default one.
    const write = (next: { prefix: string; year: boolean; digits: number }, typed?: string) =>
      this.#set(
        ['submission', 'entryReference'],
        next.prefix === '' && next.year && next.digits === REFERENCE_DIGITS
          ? undefined
          : { ...(next.prefix ? { prefix: next.prefix } : {}), year: next.year, digits: next.digits },
        label,
        typed,
      );
    const example = referenceOf(prefix, year ? new Date().getFullYear() : null, digits, EXAMPLE_NUMBER);
    return html`<fieldset class="item">
      <legend class="label">${label}</legend>
      <p class="help">${this.#term('referenceHelp')}</p>
      <div class="pair">
        ${this.#line(
          'reference-prefix',
          this.#term('referencePrefix'),
          null,
          prefix,
          this._choices?.limits.lineLength ?? 0,
          (text, typed) => write({ prefix: text, year, digits }, typed ? 'reference-prefix' : undefined),
        )}
        ${this.#whole(
          'reference-digits',
          this.#term('referenceDigits'),
          null,
          digits,
          1,
          this._choices?.limits.referenceDigits ?? REFERENCE_DIGITS,
          (value, typed) =>
            write({ prefix, year, digits: value ?? REFERENCE_DIGITS }, typed ? 'reference-digits' : undefined),
          'refused',
        )}
      </div>
      ${this.#switch('reference-year', this.#term('referenceYear'), null, year, (on) =>
        write({ prefix, year: on, digits }),
      )}
      <p class="help example" aria-live="polite">${this.#term('referenceExample')} <code>${example}</code></p>
    </fieldset>`;
  }

  // "Insert merge tag" under the entry title's box, with the tags an entry title may use (ADR-0173).
  #tagPicker(id: string): TemplateResult | typeof nothing {
    const form = this.#form;
    if (!form) return nothing;
    return html`<jfb-merge-tag-picker
      .place=${'entryTitle'}
      .form=${form}
      .owner=${''}
      .culture=${this._look?.culture ?? ''}
      .definition=${() => this.#definition()}
      .insert=${(tag: string) => this.#insertTag(id, tag)}
    ></jfb-merge-tag-picker>`;
  }

  // A tag put into the box where its caret was, the box given the focus with the caret after the tag, and the change
  // sent as leaving the box sends one.
  #insertTag(id: string, tag: string) {
    const box = this.shadowRoot?.getElementById(id);
    if (!(box instanceof HTMLInputElement)) return;
    const end = box.value.length;
    const { text, caret } = withTag(box.value, box.selectionStart ?? end, box.selectionEnd ?? end, tag);
    box.value = text;
    box.focus();
    box.setSelectionRange(caret, caret);
    box.dispatchEvent(new Event('change', { bubbles: true }));
  }

  // ---- Availability -------------------------------------------------------------------------------------------------

  #availability(
    definition: DesignDefinition,
    choices: FormSettingsChoicesResponseModel,
    look: CanvasLook,
  ): TemplateResult {
    const availability = groupOf(definition, 'availability');
    const zone = this.#zone();
    const limit = this.#term('maxEntries');
    return html`
      <p class="note" id="time-zone">${this.#term('timeZone', zone)}</p>
      <fieldset class="item">
        <legend class="visually-hidden">${this.#term('opensAt')}, ${this.#term('closesAt')}</legend>
        <div class="pair">
          ${this.#instant('opens-at', 'opens', this.#term('opensAt'), availability['opensAt'], zone)}
          ${this.#instant('closes-at', 'closes', this.#term('closesAt'), availability['closesAt'], zone)}
        </div>
        <p class="help" id="opening-help">${this.#term('openingHelp')}</p>
      </fieldset>
      ${this.#whole(
        'max-entries',
        limit,
        this.#term('maxEntriesHelp'),
        numberIn(availability['maxEntries']),
        1,
        MOST_ENTRIES,
        (value, typed) => this.#set(['availability', 'maxEntries'], value, limit, typed ? 'max-entries' : undefined),
      )}
      ${this.#closedMessage(availability, choices, look)} ${this.#access(availability, choices)}
    `;
  }

  /**
   * An opening or a closing on the site's clock, which its box names. A closing must come after the opening; a bound
   * refused for the other one is taken once the other one changes so that it fits.
   */
  #instant(id: string, kind: 'opens' | 'closes', label: string, value: unknown, zone: string): TemplateResult {
    const own = kind === 'opens' ? 'opensAt' : 'closesAt';
    const other = kind === 'opens' ? 'closesAt' : 'opensAt';
    const otherId = kind === 'opens' ? 'closes-at' : 'opens-at';
    // A box's time as an instant: undefined for an empty box, null for one half typed or no time.
    const read = (box: HTMLInputElement): string | undefined | null =>
      box.validity.badInput ? null : box.value === '' ? undefined : toInstant(box.value, zone);
    const ordered = (opening: string | undefined, closing: string | undefined) =>
      !opening || !closing || Date.parse(closing) > Date.parse(opening);
    const typed = (box: HTMLInputElement, done: boolean) => {
      const instant = read(box);
      if (instant === null) {
        if (done) this.#problem(id, this.#term('notATime'));
        return;
      }
      const availability = groupOf(this.#definition(), 'availability');
      const stored = isInstant(availability[other]) ? availability[other] : undefined;
      if (!(kind === 'opens' ? ordered(instant, stored) : ordered(stored, instant))) {
        if (done) this.#problem(id, this.#term(kind === 'opens' ? 'opensLast' : 'closesFirst'));
        return;
      }
      this.#problem(id, null);
      let next = withSettingAt(this.#definition(), ['availability', own], instant);
      const held = this.shadowRoot?.getElementById(otherId);
      if (this._problems.has(otherId) && held instanceof HTMLInputElement) {
        const heldInstant = read(held);
        const fits =
          heldInstant !== null && (kind === 'opens' ? ordered(instant, heldInstant) : ordered(heldInstant, instant));
        if (fits) {
          next = withSettingAt(next, ['availability', other], heldInstant);
          this.#problem(otherId, null);
        }
      }
      this.#change(next, label, done ? undefined : id);
    };
    return this.#frame(
      id,
      label,
      null,
      (described, invalid) =>
        html`<input
          type="datetime-local"
          id=${id}
          step="60"
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          .value=${boxValue(toLocal(value, zone), this._problems.has(id))}
          @input=${(event: Event) => typed(event.target as HTMLInputElement, false)}
          @change=${(event: Event) => typed(event.target as HTMLInputElement, true)}
        />`,
      { besides: ['time-zone', 'opening-help'] },
    );
  }

  #closedMessage(
    availability: Readonly<Record<string, unknown>>,
    choices: FormSettingsChoicesResponseModel,
    look: CanvasLook,
  ): TemplateResult {
    const id = 'closed-message';
    const label = this.#term('closedMessage');
    const help = this.#term('closedMessageHelp');
    const texts = textsIn(availability, 'closedMessage');
    const isDefault = this.#isDefault();
    const problems = this.#problemsAt(id);
    const change = (text: string, done: boolean) => {
      if (text.length > choices.limits.htmlLength) {
        if (done) this.#problem(id, this.#problemWords({ kind: 'tooLong', limit: choices.limits.htmlLength }));
        return;
      }
      this.#problem(id, null);
      const before = textsIn(groupOf(this.#definition(), 'availability'), 'closedMessage');
      this.#set(
        ['availability', 'closedMessage'],
        withText(before, look.culture, text, isDefault),
        label,
        done ? undefined : id,
      );
    };
    // The editor lies in a shadow root of its own, which ids can't reach: its description carries the problems too.
    return html`<div class="item">
      <p class="label" id=${`${id}-label`}>${label}</p>
      <p class="help">${help}</p>
      <jfb-rich-text
        id=${id}
        .label=${label}
        .description=${[help, ...problems.map((problem) => problem.text)].join(' ')}
        .language=${look.culture}
        .placeholder=${isDefault ? '' : textIn(texts, this.#defaultCulture())}
        .value=${isDefault ? textIn(texts, look.culture) : ownText(texts, look.culture)}
        .change=${change}
      ></jfb-rich-text>
      ${this.#whys(id, problems)}
    </div>`;
  }

  // Who may fill it in, and for some member groups the groups under it: one template for every choice, so the choice
  // keeps the focus as it changes.
  #access(availability: Readonly<Record<string, unknown>>, choices: FormSettingsChoicesResponseModel): TemplateResult {
    const label = this.#term('access');
    const access = wordIn(availability, 'members', 'anyone');
    const chosen = namesIn(availability, 'memberGroups');
    const choose = this.#select(
      'access',
      label,
      this.#term('accessHelp'),
      access,
      ['anyone', 'members', 'groups'].map((value) => ({ value, label: this.#term(`access_${value}`) })),
      (value) =>
        this.#change(
          withSettingAt(
            withSettingAt(this.#definition(), ['availability', 'members'], value),
            ['availability', 'memberGroups'],
            value === 'groups' ? chosen : undefined,
          ),
          label,
        ),
    );
    return html`<div class="item with-detail">
      ${choose}
      ${access === 'groups' ? html`<div class="detail">${this.#memberGroups(chosen, choices)}</div>` : nothing}
    </div>`;
  }

  #memberGroups(chosen: readonly string[], choices: FormSettingsChoicesResponseModel): TemplateResult {
    const groupsLabel = this.#term('memberGroups');
    const known = new Set(choices.memberGroups.map((group) => group.key));
    const gone = chosen.filter((key) => !known.has(key));
    const problems = this.#problemsAt('member-groups');
    const toggle = (key: string, on: boolean) => {
      const now = namesIn(groupOf(this.#definition(), 'availability'), 'memberGroups');
      this.#set(['availability', 'memberGroups'], on ? [...now, key] : now.filter((one) => one !== key), groupsLabel);
    };
    const box = (key: string, name: string) =>
      html`<li>
        <label>
          <input
            type="checkbox"
            .checked=${live(chosen.includes(key))}
            @change=${(event: Event) => toggle(key, (event.target as HTMLInputElement).checked)}
          />
          <span>${name}</span>
        </label>
      </li>`;
    return html`<fieldset
      class="item"
      id="member-groups"
      aria-describedby=${this.#described('member-groups', false, problems) ?? nothing}
    >
      <legend class="label">${groupsLabel}</legend>
      ${
        choices.memberGroups.length === 0 && gone.length === 0
          ? html`<p class="help">${this.#term('memberGroupsNone')}</p>`
          : html`<ul class="checks">
              ${choices.memberGroups.map((group) => box(group.key, group.name))}
              ${gone.map((key) => box(key, this.#term('memberGroupGone')))}
            </ul>`
      }
      ${this.#whys('member-groups', problems)}
    </fieldset>`;
  }

  // ---- Spam protection ----------------------------------------------------------------------------------------------

  #protection(
    definition: DesignDefinition,
    choices: FormSettingsChoicesResponseModel,
    providers: readonly CaptchaProviderResponseModel[],
  ): TemplateResult {
    const label = this.#term('protection');
    const choice = protectionOf(definition);
    const chosen = choiceValue(choice);
    const problems = this.#problemsAt('protection');
    const rows: {
      readonly choice: ProtectionChoice;
      readonly name: string;
      readonly line: string;
      readonly dimmed: boolean;
    }[] = [
      {
        choice: { kind: 'level', level: 'inherit' },
        name: this.#term('siteDefault', siteProtection(choices, providers, this.#term)),
        line: this.#term('protection_inherit'),
        dimmed: false,
      },
      {
        choice: { kind: 'level', level: 'standard' },
        name: this.#term('protection_standard'),
        line: this.#term('protection_standardLine'),
        dimmed: false,
      },
      {
        choice: { kind: 'level', level: 'proofOfWork' },
        name: this.#term('protection_proofOfWork'),
        line: this.#term('protection_proofOfWorkLine'),
        dimmed: false,
      },
      ...providers.map((provider) => ({
        choice: { kind: 'captcha' as const, provider: provider.name },
        name: provider.label,
        line: provider.isConfigured ? this.#term('captchaLine') : this.#term('captchaNoKeys'),
        dimmed: !provider.isConfigured,
      })),
      // A provider the site lacks stays chosen until another choice, said as such.
      ...(choice.kind === 'captcha' && !providers.some((provider) => provider.name === choice.provider)
        ? [{ choice, name: choice.provider, line: this.#term('captchaGone'), dimmed: true }]
        : []),
    ];
    const provider = choice.kind === 'captcha' ? providers.find((one) => one.name === choice.provider) : undefined;
    // What a provider without its keys means is said once, under the choices.
    const keyless = providers.some((one) => !one.isConfigured);
    const rules = this.#term('contentRules');
    return html`
      <fieldset
        class="item"
        id="protection"
        aria-describedby=${
          this.#described('protection', false, problems, keyless ? ['protection-keys'] : []) ?? nothing
        }
      >
        <legend class="visually-hidden">${label}</legend>
        <div class="choices">
          ${rows.map((row) => {
            const value = choiceValue(row.choice);
            const id = `protection-${value.replace(/[^a-zA-Z0-9]/g, '-')}`;
            // Named by its name alone, its line its description: a screen reader says each once.
            return html`<label class="choice" ?data-dimmed=${row.dimmed}>
              <input
                type="radio"
                name="protection"
                value=${value}
                .checked=${live(value === chosen)}
                aria-labelledby=${`${id}-name`}
                aria-describedby=${`${id}-line`}
                @change=${() =>
                  this.#change(withProtection(this.#definition(), row.choice), this.#term('group_protection'))}
              />
              <span class="choice-name" id=${`${id}-name`}>${row.name}</span>
              <span class="choice-line" id=${`${id}-line`}>${row.line}</span>
            </label>`;
          })}
        </div>
        ${keyless ? html`<p class="help" id="protection-keys">${this.#term('captchaNoKeysNote')}</p>` : nothing}
        ${this.#whys('protection', problems)}
      </fieldset>
      ${
        provider && provider.settings.length > 0
          ? html`<div class="detail provider-settings">${this.#providerSettings(definition, provider)}</div>`
          : nothing
      }
      ${this.#select(
        'content-rules',
        rules,
        this.#term('contentRulesHelp'),
        wordIn(groupOf(definition, 'protection'), 'contentRules', 'inherit'),
        [
          {
            value: 'inherit',
            label: this.#term('siteDefault', this.#term(`contentRules_${choices.defaults.contentRules}`)),
          },
          ...['off', 'normal', 'strict'].map((value) => ({ value, label: this.#term(`contentRules_${value}`) })),
        ],
        (value) => this.#set(['protection', 'contentRules'], value, rules),
      )}
    `;
  }

  // A chosen provider's own settings, drawn by the inspector's editors from what the provider describes; the form
  // check's problems under them, without holding what a box shows.
  #providerSettings(
    definition: DesignDefinition,
    provider: CaptchaProviderResponseModel,
  ): (TemplateResult | typeof nothing)[] {
    const settings = (settingAt(definition, ['protection', 'captcha', 'settings']) ?? {}) as Readonly<
      Record<string, unknown>
    >;
    const found = (id: string): Found | undefined => {
      const problems = this.#checkedNow.found.get(id);
      if (!problems?.length) return undefined;
      const ids = problems.map((_, index) => `${id}-found-${index}`);
      return {
        ids,
        lines: html`${problems.map((problem, index) => whyLine(ids[index] ?? '', problem, this.#weight))}`,
        error: problems.some((problem) => problem.severity === 'error'),
      };
    };
    const context: EditorContext = {
      say: this.#say,
      term: (key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args),
      culture: this._look?.culture ?? '',
      defaultCulture: this.#defaultCulture(),
      problems: this._problems,
      set: (path, setting, parsed, done) => this.#setCaptcha(path, setting, parsed, done),
      found,
    };
    return provider.settings.map((setting) =>
      settingEditor(
        context,
        setting,
        Object.hasOwn(settings, setting.name) ? settings[setting.name] : setting.default,
        `captcha.${setting.name}`,
      ),
    );
  }

  #setCaptcha(path: string, setting: FieldSettingResponseModel, parsed: Parsed, done: boolean) {
    const id = captchaBox(setting.name);
    if (!parsed.ok) {
      if (done) this.#problem(id, this.#problemWords(parsed.problem));
      return;
    }
    this.#problem(id, null);
    const value = parsed.value === undefined || same(parsed.value, setting.default) ? undefined : parsed.value;
    this.#set(
      ['protection', 'captcha', 'settings', setting.name],
      value,
      this.#say(setting.label),
      done ? undefined : path,
    );
  }

  // ---- Appearance and Developer -------------------------------------------------------------------------------------

  #appearance(definition: DesignDefinition, choices: FormSettingsChoicesResponseModel): TemplateResult {
    const appearance = groupOf(definition, 'appearance');
    const theme = this.#term('theme');
    const marker = this.#term('requiredMarker');
    const layout = this.#term('choiceLayout');
    const css = this.#term('cssClass');
    const own = lineIn(appearance, 'theme');
    return html`
      ${this.#select(
        'theme',
        theme,
        this.#term('themeHelp'),
        own,
        [
          { value: '', label: this.#term('siteDefault', themeName(choices, choices.defaults.theme)) },
          ...choices.themes.map((one) => ({ value: one.alias, label: one.label })),
          // A theme the site lacks stays chosen until another choice; the form check says it is missing.
          ...(own && !choices.themes.some((one) => one.alias === own)
            ? [{ value: own, label: own, disabled: true }]
            : []),
        ],
        (value) => this.#set(['appearance', 'theme'], value || undefined, theme),
      )}
      ${this.#select(
        'required-marker',
        marker,
        this.#term('requiredMarkerHelp'),
        wordIn(appearance, 'requiredMarker', 'inherit'),
        [
          {
            value: 'inherit',
            label: this.#term('siteDefault', this.#term(`summary_marker_${choices.defaults.requiredMarker}`)),
          },
          ...['required', 'optional', 'none'].map((value) => ({ value, label: this.#term(`requiredMarker_${value}`) })),
        ],
        (value) => this.#set(['appearance', 'requiredMarker'], value, marker),
      )}
      ${this.#switch(
        'validation-summary',
        this.#term('validationSummary'),
        this.#term('validationSummaryHelp'),
        flagIn(appearance, 'validationSummary', true),
        (on) => this.#set(['appearance', 'validationSummary'], on, this.#term('validationSummary')),
      )}
      ${this.#select(
        'choice-layout',
        layout,
        this.#term('choiceLayoutHelp'),
        wordIn(appearance, 'choiceLayout', 'vertical'),
        ['vertical', 'horizontal', 'cards'].map((value) => ({ value, label: this.#term(`choiceLayout_${value}`) })),
        (value) => this.#set(['appearance', 'choiceLayout'], value, layout),
      )}
      ${this.#line(
        'css-class',
        css,
        this.#term('cssClassHelp'),
        lineIn(appearance, 'cssClass'),
        choices.limits.lineLength,
        (text, typed) => this.#set(['appearance', 'cssClass'], text || undefined, css, typed ? 'css-class' : undefined),
        { code: true },
      )}
    `;
  }

  // Privacy (ADR-0211, ADR-0222, ADR-0224): the form's own days per state, each empty for the site's, which its box shows;
  // what its entries keep of IP addresses, the full address only where the host allows it; and the form's insights,
  // which count only while the site's are on too.
  #privacy(definition: DesignDefinition, choices: FormSettingsChoicesResponseModel): TemplateResult {
    const own = objectIn(groupOf(definition, 'privacy'), 'retentionDays') ?? {};
    const site = choices.defaults.retention;
    const ipLabel = this.#term('ip');
    const ip = wordIn(groupOf(definition, 'privacy'), 'ip', 'inherit');
    const fullIp = choices.host.fullIp;
    return html`
      <fieldset class="item" id="retention">
        <legend class="label">${this.#term('retentionDays')}</legend>
        <p class="help">${this.#term('retentionDaysHelp')}</p>
        <div class="pair">
          ${RETENTION_STATES.map((state) => {
            const id = `retention-${state}`;
            const label = this.#term(`retention_${state}`);
            const byDefault = site[state];
            return this.#whole(
              id,
              label,
              null,
              numberIn(own[state]),
              1,
              choices.limits.retentionDays,
              (value, typed) => this.#set(['privacy', 'retentionDays', state], value, label, typed ? id : undefined),
              'none',
              {
                unit: this.#term('daysUnit'),
                placeholder: byDefault === FOR_GOOD ? this.#term('retentionForGood') : this.#number(byDefault),
              },
            );
          })}
        </div>
      </fieldset>
      ${this.#select(
        'ip',
        ipLabel,
        fullIp ? this.#term('ipHelp') : this.#term('ipHelpNoFull', choices.host.fullIpKey),
        ip,
        [
          { value: 'inherit', label: this.#term('siteDefault', this.#term(`summary_ip_${choices.defaults.ip}`)) },
          ...['none', 'anonymised', 'full'].map((value) => ({
            value,
            label: this.#term(`ip_${value}`),
            // Full addresses wait for the host; one the form holds stays chosen, and the form check says what the site keeps.
            disabled: value === 'full' && !fullIp && ip !== 'full',
          })),
        ],
        (value) => this.#set(['privacy', 'ip'], value, ipLabel),
      )}
      ${this.#switch(
        'insights',
        this.#term('insights'),
        choices.defaults.insights ? this.#term('insightsHelp') : this.#term('insightsSiteOff'),
        flagIn(groupOf(definition, 'privacy'), 'insights', true),
        (on) => this.#set(['privacy', 'insights'], on, this.#term('insights')),
      )}
    `;
  }

  #developer(definition: DesignDefinition, choices: FormSettingsChoicesResponseModel): TemplateResult {
    const developer = groupOf(definition, 'developer');
    return html`
      ${this.#switch(
        'headless',
        this.#term('headless'),
        this.#term('headlessHelp'),
        flagIn(developer, 'headless', false),
        (on) => this.#set(['developer', 'headless'], on, this.#term('headless')),
      )}
      ${this.#names(
        'query-values',
        'queryValues',
        this.#term('queryValues'),
        this.#term('queryValuesHelp'),
        namesIn(developer, 'queryValues'),
        choices,
        null,
      )}
      ${this.#names(
        'context-values',
        'contextValues',
        this.#term('contextValues'),
        this.#term('contextValuesHelp'),
        namesIn(developer, 'contextValues'),
        choices,
        'jfb',
      )}
    `;
  }

  // A list of names, one a line, checked as the server checks it.
  #names(
    id: string,
    name: 'queryValues' | 'contextValues',
    label: string,
    help: string,
    names: readonly string[],
    choices: FormSettingsChoicesResponseModel,
    reserved: string | null,
  ): TemplateResult {
    const longest = choices.limits.nameLength;
    const typed = (raw: string, done: boolean) => {
      const parsed = parseNames(raw, longest, reserved);
      if (!parsed.ok) {
        if (done)
          this.#problem(id, this.#term(`name_${parsed.problem.kind}`, parsed.problem.name, this.#number(longest)));
        return;
      }
      this.#problem(id, null);
      this.#set(['developer', name], parsed.names, label, done ? undefined : id);
    };
    return this.#frame(
      id,
      label,
      help,
      (described, invalid) =>
        html`<textarea
          id=${id}
          class="code short"
          rows="3"
          spellcheck="false"
          autocomplete="off"
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          .value=${boxValue(names.join('\n'), this._problems.has(id))}
          @input=${(event: Event) => typed((event.target as HTMLTextAreaElement).value, false)}
          @change=${(event: Event) => typed((event.target as HTMLTextAreaElement).value, true)}
        ></textarea>`,
    );
  }

  // ---- After sending ------------------------------------------------------------------------------------------------

  #list(): OutcomeListElement | null {
    return this.shadowRoot?.querySelector('jfb-outcome-list') ?? null;
  }

  #after(definition: DesignDefinition, choices: FormSettingsChoicesResponseModel, look: CanvasLook): TemplateResult {
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    return html`<p class="note">${this.#term('outcome_listHelp')}</p>
      <jfb-outcome-list
        .outcomes=${outcomesOf(definition)}
        .culture=${look.culture}
        .defaultMessage=${word(look, 'thankYou')}
        .words=${inspectorRuleWords(definition, this._types, look.culture, look.languages, term)}
        .flags=${this.#checkedNow.outcomes}
        .problems=${this.#problemsAt('outcomes')}
        .weight=${this.#weight}
        .limit=${choices.limits.outcomes}
        .open=${(key: string | null) => void this.#openOutcome(key)}
        .move=${(from: number, to: number) => this.#moveOutcome(from, to)}
      ></jfb-outcome-list>`;
  }

  /** Makes a change of the outcomes, named for Undo; false where it changes nothing or is refused. */
  #changeOutcomes(definition: DesignDefinition, said: string): boolean {
    const context = this.#context;
    const view = this._view;
    if (!context || !view) return false;
    this.#visit++;
    if (same(definition['outcomes'], view.state.definition['outcomes'])) return false;
    const result = context.edit({ ok: true, state: { definition, selection: view.state.selection } }, said);
    if (!result.ok) this.#announce(this.localize.term(`jfbDesigner_refusal_${result.refusal}`));
    return result.ok;
  }

  #moveOutcome(from: number, to: number) {
    const definition = this.#definition();
    this.#changeOutcomes(
      withOutcomes(definition, moved(outcomesOf(definition), from, to)),
      this.#term('outcome_undoMoved'),
    );
  }

  /**
   * An outcome's sidebar, or a new one's with null: Update keeps the outcome at the place the sidebar chose, Add puts a
   * new one there, and Delete outcome takes it away with Undo in the toast. On a published form a toast says when
   * visitors get a change; a change that changed nothing says nothing.
   */
  async #openOutcome(key: string | null, focus?: OutcomeSetting) {
    const context = this.#context;
    const look = this._look;
    const choices = this._choices;
    const form = this.#form;
    if (!context || !look || !choices || !form || this.#editingOutcome) return;
    const definition = this.#definition();
    const outcomes = outcomesOf(definition);
    const at = key === null ? -1 : outcomes.findIndex((one) => one.key === key);
    const outcome = outcomes[at] ?? null;
    if (key !== null && !outcome) return;
    if (key === null && choices.limits.outcomes > 0 && outcomes.length >= choices.limits.outcomes) return;
    const defaultMessage = word(look, 'thankYou');
    this.#editingOutcome = true;
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
          htmlLength: choices.limits.htmlLength,
          check: (asked) => context.checkOf(asked),
          preview: (asked, outcome, culture) => context.previewOf(asked, outcome, culture),
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
        const deleted = this.#changeOutcomes(withOutcomes(now, left), this.#term('outcome_undoDeleted'));
        if (deleted && key !== null) void this.#offerUndo(this.#term('outcome_deleted'), key);
        // The list shows the change before the focus goes to an outcome in it.
        await this.updateComplete;
        await list?.focusOutcome(left[Math.max(at, 0)]?.key ?? left.at(-1)?.key ?? null);
        return;
      }
      // The outcome at the place the sidebar chose: a changed one taken from where it was first.
      const others = before.filter((one) => one.key !== value.outcome.key);
      const next = withOutcome(others, value.outcome, Math.min(value.at, others.length));
      const changed = this.#changeOutcomes(
        withOutcomes(now, next),
        this.#term(outcome ? 'outcome_undoChanged' : 'outcome_undoAdded'),
      );
      if (!changed) {
        await list?.focusOutcome(key);
        return;
      }
      const place = next.findIndex((one) => one.key === value.outcome.key);
      const name = outcomeName(value.outcome, look.culture, defaultMessage, new Map(), this.#term);
      if (!outcome) list?.announce(this.#term('outcome_added', name, place + 1, next.length));
      else if (place !== at) list?.announce(this.#term('outcome_moved', name, place + 1, next.length));
      if (this.#published)
        void this.#getNotifications().then((notifications) =>
          notifications?.peek('positive', {
            data: { message: this.#term(outcome ? 'outcome_updatedPublished' : 'outcome_addedPublished') },
          }),
        );
      await this.updateComplete;
      await list?.focusOutcome(value.outcome.key);
    } finally {
      this.#editingOutcome = false;
    }
  }

  #getNotifications() {
    return this.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
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
        if (this._view?.state !== made) return;
        this.#travel('undo');
        await this.updateComplete;
        await this.#list()?.focusOutcome(restored);
      },
    };
    this.#undoToast = (await this.#getNotifications())?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  // ---- The page -----------------------------------------------------------------------------------------------------

  #body(
    group: GroupId,
    definition: DesignDefinition,
    choices: FormSettingsChoicesResponseModel,
    providers: readonly CaptchaProviderResponseModel[],
    look: CanvasLook,
  ): TemplateResult {
    switch (group) {
      case 'submission':
        return this.#submission(definition, look);
      case 'after':
        return this.#after(definition, choices, look);
      case 'availability':
        return this.#availability(definition, choices, look);
      case 'protection':
        return this.#protection(definition, choices, providers);
      case 'privacy':
        return this.#privacy(definition, choices);
      case 'appearance':
        return this.#appearance(definition, choices);
      case 'developer':
        return this.#developer(definition, choices);
    }
  }

  #group(
    group: GroupId,
    definition: DesignDefinition,
    choices: FormSettingsChoicesResponseModel,
    providers: readonly CaptchaProviderResponseModel[],
    look: CanvasLook,
  ): TemplateResult {
    const open = this._open.has(group);
    const thanks = word(look, 'thankYou');
    const line = open
      ? nothing
      : html`<span class="summary"
          >${flagLine(this.#checkedNow.flagged.get(group), (count) => this.#term('problems', count), DOT)}${summaryOf(
            group,
            definition,
            choices,
            providers,
            {
              term: this.#term,
              date: this.#date,
              number: this.#number,
              outcome: (outcome) => outcomeName(outcome, look.culture, thanks, new Map(), this.#term),
            },
          )}</span
        >`;
    return html`<section class="group" data-group=${group}>
      <h2 class="group-head">
        <button
          type="button"
          id=${`group-${group}`}
          aria-expanded=${open ? 'true' : 'false'}
          aria-controls=${open ? `body-${group}` : nothing}
          @click=${() => this.#toggle(group)}
        >
          <uui-icon name=${open ? 'icon-navigation-down' : 'icon-navigation-right'} aria-hidden="true"></uui-icon>
          <span class="group-name">${this.#term(`group_${group}`)}</span>
          ${line}
        </button>
      </h2>
      ${
        open
          ? html`<div class="group-body" id=${`body-${group}`}>
              ${this.#body(group, definition, choices, providers, look)}
            </div>`
          : nothing
      }
    </section>`;
  }

  // The language the texts are shown in, on a site with more than one, and whose texts visitors see where one is empty.
  #toolbar(look: CanvasLook): TemplateResult | typeof nothing {
    if (look.languages.length < 2) return nothing;
    const shown = look.culture.toLowerCase();
    return html`<div class="toolbar">
      <label class="language">
        <span>${this.localize.term('jfbDesigner_language')}</span>
        <select @change=${(event: Event) => void this.#showLanguage((event.target as HTMLSelectElement).value)}>
          ${look.languages.map(
            (language) =>
              html`<option value=${language.culture} ?selected=${language.culture.toLowerCase() === shown}>
                ${language.name}
              </option>`,
          )}
        </select>
      </label>
      ${
        this.#isDefault()
          ? nothing
          : html`<p class="note">
              ${this.localize.term(
                'jfbInspector_editingLanguage',
                this.#languageName(look.culture),
                this.#languageName(this.#defaultCulture()),
              )}
            </p>`
      }
    </div>`;
  }

  override render() {
    if (this._failed)
      return html`<div class="page">
        <div class="failed" role="alert">
          <p>${this.#term('failed')}</p>
          <uui-button look="secondary" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </div>`;
    const view = this._view;
    const choices = this._choices;
    const providers = this._providers;
    const look = this._look;
    if (!view || !choices || !providers || !look)
      return html`<div class="page">
        <div class="skeleton" aria-hidden="true"><span></span><span></span><span></span><span></span><span></span></div>
        <p class="visually-hidden" role="status">${this.#term('loading')}</p>
      </div>`;
    const definition = view.state.definition;
    this.#checkedNow = this.#checked(definition);
    return html`<div class="page">
        ${this.#toolbar(look)} ${GROUPS.map((group) => this.#group(group, definition, choices, providers, look))}
      </div>
      <div class="visually-hidden" id="said" role="status" aria-live="polite">${this._said}</div>`;
  }
}

if (!customElements.get('jfb-form-settings-view'))
  customElements.define('jfb-form-settings-view', FormSettingsViewElement);

export { FormSettingsViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-settings-view': FormSettingsViewElement;
  }
}
