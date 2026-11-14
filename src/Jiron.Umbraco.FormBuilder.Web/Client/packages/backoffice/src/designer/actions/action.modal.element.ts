// An action's sidebar (ROADMAP 10.2a, IA 5.3, ADR-0188): for a new action, what happens first, a choice of the types'
// presets that replaces the settings below it; then the name, which follows that choice until it is typed, when it
// runs and its place among that trigger's actions, the type's own settings as the inspector draws them (Essentials, then More options), whether a failure stops the
// rest, and the action's rule, which the rules builder edits over it. An action of the form keeps its type, said where
// the choice would be. The form check is asked about the draft as Add or Update would make it once the changes pause,
// and its problems in the action show under their box: those it had at once, others once their box is left or the
// button is pressed, which keeps the action once the check finds no error in it, or gives no answer. Without
// `manageActions` every setting shows as it is, and only Close is offered. An email has its own editors (ADR-0189): its
// To box holds every kind of recipient, its fields are picked by kind, its subject and body offer merge tags for mail,
// and the mail it sends for the sample entry shows beside the settings where there is room, after them where there is
// not, asked once the changes pause. A webhook's (ADR-0191) say where it goes and what it sends in a box each, its texts
// offering merge tags for actions, and keep what another choice of theirs held while the sidebar is open.
import {
  css,
  html,
  live,
  nothing,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement, umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { ActionTypeResponseModel, FieldSettingResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { pressOf } from '../../styles/press';
import { issueWords, type CheckWordsContext } from '../check/check-words';
import { parsePath, type CheckSeverity } from '../check/form-check';
import { FormChecker, UNCHECKED, type CheckState } from '../check/form-checker';
import { allFields, type DesignDefinition, type DesignField, type LocalizedText } from '../definition';
import { boxValue } from '../inspector/box-value';
import { controlStyles } from '../inspector/control.styles';
import { MOST_SHOWN } from '../inspector/inspector-model';
import { focusableIn } from '../inspector/item-focus';
import { insertTagInto } from '../inspector/merge-tag-picker.element';
import { resolveOptions } from '../inspector/option-data';
import type { DesignOptions } from '../inspector/option-values';
import {
  boxId,
  pickedKeys,
  problemText,
  settingEditor,
  settingText,
  type EditorContext,
  type Found as BoxFound,
} from '../inspector/setting-editor';
import { same, valueOf, withText, type Parsed, type Problem } from '../inspector/setting-values';
import { NO_PREVIEW, type PreviewState } from '../preview/draft-previewer';
import { textIn } from '../preview/field-models';
import { RULE_BUILDER_MODAL } from '../rules/rule-builder.modal.token';
import { elementNames, inspectorRuleWords, typeFactsOf } from '../rules/rule-names';
import { ruleWords } from '../rules/rule-words';
import { choiceStyles } from '../settings/choice.styles';
import { described, whyLine, whys, type Shown } from '../settings/problem-lines';
import {
  actionEditors,
  actionEditorStyles,
  actionSettingText,
  type ActionEditorContext,
  type PickField,
  type PickOption,
  type RecipientRoute,
} from './action-editors';
import {
  TRIGGERS,
  actionOf,
  actionsOf,
  editOf,
  keptIn,
  newEdit,
  placeOf,
  settingAt,
  settingAtPath,
  withActions,
  withChoice,
  withSettingAt,
  type ActionChoice,
  type ActionEdit,
  type ActionSetting,
} from './action-model';
import { testHint } from './action-test';
import type { ActionModalData, ActionModalValue } from './action.modal.token';
import { contentEditorStyles, rowBox, type RowPart } from './content-editors';
import { parentSourceOf, type TypeRead } from './content-model';
import { EmailPreviewer, type EmailPreview } from './email-preview';
import './email-preview.element';
import { webhookEditorStyles } from './webhook-editors';
import { DictionaryReader, dictionaryKeysIn, dictionaryLineOf, type DictionaryLookups } from '../dictionary-texts';
import { EXTENSIONS_CHANGED } from '../../extending/extension-registry';

/** The editors that draw a row's boxes, so a problem at a row's part lands on that box (ADR-0192). */
const ROW_EDITORS: ReadonlySet<string> = new Set(['contentMapping']);

const isRowPart = (part: string | undefined): part is RowPart =>
  part === 'property' || part === 'field' || part === 'value';

/** The editors that pick Umbraco's own items, which a reader sees as Umbraco names them. */
const PICKERS: ReadonlySet<string> = new Set(['documentPicker', 'contentPicker', 'mediaPicker', 'contentMediaFolder']);

/** The ids of the sidebar's own boxes; a type's settings have the inspector's (`boxId`). */
const BOX = {
  whatHappens: 'what-happens',
  name: 'name',
  trigger: 'trigger',
  place: 'place',
  stopOnFailure: 'stop-on-failure',
  rule: 'rule',
} as const;

/** The longest name, as the save checks hold a name. */
const MOST_NAME = 200;

/** A problem the check found in the action, by the box it shows under. */
type FoundAt = ReadonlyMap<string, readonly Shown[]>;

/** A setting of the type and its editor. */
interface Drawn {
  readonly setting: FieldSettingResponseModel;
  readonly template: TemplateResult | typeof nothing;
}

export class ActionModalElement extends UmbModalBaseElement<ActionModalData, ActionModalValue> {
  static override properties = {
    _edit: { state: true },
    _check: { state: true },
    _typed: { state: true },
    _testing: { state: true },
    _shown: { state: true },
    _tried: { state: true },
    _checking: { state: true },
    _more: { state: true },
    _at: { state: true },
    _preview: { state: true },
    _options: { state: true },
    _lookups: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    pressOf('.remove'),
    jfbVisuallyHidden,
    controlStyles,
    choiceStyles,
    actionEditorStyles,
    webhookEditorStyles,
    contentEditorStyles,
    css`
      .action {
        display: grid;
        gap: var(--uui-size-space-6);
      }

      /* The settings and an email's preview: one column, the preview after the settings; side by side where the
         sidebar has room, the preview kept in view while the settings scroll (as an outcome's, ADR-0187). */
      .room {
        container-type: inline-size;
      }

      .layout {
        display: grid;
        gap: var(--uui-size-space-6);
      }

      @container (min-width: 960px) {
        .layout.with-preview {
          grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
          align-items: start;
          gap: var(--uui-size-layout-2);
        }

        jfb-email-preview {
          position: sticky;
          top: 0;
        }
      }

      .kept {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin: 0;
        font-weight: 600;
      }

      .kept uui-icon {
        color: var(--jfb-bo-color-text-muted);
      }

      .choice-name uui-icon {
        margin-inline-end: var(--uui-size-space-2);
        color: var(--jfb-bo-color-text-muted);
        vertical-align: -2px;
      }

      .settings {
        display: grid;
        gap: var(--uui-size-space-6);
      }

      .more {
        justify-self: start;
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        cursor: pointer;
      }

      .rule-words,
      .value {
        margin: 0;
      }

      .value {
        overflow-wrap: anywhere;
        white-space: pre-line;
      }

      .needs {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      /* The rule's button, Umbraco's outline look on a native button, which its words can describe. */
      .rule-button {
        justify-self: start;
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-4);
        border: 1px solid var(--uui-color-border-emphasis);
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        cursor: pointer;
      }

      @media (hover: hover) {
        .rule-button:hover,
        .more:hover {
          color: var(--uui-color-interactive-emphasis);
          border-color: var(--uui-color-interactive-emphasis);
        }
      }

      .rule-button:focus-visible,
      .more:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      /* The footer keeps Cancel and the button in one row; where a phone leaves no room for Delete action beside
         them, it goes to the end of the sidebar's content (as the outcome's sidebar, ADR-0185). */
      .actions {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .actions uui-button,
      .delete-footer {
        white-space: nowrap;
      }

      .delete-body {
        display: none;
      }

      /* Test keeps apart from Delete action, which acts at once. */
      .delete-footer + .test {
        margin-inline-start: var(--uui-size-space-3);
      }

      @media (max-width: 600px) {
        .delete-footer {
          display: none;
        }

        .delete-footer + .test {
          margin-inline-start: 0;
        }

        .delete-body {
          display: block;
          padding-block-start: var(--uui-size-space-5);
          border-block-start: 1px solid var(--uui-color-divider-standalone);
        }
      }
    `,
  ];

  declare private _edit: ActionEdit;
  declare private _check: CheckState;
  /** Why a box's last value couldn't be kept, by its box's id, until it can. */
  declare private _typed: ReadonlyMap<string, string>;
  declare private _testing: boolean;
  /** The boxes whose problems the check's answers show: those the action had as it opened, and each one left. */
  declare private _shown: ReadonlySet<string>;
  /** Whether Add or Update was pressed, after which every problem shows. */
  declare private _tried: boolean;
  declare private _checking: boolean;
  /** Whether the type's settings beyond Essentials show. */
  declare private _more: boolean;
  /** The place chosen among its trigger's actions; null for where it is, or after the others. */
  declare private _at: number | null;
  /** An email's mail for the sample entry, as the preview shows it. */
  declare private _preview: PreviewState<EmailPreview>;
  /** The options of the fields whose options come from a source, by the field's key, as read for the recipient map. */
  declare private _options: ReadonlyMap<string, readonly PickOption[]>;
  /** The dictionary items the action's texts name in the language shown (ADR-0228). */
  declare private _lookups: DictionaryLookups;
  readonly #dictionary = new DictionaryReader();
  // The keys and language the lookups were asked for: an answer for others is dropped.
  #dictionaryAsked = '';

  /** Whether the button is waiting for the check, so a second press does not keep the action twice. */
  #submitting = false;
  /** Whether the rules builder is open over the sidebar, so a second does not open over it. */
  #building = false;
  #checker: FormChecker | undefined;
  #previewer: EmailPreviewer | undefined;
  /** The fields whose options are being read. */
  #reading = new Set<string>();
  /** Whether the check has answered yet: its first answer's problems are the action's own, which show at once. */
  #answered = false;

  constructor() {
    super();
    this._edit = { ...newEdit('', { id: '', type: '', name: '', icon: '', settings: {} }, ''), choice: null };
    this._check = UNCHECKED;
    this._typed = new Map();
    this._testing = false;
    this._shown = new Set();
    this._tried = false;
    this._checking = false;
    this._more = false;
    this._at = null;
    this._preview = NO_PREVIEW;
    this._options = new Map();
    this._lookups = new Map();
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener(EXTENSIONS_CHANGED, this.#extensionsChanged);
    const data = this.data;
    if (!data) return;
    const first = data.choices[0];
    this._edit = data.action
      ? editOf(data.action)
      : first
        ? newEdit(crypto.randomUUID(), first, this.#say(first.name))
        : this._edit;
    this.#answered = false;
    // Who may only read the actions sees the mail too, as the preview needs only design (ADR-0189).
    const preview = data.preview;
    if (preview)
      this.#previewer = new EmailPreviewer(
        (definition) => preview(definition, this._edit.key, data.culture),
        (state) => (this._preview = state),
      );
    this.#askPreview();
    if (data.readOnly) return;
    this.#checker = new FormChecker(data.check, (state) => this.#heard(state));
    this.#checker.changed(this.#draft());
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.removeEventListener(EXTENSIONS_CHANGED, this.#extensionsChanged);
    this.#checker?.reset();
    this.#previewer?.stop();
    this.data?.closed?.();
  }

  // A site's editor registered after the sidebar opened draws its setting now (ADR-0233).
  readonly #extensionsChanged = () => this.requestUpdate();

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    this.#readDictionary();
  }

  // The texts of the dictionary items the action's texts name in the language shown, each asked once (ADR-0228).
  #readDictionary() {
    const culture = this.data?.culture ?? '';
    const keys = dictionaryKeysIn(this._edit.settings, culture);
    const asked = `${culture}|${[...keys].sort().join(' ')}`;
    if (asked === this.#dictionaryAsked) return;
    this.#dictionaryAsked = asked;
    if (keys.size === 0) return;
    void this.#dictionary.read(this, keys, culture).then((lookups) => {
      if (this.#dictionaryAsked === asked) this._lookups = lookups;
    });
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // The sidebar takes the focus itself as it opens; the box a Fix it leads to, or the first, takes it after.
    const focus = this.data?.focus;
    if (focus?.kind === 'setting' && this.#hidden(focus.path)) this._more = true;
    requestAnimationFrame(() => void this.updateComplete.then(() => this.#focus(focus ?? { kind: 'action' })));
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  #weight = (severity: CheckSeverity) => this.localize.term(`jfbSettings_severity_${severity}`);

  #problemWords(problem: Problem): string {
    return problemText((key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args), problem);
  }

  #defaultCulture(): string {
    const data = this.data;
    return data?.languages.find((language) => language.isDefault)?.culture ?? data?.culture ?? '';
  }

  // ---- The action ---------------------------------------------------------------------------------------------------

  #type(): ActionTypeResponseModel | undefined {
    return this.data?.actionTypes.find((type) => type.alias === this._edit.type);
  }

  /** The draft as Add or Update would make it: the action at its place among its trigger's (`keptIn`). */
  #draft(): DesignDefinition {
    const definition = this.data?.definition ?? { pages: [] };
    return withActions(definition, keptIn(actionsOf(definition), actionOf(this._edit), this._at));
  }

  /** The action's place among its trigger's: as chosen, or where it is, or after the others; and how many there are. */
  #place(): { readonly at: number; readonly count: number } {
    const place = placeOf(actionsOf(this.data?.definition ?? { pages: [] }), this._edit.key, this._edit.trigger);
    return { at: Math.min(this._at ?? place.at, place.count - 1), count: place.count };
  }

  /** The names of the type's settings the sidebar has an editor for; the others come with 10.2b and 10.2c. */
  #drawable(): ReadonlySet<string> {
    const context = this.#context(new Map());
    return new Set(
      (this.#type()?.settings ?? [])
        .filter((setting) => settingEditor(context, setting, this.#valueOf(setting)) !== nothing)
        .map((setting) => setting.name),
    );
  }

  /** A change of the action: `done` once its box is left, after which the check's problems with it show. */
  #change(next: ActionEdit, box: string, done: boolean) {
    this._edit = next;
    if (done && !this._shown.has(box)) this._shown = new Set([...this._shown, box]);
    this.#checker?.changed(this.#draft());
    this.#askPreview();
  }

  /** Whether the action sends mail, which its preview shows. */
  #isEmail(): boolean {
    return this._edit.type === 'email' && this.#previewer !== undefined;
  }

  /** Asks for an email's preview of the draft as it is now, once the changes pause. */
  #askPreview() {
    if (this.#isEmail()) this.#previewer?.changed(this.#draft());
  }

  /** The draft's fields that hold a value, as the action's pickers offer them, in the language shown. */
  #pickFields(): PickField[] {
    const data = this.data;
    if (!data) return [];
    const definition = data.definition;
    const top = new Set(
      definition.pages.flatMap((page) => page.groups.flatMap((group) => group.fields.map((field) => field.key))),
    );
    return allFields(definition).flatMap((field): PickField[] => {
      const type = data.fieldTypes[field.type];
      if (!type?.holdsValue) return [];
      const options = field['options'] as DesignOptions | null | undefined;
      return [
        {
          key: field.key,
          alias: field.alias ?? '',
          label: textIn(field.label, data.culture) || field.alias || field.key,
          type: field.type,
          email: field.type === 'email',
          choice: options !== undefined && options !== null,
          file: type.storage === 'file',
          sensitive: field['sensitive'] === true,
          top: top.has(field.key),
          options: this.#optionsOf(field, options),
        },
      ];
    });
  }

  /** A field's options with their labels: its own, or those its source gave; null while a source's are not read. */
  #optionsOf(field: DesignField, options: DesignOptions | null | undefined): readonly PickOption[] | null {
    if (!options) return [];
    if ((options.source ?? 'items') === 'items')
      return (options.items ?? []).map((item) => ({
        value: item.value,
        label: textIn(item.label, this.data?.culture ?? '') || item.value,
      }));
    return this._options.get(field.key) ?? null;
  }

  /** Reads the options of a field whose options come from a source, once, for the recipient map. */
  #readOptions(key: string) {
    const data = this.data;
    if (!data || this.#reading.has(key) || this._options.has(key)) return;
    const field = allFields(data.definition).find((one) => one.key === key);
    const options = field?.['options'] as DesignOptions | null | undefined;
    if (!field || !options || (options.source ?? 'items') === 'items') return;
    this.#reading.add(key);
    void resolveOptions(this, data.form, field, data.culture).then((resolved) => {
      this.#reading.delete(key);
      const read = resolved?.isAvailable
        ? resolved.options.map((option) => ({ value: option.value, label: option.label || option.value }))
        : [];
      this._options = new Map(this._options).set(key, read);
    });
  }

  #typedProblem(box: string, text: string | null) {
    if ((this._typed.get(box) ?? null) === text) return;
    const next = new Map(this._typed);
    if (text === null) next.delete(box);
    else next.set(box, text);
    this._typed = next;
  }

  /**
   * The type's settings the sidebar draws, with their editors: Essentials up to seven, then More options with the rest
   * of Essentials and every other group (section 4.13). A setting whose editor the sidebar has none of yet is neither
   * counted nor shown to who may not change it, so both see the same settings.
   */
  #split(found: FoundAt): {
    readonly shown: readonly Drawn[];
    readonly more: readonly Drawn[];
  } {
    const type = this.#type();
    if (!type) return { shown: [], more: [] };
    const readOnly = this.data?.readOnly ?? false;
    const context = this.#context(found);
    // A setting within another's box is drawn by that box's editor, unless a host locks the box (ADR-0189).
    const boxed = (setting: FieldSettingResponseModel) =>
      Boolean(setting.within) && type.settings.some((box) => box.name === setting.within && !box.lock);
    const drawn = type.settings
      .filter((setting) => !boxed(setting))
      .flatMap((setting): Drawn[] => {
        const editor = settingEditor(context, setting, this.#valueOf(setting));
        return editor === nothing ? [] : [{ setting, template: readOnly ? this.#shown(setting) : editor }];
      });
    const essentials = drawn.filter((one) => one.setting.group === 'essentials');
    return {
      shown: essentials.slice(0, MOST_SHOWN),
      more: [...essentials.slice(MOST_SHOWN), ...drawn.filter((one) => one.setting.group !== 'essentials')],
    };
  }

  /** Whether a setting of the type, by its path, is under More options. */
  #hidden(path: string): boolean {
    const name = path.split('.')[0];
    return this.#split(new Map()).more.some((one) => one.setting.name === name);
  }

  // ---- The check ----------------------------------------------------------------------------------------------------

  #heard(state: CheckState) {
    this._check = state;
    if (this.#answered || state.status !== 'checked') return;
    this.#answered = true;
    // An action of the form shows the problems it has at once; a new one's show once their box is left, or Add pressed.
    if (this.data?.action) this._shown = new Set([...this._shown, ...this.#found().keys()]);
  }

  /** The box a problem's place in the action shows under. */
  #boxOf(setting: ActionSetting): string {
    switch (setting.kind) {
      case 'name':
        return BOX.name;
      case 'trigger':
        return BOX.trigger;
      case 'stopOnFailure':
        return BOX.stopOnFailure;
      case 'rule':
        return BOX.rule;
      case 'setting': {
        // A setting without its editor yet says its problems under what happens.
        const name = setting.path.split('.')[0] ?? setting.path;
        if (!this.#drawable().has(name)) return BOX.whatHappens;
        // A row's part, where its editor draws each row's boxes and the setting holds that row.
        const own = this.#type()?.settings.find((one) => one.name === name);
        const { row } = setting;
        if (row && own && ROW_EDITORS.has(own.editor) && isRowPart(row.part)) {
          const rows = this.#valueOf(own);
          if (Array.isArray(rows) && row.index < rows.length) return rowBox(name, row.index, row.part);
        }
        return boxId(name);
      }
      case 'action':
        return this.data?.action ? BOX.name : BOX.whatHappens;
    }
  }

  /**
   * The check's problems in this action, by the box they show under; one of a setting without its editor yet under what
   * happens, by the setting's name. An answer about the action as it was before the last change shows none of a setting
   * the type lacks: it is another type's, which the last choice replaced; nor of a setting changed since, which is being
   * checked again.
   */
  #found(): Map<string, Shown[]> {
    const found = new Map<string, Shown[]>();
    const data = this.data;
    const checked = this._check.definition;
    if (!data || !checked) return found;
    const actions = actionsOf(checked);
    const index = actions.findIndex((action) => action.key === this._edit.key);
    if (index < 0) return found;
    const editing = actionOf(this._edit);
    const current = same(actions[index], editing);
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    const words: CheckWordsContext = {
      term,
      say: (text) => (text ? this.localize.string(text) : ''),
      types: data.fieldTypes,
      names: elementNames(checked, data.culture, term),
    };
    const settings = this.#type()?.settings ?? [];
    for (const issue of this._check.issues) {
      const steps = parsePath(issue.path);
      if (!steps || steps[0] !== 'actions' || steps[1] !== index) continue;
      const at = settingAtPath(steps.slice(2));
      const own = at.kind === 'setting' ? settings.find((one) => one.name === at.path.split('.')[0]) : undefined;
      if (!current && at.kind === 'setting' && !own) continue;
      // A setting changed since the answer came is being checked again: its old problems go with the change, rather than
      // say "A mail needs someone to go to" beside the recipient just added (16.6's review).
      if (!current && at.kind === 'setting') {
        const name = at.path.split('.')[0] ?? at.path;
        if (!same(actions[index]?.settings?.[name], editing.settings?.[name])) continue;
      }
      const box = this.#boxOf(at);
      const said = issueWords(issue, checked, words).said;
      const text =
        at.kind === 'setting' && box === BOX.whatHappens ? `${this.#say(own?.label) || at.path}: ${said}` : said;
      found.set(box, [...(found.get(box) ?? []), { text, severity: issue.severity }]);
    }
    return found;
  }

  /** The check's problems under a box that show by now: those it had at once, others once it is left or kept. */
  #checkedAt(box: string, found: FoundAt): readonly Shown[] {
    return this._tried || this._shown.has(box) || this.#rowShown(box) ? (found.get(box) ?? []) : [];
  }

  /** Whether a box is a row's of a table that writes its rows, which shows its problems once the table is written. */
  #rowShown(box: string): boolean {
    return (this.#type()?.settings ?? []).some(
      (setting) =>
        ROW_EDITORS.has(setting.editor) &&
        this._shown.has(boxId(setting.name)) &&
        box.startsWith(`${boxId(setting.name)}-`),
    );
  }

  /** The problems shown under a box: one typed that can't be kept, then the check's that show by now. */
  #problemsAt(box: string, found: FoundAt): Shown[] {
    const typed = this._typed.get(box);
    return [...(typed ? [{ text: typed, severity: 'error' as const }] : []), ...this.#checkedAt(box, found)];
  }

  // ---- Keeping ------------------------------------------------------------------------------------------------------

  #focus(setting: ActionSetting) {
    const root = this.shadowRoot;
    // A setting its box's chosen way does not draw: that box, by its own id (`-box`), or the box it is within (ADR-0191).
    const name = setting.kind === 'setting' ? setting.path.split('.')[0] : undefined;
    const within = name ? this.#type()?.settings.find((one) => one.name === name)?.within : undefined;
    const ids = [
      this.#boxOf(setting),
      ...(name ? [`${boxId(name)}-box`] : []),
      ...(within ? [`${boxId(within)}-box`, boxId(within)] : []),
      BOX.name,
    ];
    const box = ids.map((id) => root?.getElementById(id)).find((found) => found);
    if (!box) return;
    const focus =
      box.localName === 'fieldset'
        ? (box.querySelector<HTMLElement>('input:checked') ?? focusableIn(box))
        : box.localName === 'jfb-rich-text'
          ? box
          : (focusableIn(box) ?? box);
    focus?.focus();
    // A box a table draws once its document type is read: the focus goes there when it is, if it is still here then.
    if (box.id !== ids[0] && this.#readingAType()) this.#waiting = { setting, at: root?.activeElement ?? null };
  }

  // A Fix it whose box waits for a document type to be read, and where the focus stood meanwhile (ADR-0192).
  #waiting: { setting: ActionSetting; at: Element | null } | undefined;

  #readingAType(): boolean {
    return [...this.#types.values()].includes('reading');
  }

  async #submit() {
    if (this.#submitting) return;
    // The first box that says why, as the sidebar shows them, takes the focus. Each is drawn: another choice of what
    // happens takes the typed problems of the settings it replaces with them, More options stay open over one, and the
    // boxes of a recipient map take theirs when they go.
    const typed = [...this._typed.keys()][0];
    if (typed) {
      const box = this.shadowRoot?.getElementById(typed);
      box?.closest('details')?.setAttribute('open', '');
      box?.focus();
      return;
    }
    this._tried = true;
    this._checking = true;
    this.#submitting = true;
    try {
      await this.#checker?.now(this.#draft());
    } finally {
      this._checking = false;
      this.#submitting = false;
    }
    // Cancelled while the check was asked: nothing is kept.
    if (!this.isConnected) return;
    if (this._check.status === 'checked') {
      const found = this.#found();
      const first = [...found].find(([, problems]) => problems.some((one) => one.severity === 'error'));
      if (first) {
        await this.updateComplete;
        const box = this.shadowRoot?.getElementById(first[0]);
        const focus = box ? (focusableIn(box) ?? box) : null;
        focus?.focus();
        return;
      }
    }
    this.value = { action: actionOf(this._edit), at: this._at };
    this._submitModal();
  }

  // Tests the action as Add or Update would make it, saved or not (ADR-0190): busy once it runs, after a yes where it
  // asks first; a second press waits for the first.
  async #test() {
    const test = this.data?.test;
    if (!test || this.#testPending) return;
    this.#testPending = true;
    try {
      await test(this.#draft(), this._edit.key, () => (this._testing = true));
    } finally {
      this.#testPending = false;
      this._testing = false;
    }
  }

  #testPending = false;

  #delete() {
    this.value = { deleted: true };
    this._submitModal();
  }

  async #buildRule() {
    const data = this.data;
    if (!data || this.#building) return;
    this.#building = true;
    try {
      const definition = this.#draft();
      const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
      // The builder answers before its sidebar slides out, and its dialog gives the focus back as it closes.
      let shown = false;
      let gone: (() => void) | undefined;
      const closed = new Promise<void>((done) => (gone = done));
      const value = await umbOpenModal(this, RULE_BUILDER_MODAL, {
        data: {
          opened: () => (shown = true),
          closed: () => gone?.(),
          definition,
          form: data.form,
          origin: this._edit.key,
          action: { name: data.action ? this.#nameShown() : null },
          rule: this._edit.rule,
          effect: 'run',
          culture: data.culture,
          languages: data.languages,
          types: typeFactsOf(data.fieldTypes),
          names: elementNames(definition, data.culture, term),
          refusal: () => null,
        },
      }).catch(() => undefined);
      if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
      if (value) this.#change({ ...this._edit, rule: value.rule }, BOX.rule, true);
      await this.updateComplete;
      this.#focus({ kind: 'rule' });
    } finally {
      this.#building = false;
    }
  }

  /** When a trigger runs: Approved, on a form without manual approval, as each entry comes in. */
  #whenOf(trigger: (typeof TRIGGERS)[number]): string {
    return trigger === 'approved' && this.data?.needs.rejected === 'approval' ? 'approvedEach' : trigger;
  }

  /** The action's name as it shows: its own, else its type's. */
  #nameShown(): string {
    return this._edit.name.trim() || this.#say(this.#type()?.name) || this._edit.type;
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #whatHappens(found: FoundAt): TemplateResult {
    const data = this.data;
    const problems = this.#problemsAt(BOX.whatHappens, found);
    // An action of the form keeps its type: another is another action (ADR-0188).
    if (data?.action || data?.readOnly) {
      const type = this.#type();
      return html`<div class="item">
        <p class="label">${this.#term('whatHappens')}</p>
        <p class="kept" id=${BOX.whatHappens} tabindex="-1">
          <uui-icon name=${type?.icon ?? 'icon-lightning'} aria-hidden="true"></uui-icon>
          <span>${this.#say(type?.name) || this._edit.type}</span>
        </p>
        ${data.readOnly ? nothing : html`<p class="help">${this.#term('whatHappensKept')}</p>`}
        ${whys(BOX.whatHappens, problems, this.#weight)}
      </div>`;
    }
    const choose = (choice: ActionChoice) => {
      this._typed = new Map([...this._typed].filter(([box]) => !box.startsWith('setting-')));
      this.#kept.clear();
      this.#change(withChoice(this._edit, choice, this.#say(choice.name)), BOX.whatHappens, true);
    };
    return html`<fieldset
      class="item"
      id=${BOX.whatHappens}
      aria-describedby=${described(BOX.whatHappens, false, problems) ?? nothing}
    >
      <legend class="label">${this.#term('whatHappens')}</legend>
      <div class="choices">
        ${(data?.choices ?? []).map(
          (choice) =>
            html`<label class="choice">
              <input
                type="radio"
                name="what-happens"
                value=${choice.id}
                .checked=${live(this._edit.choice === choice.id)}
                @change=${() => choose(choice)}
              />
              <span class="choice-name"
                ><uui-icon name=${choice.icon} aria-hidden="true"></uui-icon>${this.#say(choice.name)}</span
              >
            </label>`,
        )}
      </div>
      ${whys(BOX.whatHappens, problems, this.#weight)}
    </fieldset>`;
  }

  #name(found: FoundAt): TemplateResult {
    const problems = this.#problemsAt(BOX.name, found);
    if (this.data?.readOnly)
      return html`<div class="item">
        <p class="label">${this.#term('name')}</p>
        <p class="value" id=${BOX.name} tabindex="-1">${this.#nameShown()}</p>
      </div>`;
    const typed = (raw: string, done: boolean) => {
      if (raw.trim().length > MOST_NAME) {
        if (done) this.#typedProblem(BOX.name, this.#problemWords({ kind: 'tooLong', limit: MOST_NAME }));
        return;
      }
      this.#typedProblem(BOX.name, null);
      this.#change({ ...this._edit, name: raw, named: true }, BOX.name, done);
    };
    return html`<div class="item">
      <label class="label" for=${BOX.name}>${this.#term('name')}</label>
      <input
        type="text"
        id=${BOX.name}
        autocomplete="off"
        aria-describedby=${described(BOX.name, false, problems) ?? nothing}
        aria-invalid=${problems.some((problem) => problem.severity === 'error') ? 'true' : 'false'}
        .value=${boxValue(this._edit.name, this._typed.has(BOX.name))}
        @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
        @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
      />
      ${whys(BOX.name, problems, this.#weight)}
    </div>`;
  }

  #trigger(found: FoundAt): TemplateResult {
    const data = this.data;
    const problems = this.#problemsAt(BOX.trigger, found);
    const needs = (trigger: (typeof TRIGGERS)[number]) => data?.needs[trigger] ?? null;
    if (data?.readOnly) {
      const need = needs(this._edit.trigger);
      return html`<div class="item">
        <p class="label">${this.#term('trigger')}</p>
        <p class="value" id=${BOX.trigger} tabindex="-1">${this.#term(`trigger_${this._edit.trigger}`)}</p>
        ${need ? html`<p class="needs">${this.#term(`triggerNeeds_${need}`)}</p>` : nothing}
      </div>`;
    }
    return html`<fieldset
      class="item"
      id=${BOX.trigger}
      aria-describedby=${described(BOX.trigger, false, problems) ?? nothing}
    >
      <legend class="label">${this.#term('trigger')}</legend>
      <div class="choices">
        ${TRIGGERS.map((trigger) => {
          const need = needs(trigger);
          return html`<label class="choice">
            <input
              type="radio"
              name="trigger"
              value=${trigger}
              aria-describedby=${`trigger-needs-${trigger}`}
              .checked=${live(this._edit.trigger === trigger)}
              @change=${() => {
                this._at = null;
                this.#change({ ...this._edit, trigger }, BOX.trigger, true);
              }}
            />
            <span class="choice-name">${this.#term(`trigger_${trigger}`)}</span>
            <span class="choice-line" id=${`trigger-needs-${trigger}`}
              >${need ? this.#term(`triggerNeeds_${need}`) : this.#term(`when_${this.#whenOf(trigger)}`)}</span
            >
          </label>`;
        })}
      </div>
      ${whys(BOX.trigger, problems, this.#weight)}
    </fieldset>`;
  }

  // Where the action runs among its trigger's, which a pointer chooses without dragging a card (WCAG 2.5.7); nothing
  // where it is alone there, or for who may not change it, whose list shows the order.
  #placeBox(): TemplateResult | typeof nothing {
    const { at, count } = this.#place();
    if (count < 2 || this.data?.readOnly) return nothing;
    return html`<div class="item">
      <label class="label" for=${BOX.place}>${this.#term('place')}</label>
      <select
        id=${BOX.place}
        class="short"
        .value=${live(String(at))}
        @change=${(event: Event) => {
          this._at = Number((event.target as HTMLSelectElement).value);
          this.#checker?.changed(this.#draft());
        }}
      >
        ${Array.from(
          { length: count },
          (_, place) =>
            html`<option value=${String(place)} ?selected=${place === at}>
              ${this.#term('placeOf', place + 1, count)}
            </option>`,
        )}
      </select>
    </div>`;
  }

  /**
   * The editors' context: the inspector's words, the boxes' problems, a change of a setting at its path, merge tags for
   * mail, and the action's own editors (ADR-0189).
   */
  #context(found: FoundAt): EditorContext {
    const editor = this.#inspectorContext(found);
    return { ...editor, editors: actionEditors(this.#actionContext(editor)) };
  }

  // The addresses of each choice field's options, kept while another field is chosen (ADR-0189).
  readonly #routes = new Map<string, readonly RecipientRoute[]>();

  // What the webhook's editors keep while the sidebar is open: another choice's values, rows being typed (ADR-0191).
  readonly #kept = new Map<string, unknown>();

  // The document types a content action's table has read, each once while the sidebar is open (ADR-0192).
  readonly #types = new Map<string, TypeRead>();

  #documentType(alias: string): TypeRead {
    const known = this.#types.get(alias);
    if (known) return known;
    const read = this.data?.documentType;
    if (!read) return 'failed';
    this.#types.set(alias, 'reading');
    const settle = (type: TypeRead) => {
      this.#types.set(alias, type);
      this.requestUpdate();
      const waiting = this.#waiting;
      if (!waiting || this.#readingAType()) return;
      this.#waiting = undefined;
      void this.updateComplete.then(() => {
        if (this.shadowRoot?.activeElement === waiting.at) this.#focus(waiting.setting);
      });
    };
    read(alias).then(
      (type) => settle(type === null ? 'gone' : (type ?? 'failed')),
      () => settle('failed'),
    );
    return 'reading';
  }

  /** What the action's own editors need beyond the inspector's context. */
  #actionContext(editor: EditorContext): ActionEditorContext {
    const data = this.data;
    return {
      editor,
      words: this.#term,
      fields: this.#pickFields(),
      layouts: data?.layouts ?? [],
      defaultLayout: data?.defaultLayout ?? '',
      within: (box) =>
        (this.#type()?.settings ?? [])
          .filter((setting) => setting.within === box)
          .map((setting) => ({ setting, value: this.#valueOf(setting) })),
      focus: (id) =>
        void this.updateComplete.then(() => (this.shadowRoot?.getElementById(id) as HTMLElement | null)?.focus()),
      problem: (id, text) => this.#typedProblem(id, text),
      options: (field) => this.#readOptions(field),
      routes: this.#routes,
      forget: (prefix) => {
        if ([...this._typed.keys()].some((box) => box.startsWith(prefix)))
          this._typed = new Map([...this._typed].filter(([box]) => !box.startsWith(prefix)));
      },
      value: (name) => {
        const setting = this.#type()?.settings.find((one) => one.name === name);
        return setting ? this.#valueOf(setting) : settingAt(this._edit, name);
      },
      settingOf: (name) => this.#type()?.settings.find((setting) => setting.name === name),
      tags: (id) => this.#tagPicker(id),
      kept: this.#kept,
      presetMessage: (target) => {
        const preset = data?.choices.find(
          (choice) => choice.type === 'webhook' && choice.settings['target'] === target,
        );
        const message = preset?.settings['message'];
        return typeof message === 'string' ? message : undefined;
      },
      redraw: () => this.requestUpdate(),
      documentType: (alias) => this.#documentType(alias),
      readAgain: (alias) => {
        this.#types.delete(alias);
        this.requestUpdate();
      },
    };
  }

  // "Insert merge tag" under a box of the action, its tags those its place may use in the draft as it is: mail's for an
  // email (ADR-0189), the other actions' for a webhook and a content item's name (ADR-0191, ADR-0192).
  #tagPicker(id: string | (() => string)): TemplateResult | typeof nothing {
    const data = this.data;
    if (!data || data.readOnly) return nothing;
    const box = () => (typeof id === 'string' ? id : id());
    return html`<jfb-merge-tag-picker
      .place=${this._edit.type === 'email' ? 'mail' : 'action'}
      .form=${data.form}
      .owner=${''}
      .culture=${data.culture}
      .definition=${() => this.#draft()}
      .insert=${(tag: string) => insertTagInto(this.shadowRoot?.getElementById(box()), tag)}
    ></jfb-merge-tag-picker>`;
  }

  #inspectorContext(found: FoundAt): EditorContext {
    const data = this.data;
    return {
      // Insert merge tag under each text that takes tags; the webhook's editors place their own (ADR-0191).
      ...(this._edit.type !== 'webhook'
        ? { mergeTags: (_setting: FieldSettingResponseModel, path: string) => this.#tagPicker(boxId(path)) }
        : {}),
      say: this.#say,
      term: (key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args),
      culture: data?.culture ?? '',
      defaultCulture: this.#defaultCulture(),
      problems: this._typed,
      set: (path, setting, parsed, done) => this.#setSetting(path, setting, parsed, done),
      dictionaryLine: (text) =>
        dictionaryLineOf(text, this._lookups, (key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args)),
      dictionaryPicked: () => {
        this.#dictionary.forget();
        this.#dictionaryAsked = '';
      },
      // What the check found, under the box; a value typed that can't be kept the editor says itself.
      found: (id): BoxFound | undefined => {
        const problems = this.#checkedAt(id, found);
        if (problems.length === 0) return undefined;
        const ids = problems.map((_, index) => `${id}-found-${index}`);
        return {
          ids,
          lines: html`${problems.map((problem, index) => whyLine(ids[index] ?? '', problem, this.#weight))}`,
          error: problems.some((problem) => problem.severity === 'error'),
        };
      },
    };
  }

  #setSetting(path: string, setting: FieldSettingResponseModel, parsed: Parsed, done: boolean) {
    const id = boxId(path);
    if (!parsed.ok) {
      if (done) this.#typedProblem(id, this.#problemWords(parsed.problem));
      return;
    }
    this.#typedProblem(id, null);
    // A text per culture takes the typed text in the language shown, its other languages kept (as the inspector writes it).
    const culture = this.data?.culture ?? '';
    const written =
      (setting.kind === 'localizedText' || setting.kind === 'localizedHtml') &&
      (typeof parsed.value === 'string' || parsed.value === undefined)
        ? withText(
            settingAt(this._edit, path) as LocalizedText | undefined,
            culture,
            (parsed.value as string | undefined) ?? '',
            culture === this.#defaultCulture(),
          )
        : parsed.value;
    const value = written === undefined || same(written, setting.default) ? undefined : written;
    this.#change(withSettingAt(this._edit, path, value), id, done);
  }

  // A setting's value: the host's where it locks the setting, else the action's, else the default (as the inspector's).
  #valueOf(setting: FieldSettingResponseModel): unknown {
    return valueOf(this._edit.settings, setting);
  }

  // A setting as it is, for who may not change it: its label and its value in words.
  #shown(setting: FieldSettingResponseModel): TemplateResult | typeof nothing {
    if (setting.lock?.hidden) return nothing;
    const context = {
      say: this.#say,
      term: (key: string, ...args: unknown[]) => this.localize.term(`jfbInspector_${key}`, ...args),
      culture: this.data?.culture ?? '',
    };
    const value = this.#valueOf(setting);
    const own = actionSettingText(this.#actionContext(this.#inspectorContext(new Map())), setting, value);
    const text = (own ?? settingText(context, setting, value)).trim();
    // Umbraco's items as Umbraco's own inputs name them, the setting's own and those of a box's picker within it.
    const items = PICKERS.has(setting.editor) ? this.#shownItems(setting, value) : nothing;
    const within = (this.#type()?.settings ?? [])
      .filter((one) => one.within === setting.name && PICKERS.has(one.editor) && !one.lock?.hidden)
      .map((one) => this.#shownWithin(setting, one));
    // The value keeps its line breaks, so its words stand on the paragraph's own line.
    const words = text || this.#term('readOnlyValue_none');
    const shown = html`<p class="value" id=${boxId(setting.name)} tabindex="-1">${words}</p>`;
    return html`<div class="item">
      <p class="label">${this.#say(setting.label)}</p>
      ${items === nothing ? shown : items} ${within}
    </div>`;
  }

  // A picker within a box, for who may only read it: what it is for, then its items; the item a content action's item
  // goes under is named for where it goes (ADR-0192).
  #shownWithin(box: FieldSettingResponseModel, one: FieldSettingResponseModel): TemplateResult | typeof nothing {
    const items = this.#shownItems(one, this.#valueOf(one));
    if (items === nothing) return nothing;
    const id = `${boxId(one.name)}-label`;
    const label =
      box.editor === 'contentParent'
        ? this.#term(`contentParent_${parentSourceOf(this.#valueOf(box))}`)
        : this.#say(one.label);
    return html`<div class="item" role="group" aria-labelledby=${id}>
      <p class="label" id=${id}>${label}</p>
      ${items}
    </div>`;
  }

  // The items a picker holds, as Umbraco's own input shows them, which no one here can change; nothing for none.
  #shownItems(setting: FieldSettingResponseModel, value: unknown): TemplateResult | typeof nothing {
    const keys = pickedKeys(setting, value);
    if (keys.length === 0) return nothing;
    const id = boxId(setting.name);
    return setting.editor === 'mediaPicker' || setting.editor === 'contentMediaFolder'
      ? html`<umb-input-media id=${id} readonly .selection=${keys}></umb-input-media>`
      : html`<umb-input-document id=${id} readonly .selection=${keys}></umb-input-document>`;
  }

  #settings(found: FoundAt): TemplateResult | typeof nothing {
    const { shown, more } = this.#split(found);
    if (shown.length + more.length === 0) return nothing;
    // A setting under More options with a problem the check shows keeps them open: the problem is said where it is.
    const typedAt = (id: string) => [...this._typed.keys()].some((box) => box === id || box.startsWith(`${id}-`));
    const flagged = more.some((one) => {
      const id = boxId(one.setting.name);
      return this.#checkedAt(id, found).length > 0 || typedAt(id);
    });
    const showsMore = this._more || flagged;
    return html`<div class="settings">
      ${shown.map((one) => one.template)} ${showsMore ? more.map((one) => one.template) : nothing}
      ${
        more.length > 0 && !flagged
          ? html`<button
              type="button"
              class="more"
              aria-expanded=${showsMore ? 'true' : 'false'}
              @click=${() => (this._more = !this._more)}
            >
              ${this.localize.term(showsMore ? 'jfbInspector_fewerOptions' : 'jfbInspector_moreOptions', more.length)}
            </button>`
          : nothing
      }
    </div>`;
  }

  #stopOnFailure(found: FoundAt): TemplateResult {
    const problems = this.#problemsAt(BOX.stopOnFailure, found);
    if (this.data?.readOnly) {
      // The value keeps its line breaks, so its word stands on the paragraph's own line.
      const word = this.#term(this._edit.stopOnFailure ? 'readOnlyValue_on' : 'readOnlyValue_off');
      return html`<div class="item">
        <p class="label">${this.#term('stopOnFailure')}</p>
        <p class="value" id=${BOX.stopOnFailure} tabindex="-1">${word}</p>
      </div>`;
    }
    return html`<div class="item">
      <div class="item-switch">
        <input
          type="checkbox"
          role="switch"
          id=${BOX.stopOnFailure}
          .checked=${live(this._edit.stopOnFailure)}
          aria-describedby=${described(BOX.stopOnFailure, true, problems) ?? nothing}
          @change=${(event: Event) =>
            this.#change(
              { ...this._edit, stopOnFailure: (event.target as HTMLInputElement).checked },
              BOX.stopOnFailure,
              true,
            )}
        />
        <label class="label" for=${BOX.stopOnFailure}>${this.#term('stopOnFailure')}</label>
      </div>
      <p class="help" id="stop-on-failure-help">${this.#term('stopOnFailureHelp')}</p>
      ${whys(BOX.stopOnFailure, problems, this.#weight)}
    </div>`;
  }

  #rule(found: FoundAt): TemplateResult {
    const data = this.data;
    if (!data) return html``;
    const problems = this.#problemsAt(BOX.rule, found);
    const rule = this._edit.rule;
    const words = rule
      ? ruleWords(
          inspectorRuleWords(this.#draft(), data.fieldTypes, data.culture, data.languages, (key, ...args) =>
            this.localize.term(key, ...args),
          ),
          rule,
        )
      : this.#term('noRule');
    const said = rule && rule.enabled === false ? this.#term('ruleOff', words) : words;
    if (data.readOnly)
      return html`<div class="item">
        <p class="label">${this.#term('rule')}</p>
        <p class="rule-words" id=${BOX.rule} tabindex="-1">${said}</p>
      </div>`;
    return html`<div class="item">
      <p class="label" id="rule-label">${this.#term('rule')}</p>
      <p class="rule-words" id="rule-words">${said}</p>
      <button
        type="button"
        id=${BOX.rule}
        class="rule-button"
        aria-describedby=${['rule-words', described(BOX.rule, false, problems)].filter(Boolean).join(' ')}
        @click=${() => void this.#buildRule()}
      >
        ${this.#term(rule ? 'changeRule' : 'addRule')}
      </button>
      ${whys(BOX.rule, problems, this.#weight)}
    </div>`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const found = this.#found();
    const readOnly = data.readOnly;
    const email = this.#isEmail();
    const view = settingAt(this._edit, 'view');
    return html`<umb-body-layout headline=${data.action ? this.#nameShown() : this.#term('headlineNew')}>
      <div class="room">
        <div class="layout ${email ? 'with-preview' : ''}">
          <div class="action">
            ${this.#whatHappens(found)} ${this.#name(found)} ${this.#trigger(found)} ${this.#placeBox()}
            ${this.#settings(found)} ${this.#stopOnFailure(found)} ${this.#rule(found)}
          </div>
          ${
            email
              ? html`<jfb-email-preview
                  .state=${this._preview}
                  .culture=${data.culture}
                  .view=${typeof view === 'string' ? view : ''}
                  .retry=${() => this.#previewer?.retry()}
                ></jfb-email-preview>`
              : nothing
          }
          ${
            data.action && !readOnly
              ? html`<div class="delete-body">
                  <uui-button
                    look="secondary"
                    label=${this.#term('deleteAction')}
                    @click=${() => this.#delete()}
                  ></uui-button>
                </div>`
              : nothing
          }
          <div class="visually-hidden" role="status">${this._testing ? this.#term('testing') : ''}</div>
        </div>
      </div>
      ${
        data.action && !readOnly
          ? html`<uui-button
              slot="footer-info"
              class="delete-footer"
              look="secondary"
              label=${this.#term('deleteAction')}
              @click=${() => this.#delete()}
            ></uui-button>`
          : nothing
      }
      ${
        data.test && !readOnly
          ? html`<uui-button
              slot="footer-info"
              class="test"
              look="outline"
              label=${this.#term('test')}
              title=${testHint(this.#type()?.test, this.#term) ?? nothing}
              .state=${this._testing ? 'waiting' : undefined}
              @click=${() => void this.#test()}
            ></uui-button>`
          : nothing
      }
      <div slot="actions" class="actions">
        ${
          readOnly
            ? html`<uui-button label=${this.localize.term('general_close')} @click=${this._rejectModal}></uui-button>`
            : html`<uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
                <uui-button
                  look="primary"
                  color="positive"
                  label=${this.#term(data.action ? 'update' : 'add')}
                  .state=${this._checking ? 'waiting' : undefined}
                  @click=${() => void this.#submit()}
                ></uui-button>`
        }
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-action-modal')) customElements.define('jfb-action-modal', ActionModalElement);

export { ActionModalElement as element };
