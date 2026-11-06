// A comparison of two versions in words (ROADMAP 10.3a, ADR-0196): the server sends each element that differs with the
// path and both values of every property that changed (ADR-0045), and the backoffice says them in the reader's language,
// in four parts: fields (with pages and groups), settings (with After sending's outcomes), actions and rules. Every
// property of schema v1 has a name here, its Settings view's or inspector's where it has one; a type's own settings are
// named by the labels the type serves. Pure: what the words need comes in a context.
import type {
  ActionTypeResponseModel,
  DefinitionChangeResponseModel,
  FieldSettingResponseModel,
  FieldTypeResponseModel,
} from '../../api';
import type { DesignDefinition, LocalizedText } from '../definition';
import { textIn } from '../preview/field-models';
import { elementNames, inspectorRuleWords } from '../rules/rule-names';
import { ruleWords } from '../rules/rule-words';
import { outcomeName, type DesignOutcome } from '../settings/outcome-model';

type Term = (key: string, ...args: unknown[]) => string;
type Json = Readonly<Record<string, unknown>>;

/** What the words need: the dictionaries, the site's languages and types, and the two versions' definitions. */
export interface ChangeWordsContext {
  /** A word by its full key, `jfbHistory_...` and the other areas' alike. */
  readonly term: Term;
  /** A `#` key or a plain text, in the reader's language. */
  readonly say: (text: string | null | undefined) => string;
  /** The site's default language, whose texts name elements and stand for a text of every language. */
  readonly culture: string;
  /** The reader's language, for numbers and times. */
  readonly locale: string;
  readonly languages: readonly { readonly culture: string; readonly name: string }[];
  readonly fieldTypes: Readonly<Record<string, FieldTypeResponseModel>>;
  readonly actionTypes: Readonly<Record<string, ActionTypeResponseModel>>;
  /** The older and the newer version's definitions; null where one could not be read. */
  readonly older: DesignDefinition | null;
  readonly newer: DesignDefinition | null;
  /** The names of the site's pages read so far, by key, for a redirect. */
  readonly pages?: ReadonlyMap<string, string>;
  /** The message visitors get without an outcome of their own. */
  readonly defaultMessage?: string;
}

/** A property that changed: the element and the property, and its value in each version. */
export interface ChangeRow {
  readonly what: string;
  readonly older: string;
  readonly newer: string;
}

export type ChangePartName = 'fields' | 'settings' | 'actions' | 'rules';

/**
 * One difference of Review both versions in words (ROADMAP 10.3c, ADR-0200): what it is about, then a row for each
 * property that differs, the older side's value first, its place where it moved, and whether it is in the form at all
 * where one side has it and the other not.
 */
export interface DifferenceWords {
  readonly title: string;
  readonly rows: readonly ChangeRow[];
}

/** A part of the comparison: what was added, removed or moved in sentences, then what changed in rows. */
export interface ChangePart {
  readonly name: ChangePartName;
  readonly lines: readonly string[];
  readonly rows: readonly ChangeRow[];
}

/** How a property's value reads. */
type Value =
  | 'flag'
  | 'number'
  | 'line'
  | 'texts'
  | 'html'
  | 'instant'
  | 'field'
  | 'groups'
  | 'content'
  | 'image'
  | 'list'
  | 'libraryList'
  | 'lines'
  | 'checks'
  | 'fieldType'
  | 'actionType'
  | 'any'
  | `enum:${string}`;

/**
 * A property of the schema: its name's key, how its value reads, and the object it holds, where it holds one. The
 * canonical form writes every switch, number and choice (ADR-0034), so a side without a value is a side without the
 * object that holds it, and says "none".
 */
interface Property {
  readonly term: string;
  readonly value: Value;
  readonly holds?: string;
}

/** The properties of the schema's objects that a comparison names, by the schema's names (`schemas/jfb-form.v1.schema.json`). */
export const VOCABULARY: Readonly<Record<string, Readonly<Record<string, Property>>>> = {
  FormSubmissionSettings: {
    submitLabel: { term: 'jfbSettings_submitLabel', value: 'texts' },
    nextLabel: { term: 'jfbSettings_nextLabel', value: 'texts' },
    backLabel: { term: 'jfbSettings_backLabel', value: 'texts' },
    summaryPage: { term: 'jfbSettings_summaryPage', value: 'flag' },
    progress: { term: 'jfbHistory_property_progress', value: 'enum:ProgressIndicator' },
    store: { term: 'jfbSettings_store', value: 'enum:EntryStorage' },
    storeDays: { term: 'jfbHistory_property_storeDays', value: 'number' },
    approval: { term: 'jfbSettings_approval', value: 'enum:ApprovalMode' },
    verification: { term: 'jfbSettings_verification', value: 'any', holds: 'EmailVerification' },
    resume: { term: 'jfbHistory_property_resume', value: 'any', holds: 'SaveAndResume' },
    entryTitle: { term: 'jfbSettings_entryTitle', value: 'line' },
    entryReference: { term: 'jfbSettings_reference', value: 'any', holds: 'EntryReferenceFormat' },
  },
  EmailVerification: {
    emailField: { term: 'jfbSettings_verificationField', value: 'field' },
    days: { term: 'jfbSettings_verificationDays', value: 'number' },
  },
  SaveAndResume: {
    days: { term: 'jfbHistory_property_resumeDays', value: 'number' },
  },
  EntryReferenceFormat: {
    prefix: { term: 'jfbSettings_referencePrefix', value: 'line' },
    year: { term: 'jfbSettings_referenceYear', value: 'flag' },
    digits: { term: 'jfbSettings_referenceDigits', value: 'number' },
  },
  FormAvailabilitySettings: {
    opensAt: { term: 'jfbSettings_opensAt', value: 'instant' },
    closesAt: { term: 'jfbSettings_closesAt', value: 'instant' },
    maxEntries: { term: 'jfbSettings_maxEntries', value: 'number' },
    closedMessage: { term: 'jfbSettings_closedMessage', value: 'html' },
    members: { term: 'jfbSettings_access', value: 'enum:MemberAccess' },
    memberGroups: { term: 'jfbSettings_memberGroups', value: 'groups' },
    onePerMember: { term: 'jfbHistory_property_onePerMember', value: 'flag' },
  },
  FormProtectionSettings: {
    level: { term: 'jfbSettings_protection', value: 'enum:ProtectionLevel' },
    captcha: { term: 'jfbHistory_property_captcha', value: 'any', holds: 'CaptchaChoice' },
    contentRules: { term: 'jfbSettings_contentRules', value: 'enum:ContentRuleLevel' },
  },
  CaptchaChoice: {
    provider: { term: 'jfbHistory_property_captchaProvider', value: 'line' },
    settings: { term: 'jfbHistory_property_captchaSettings', value: 'any' },
  },
  FormPrivacySettings: {
    retentionDays: { term: 'jfbHistory_property_retentionDays', value: 'any', holds: 'RetentionDaysByState' },
    ip: { term: 'jfbHistory_property_ip', value: 'enum:IPHandling' },
    insights: { term: 'jfbHistory_property_insights', value: 'flag' },
  },
  RetentionDaysByState: {
    submitted: { term: 'jfbHistory_property_retention_submitted', value: 'number' },
    approved: { term: 'jfbHistory_property_retention_approved', value: 'number' },
    rejected: { term: 'jfbHistory_property_retention_rejected', value: 'number' },
    spam: { term: 'jfbHistory_property_retention_spam', value: 'number' },
  },
  FormAppearanceSettings: {
    theme: { term: 'jfbSettings_theme', value: 'line' },
    requiredMarker: { term: 'jfbSettings_requiredMarker', value: 'enum:RequiredMarker' },
    validationSummary: { term: 'jfbSettings_validationSummary', value: 'flag' },
    choiceLayout: { term: 'jfbSettings_choiceLayout', value: 'enum:ChoiceLayout' },
    cssClass: { term: 'jfbSettings_cssClass', value: 'line' },
  },
  FormDeveloperSettings: {
    headless: { term: 'jfbSettings_headless', value: 'flag' },
    queryValues: { term: 'jfbSettings_queryValues', value: 'lines' },
    contextValues: { term: 'jfbSettings_contextValues', value: 'lines' },
  },
  FormPage: {
    title: { term: 'jfbInspector_property_title', value: 'texts' },
    description: { term: 'jfbHistory_property_description', value: 'html' },
  },
  FormGroup: {
    title: { term: 'jfbInspector_property_title', value: 'texts' },
    description: { term: 'jfbHistory_property_description', value: 'html' },
    columns: { term: 'jfbInspector_property_columns', value: 'number' },
    style: { term: 'jfbInspector_property_style', value: 'enum:GroupStyle' },
  },
  FormField: {
    type: { term: 'jfbHistory_property_type', value: 'fieldType' },
    alias: { term: 'jfbInspector_property_alias', value: 'line' },
    formerAliases: { term: 'jfbInspector_property_alias', value: 'lines' },
    span: { term: 'jfbInspector_property_span', value: 'number' },
    label: { term: 'jfbInspector_property_label', value: 'texts' },
    hideLabel: { term: 'jfbInspector_property_hideLabel', value: 'flag' },
    help: { term: 'jfbInspector_property_help', value: 'texts' },
    placeholder: { term: 'jfbInspector_property_placeholder', value: 'texts' },
    required: { term: 'jfbInspector_property_required', value: 'flag' },
    messages: { term: 'jfbHistory_property_messages', value: 'any', holds: 'FieldMessages' },
    defaultValue: { term: 'jfbInspector_property_defaultValue', value: 'any' },
    prefill: { term: 'jfbInspector_property_prefill', value: 'any', holds: 'FieldPrefill' },
    sensitive: { term: 'jfbInspector_property_sensitive', value: 'flag' },
    includeInEmails: { term: 'jfbInspector_property_includeInEmails', value: 'flag' },
    keep: { term: 'jfbInspector_property_keep', value: 'enum:FieldKeep' },
    autocomplete: { term: 'jfbInspector_property_autocomplete', value: 'line' },
    cssClass: { term: 'jfbInspector_property_cssClass', value: 'line' },
    validation: { term: 'jfbHistory_property_validation', value: 'checks' },
    options: { term: 'jfbInspector_property_options', value: 'any', holds: 'FieldOptions' },
    rows: { term: 'jfbInspector_property_rows', value: 'list' },
    settings: { term: 'jfbHistory_property_settings', value: 'any' },
  },
  FieldMessages: Object.fromEntries(
    [
      'required',
      'invalid',
      'minLength',
      'maxLength',
      'minWords',
      'maxWords',
      'min',
      'max',
      'step',
      'dateWindow',
      'selections',
      'fileType',
      'fileSize',
      'fileCount',
      'full',
      'confirmation',
      'digits',
      'rowCount',
    ].map((kind) => [kind, { term: `jfbInspector_message_${kind}`, value: 'texts' } satisfies Property]),
  ),
  FieldPrefill: {
    query: { term: 'jfbInspector_prefill_query', value: 'line' },
    member: { term: 'jfbInspector_prefill_member', value: 'line' },
    context: { term: 'jfbInspector_prefill_context', value: 'line' },
    memberMode: { term: 'jfbInspector_prefill_memberMode', value: 'enum:PrefillMemberMode' },
  },
  FieldOptions: {
    source: { term: 'jfbHistory_property_optionSource', value: 'enum:OptionSource' },
    items: { term: 'jfbInspector_property_options', value: 'list' },
    list: { term: 'jfbHistory_property_optionList', value: 'libraryList' },
    config: { term: 'jfbHistory_property_optionConfig', value: 'any' },
    parent: { term: 'jfbHistory_property_optionParent', value: 'field' },
  },
  FieldOption: {
    value: { term: 'jfbHistory_property_value', value: 'line' },
    label: { term: 'jfbInspector_property_label', value: 'texts' },
    description: { term: 'jfbHistory_property_description', value: 'texts' },
    image: { term: 'jfbHistory_property_image', value: 'image' },
    places: { term: 'jfbHistory_property_places', value: 'number' },
    parentValues: { term: 'jfbHistory_property_parentValues', value: 'lines' },
  },
  GridRow: {
    key: { term: 'jfbHistory_property_value', value: 'line' },
    label: { term: 'jfbInspector_property_label', value: 'texts' },
    required: { term: 'jfbInspector_property_required', value: 'flag' },
  },
  FormAction: {
    type: { term: 'jfbHistory_property_type', value: 'actionType' },
    name: { term: 'jfbHistory_property_name', value: 'line' },
    enabled: { term: 'jfbHistory_property_enabled', value: 'flag' },
    trigger: { term: 'jfbHistory_property_trigger', value: 'enum:ActionTrigger' },
    stopOnFailure: { term: 'jfbHistory_property_stopOnFailure', value: 'flag' },
    settings: { term: 'jfbHistory_property_settings', value: 'any' },
  },
  FormOutcome: {
    kind: { term: 'jfbSettings_outcome_kind', value: 'enum:OutcomeKind' },
    message: { term: 'jfbSettings_outcome_message', value: 'html' },
    showAnswers: { term: 'jfbSettings_outcome_showAnswers', value: 'flag' },
    redirect: { term: 'jfbHistory_property_redirect', value: 'any', holds: 'OutcomeRedirect' },
  },
  OutcomeRedirect: {
    content: { term: 'jfbSettings_outcome_page', value: 'content' },
    url: { term: 'jfbSettings_outcome_address', value: 'line' },
    delaySeconds: { term: 'jfbSettings_outcome_delay', value: 'number' },
  },
};

/** What each value of the schema's enumerations is called, by enumeration and value: its Settings view's word where it has one. */
export const VALUE_TERMS: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  GroupStyle: { plain: 'jfbHistory_value_GroupStyle_plain', card: 'jfbHistory_value_GroupStyle_card' },
  FieldKeep: { always: 'jfbInspector_keep_always', untilActionsRun: 'jfbInspector_keep_untilActionsRun' },
  PrefillMemberMode: {
    show: 'jfbHistory_value_PrefillMemberMode_show',
    hide: 'jfbHistory_value_PrefillMemberMode_hide',
  },
  OptionSource: Object.fromEntries(
    ['items', 'list', 'content', 'dataType', 'url', 'file', 'countries', 'code'].map((value) => [
      value,
      `jfbHistory_value_OptionSource_${value}`,
    ]),
  ),
  ActionTrigger: Object.fromEntries(
    ['submitted', 'approved', 'rejected', 'verified'].map((value) => [
      value,
      `jfbHistory_value_ActionTrigger_${value}`,
    ]),
  ),
  OutcomeKind: { message: 'jfbSettings_outcome_kind_message', redirect: 'jfbSettings_outcome_kind_redirect' },
  ProgressIndicator: {
    steps: 'jfbSettings_progress_steps',
    bar: 'jfbSettings_progress_bar',
    none: 'jfbSettings_progress_none',
  },
  EntryStorage: {
    always: 'jfbSettings_store_always',
    never: 'jfbSettings_store_never',
    days: 'jfbHistory_value_EntryStorage_days',
  },
  ApprovalMode: {
    automatic: 'jfbHistory_value_ApprovalMode_automatic',
    manual: 'jfbHistory_value_ApprovalMode_manual',
  },
  MemberAccess: {
    anyone: 'jfbSettings_access_anyone',
    members: 'jfbSettings_access_members',
    groups: 'jfbSettings_access_groups',
  },
  ProtectionLevel: {
    inherit: 'jfbHistory_value_ProtectionLevel_inherit',
    standard: 'jfbSettings_protection_standard',
    proofOfWork: 'jfbSettings_protection_proofOfWork',
    captcha: 'jfbSettings_protection_captcha',
  },
  ContentRuleLevel: {
    inherit: 'jfbHistory_value_ContentRuleLevel_inherit',
    off: 'jfbSettings_contentRules_off',
    normal: 'jfbSettings_contentRules_normal',
    strict: 'jfbSettings_contentRules_strict',
  },
  IPHandling: Object.fromEntries(
    ['inherit', 'none', 'anonymised', 'full'].map((value) => [value, `jfbHistory_value_IPHandling_${value}`]),
  ),
  RequiredMarker: {
    inherit: 'jfbHistory_value_RequiredMarker_inherit',
    required: 'jfbSettings_requiredMarker_required',
    optional: 'jfbSettings_requiredMarker_optional',
    none: 'jfbSettings_requiredMarker_none',
  },
  ChoiceLayout: {
    vertical: 'jfbSettings_choiceLayout_vertical',
    horizontal: 'jfbSettings_choiceLayout_horizontal',
    cards: 'jfbSettings_choiceLayout_cards',
  },
};

/** The schema's object of each element kind, and of each settings group. */
const ROOTS: Readonly<Record<string, string>> = {
  page: 'FormPage',
  group: 'FormGroup',
  field: 'FormField',
  action: 'FormAction',
  outcome: 'FormOutcome',
  submission: 'FormSubmissionSettings',
  availability: 'FormAvailabilitySettings',
  protection: 'FormProtectionSettings',
  privacy: 'FormPrivacySettings',
  appearance: 'FormAppearanceSettings',
  developer: 'FormDeveloperSettings',
};

const PARTS: Readonly<Record<string, ChangePartName>> = {
  page: 'fields',
  group: 'fields',
  field: 'fields',
  settings: 'settings',
  outcome: 'settings',
  action: 'actions',
  rule: 'rules',
  validationRule: 'rules',
};

/** A settings group's name: the Settings view's, and History's own for Privacy, which that view gains with 11.5. */
export const groupTerm = (group: string): string =>
  group === 'privacy' ? 'jfbHistory_group_privacy' : `jfbSettings_group_${group}`;

/** The longest text a cell says before it is cut. */
const LONGEST = 160;

const isObject = (value: unknown): value is Json =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** A text without its markup, its spaces made one. */
const plain = (html: string): string =>
  html
    .replace(/<\/?(?:p|div|br|li|ul|ol|h[1-6]|blockquote|table|tr|td|th)\b[^>]*>/gi, ' ')
    .replace(/<[^>]*>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/\s+/g, ' ')
    .trim();

const ELLIPSIS = String.fromCharCode(0x2026);

/** A text cut to what a cell holds. */
const cut = (text: string): string =>
  text.length > LONGEST ? `${text.slice(0, LONGEST - 1).trimEnd()}${ELLIPSIS}` : text;

/** Whether a path's segment is a language's code: "" for the default, "de", "en-US". */
const isCulture = (segment: string): boolean => segment === '' || /^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$/.test(segment);

/** Which side of the comparison a value is from. */
type Side = 'older' | 'newer';

/** The words of one comparison. */
class Words {
  readonly #context: ChangeWordsContext;
  readonly #names: Record<Side, Record<string, string>>;

  constructor(context: ChangeWordsContext) {
    this.#context = context;
    const names = (definition: DesignDefinition | null) =>
      definition ? elementNames(definition, context.culture, context.term) : {};
    this.#names = { older: names(context.older), newer: names(context.newer) };
  }

  #definition(side: Side): DesignDefinition | null {
    return side === 'older' ? this.#context.older : this.#context.newer;
  }

  /** An element's name by its key, from the side that has it, the newer first. */
  nameOf(key: string | null | undefined, side?: Side): string {
    if (!key) return '';
    const sides: Side[] = side ? [side, side === 'older' ? 'newer' : 'older'] : ['newer', 'older'];
    for (const one of sides) {
      const name = this.#names[one][key];
      if (name) return name;
    }
    const action = [this.#context.newer, this.#context.older]
      .flatMap((definition) => (definition?.['actions'] ?? []) as readonly Json[])
      .find((one) => one['key'] === key);
    if (typeof action?.['name'] === 'string' && action['name'] !== '') return action['name'];
    const outcome = this.#outcome(key);
    return outcome ? this.#outcomeName(outcome) : '';
  }

  #outcome(key: string, side?: Side): DesignOutcome | undefined {
    const definitions = side ? [this.#definition(side)] : [this.#context.newer, this.#context.older];
    return definitions
      .flatMap((definition) => (definition?.['outcomes'] ?? []) as readonly DesignOutcome[])
      .find((one) => one.key === key);
  }

  #outcomeName(outcome: DesignOutcome): string {
    const term: Term = (key, ...args) => this.#context.term(`jfbSettings_${key}`, ...args);
    return outcomeName(
      outcome,
      this.#context.culture,
      this.#context.defaultMessage ?? '',
      this.#context.pages ?? new Map(),
      term,
    );
  }

  /** What a change is about, by its name: a field's label, a page's title, an action's name, a settings group. */
  elementOf(change: DefinitionChangeResponseModel): string {
    const term = this.#context.term;
    if (change.kind === 'settings') return term(groupTerm(change.settingsGroup ?? ''));
    if (change.kind === 'outcome') {
      const own = (change.after ?? change.before) as DesignOutcome | null | undefined;
      const outcome = own ? ({ ...own, key: change.key ?? '' } as DesignOutcome) : undefined;
      return term('jfbHistory_what', term('jfbSettings_group_after'), outcome ? this.#outcomeName(outcome) : '');
    }
    if (change.kind === 'validationRule') {
      const own = (change.after ?? change.before) as Json | null | undefined;
      const field = typeof own?.['field'] === 'string' ? own['field'] : '';
      return term('jfbHistory_check', this.nameOf(field) || term('jfbHistory_aField'));
    }
    return this.nameOf(change.key) || change.label || change.alias || term('jfbHistory_unnamed');
  }

  /** A rule, or a form-level check, as the rules builder says it, in the version that holds it. */
  ruleOf(rule: unknown, side: Side, validation: boolean): string {
    const definition = this.#definition(side);
    if (!definition || !isObject(rule)) return this.#context.term('jfbHistory_none');
    const context = inspectorRuleWords(
      definition,
      this.#context.fieldTypes,
      this.#context.culture,
      this.#context.languages,
      this.#context.term,
    );
    const words = ruleWords(
      context,
      validation
        ? {
            effect: 'message',
            match: rule['match'] as string,
            conditions: rule['conditions'] as unknown[],
            message: rule['message'],
          }
        : {
            effect: typeof rule['effect'] === 'string' ? rule['effect'] : 'show',
            match: rule['match'] as string,
            conditions: rule['conditions'] as unknown[],
          },
    );
    return rule['enabled'] === false ? this.#context.term('jfbHistory_ruleOff', words) : words;
  }

  /** A language's name, by its culture. */
  languageOf(culture: string): string {
    const known = this.#context.languages.find((language) => language.culture.toLowerCase() === culture.toLowerCase());
    if (known) return known.name;
    try {
      return new Intl.DisplayNames([this.#context.locale], { type: 'language' }).of(culture) ?? culture;
    } catch {
      return culture;
    }
  }

  #yesNo(value: boolean): string {
    return this.#context.term(value ? 'jfbHistory_yes' : 'jfbHistory_no');
  }

  #number(value: number): string {
    return new Intl.NumberFormat(this.#context.locale).format(value);
  }

  /** A field of a version by its name, or what a key the version no longer has stands for. */
  #field(key: unknown, side: Side): string {
    return typeof key === 'string' && key !== ''
      ? this.nameOf(key, side) || this.#context.term('jfbHistory_aField')
      : this.#context.term('jfbHistory_none');
  }

  /** A value as a cell says it, by how it reads. */
  value(value: unknown, how: Value, side: Side, holds?: string): string {
    const term = this.#context.term;
    if (value === null || value === undefined || value === '') return term('jfbHistory_none');
    if (holds && isObject(value)) return this.#object(value, holds, side);
    if (how.startsWith('enum:') && typeof value === 'string') {
      const key = VALUE_TERMS[how.slice('enum:'.length)]?.[value];
      return key ? term(key) : value;
    }
    switch (how) {
      case 'flag':
        return typeof value === 'boolean' ? this.#yesNo(value) : this.any(value, side);
      case 'number':
        return typeof value === 'number' ? this.#number(value) : this.any(value, side);
      case 'texts':
      case 'html':
        if (isObject(value)) return this.value(textIn(value as LocalizedText, this.#context.culture), how, side);
        return typeof value === 'string' ? cut(how === 'html' ? plain(value) : value) : this.any(value, side);
      case 'instant':
        return typeof value === 'string' ? this.#instant(value) : this.any(value, side);
      case 'field':
        return this.#field(value, side);
      case 'groups':
        return Array.isArray(value) ? term('jfbHistory_groups', value.length) : this.any(value, side);
      case 'content':
        return (typeof value === 'string' && this.#context.pages?.get(value)) || term('jfbHistory_aPage');
      case 'image':
        return term('jfbHistory_anImage');
      case 'libraryList':
        return term('jfbHistory_aLibraryList');
      case 'checks':
        return Array.isArray(value) ? this.#checks(value, side) : this.any(value, side);
      case 'fieldType': {
        const type = typeof value === 'string' ? this.#context.fieldTypes[value] : undefined;
        return type ? this.#context.say(type.name) : String(value);
      }
      case 'actionType': {
        const type = typeof value === 'string' ? this.#context.actionTypes[value] : undefined;
        return type ? this.#context.say(type.name) : String(value);
      }
      default:
        return this.any(value, side);
    }
  }

  #instant(text: string): string {
    const at = new Date(text);
    if (Number.isNaN(at.getTime())) return text;
    return new Intl.DateTimeFormat(this.#context.locale, { dateStyle: 'medium', timeStyle: 'short' }).format(at);
  }

  /** A field's checks: each by what it asks. */
  #checks(checks: readonly unknown[], side: Side): string {
    const term = this.#context.term;
    if (checks.length === 0) return term('jfbHistory_none');
    return checks
      .map((check) => {
        const own = isObject(check) ? check : {};
        if (own['kind'] === 'pattern') return term('jfbHistory_check_pattern', String(own['pattern'] ?? ''));
        if (own['kind'] === 'matchesField')
          return term('jfbHistory_check_matchesField', this.#field(own['field'], side));
        if (own['kind'] === 'uniquePerForm') return term('jfbHistory_check_uniquePerForm');
        return String(own['kind'] ?? '');
      })
      .join('; ');
  }

  /** An object of the schema said property by property: "Address to confirm: Email, Link valid for (days): 7". */
  #object(value: Json, holds: string, side: Side): string {
    const properties = VOCABULARY[holds] ?? {};
    const parts = Object.entries(value)
      .filter(([, inner]) => inner !== null && inner !== undefined)
      .map(([name, inner]) => {
        const property = properties[name];
        const said = property ? this.value(inner, property.value, side, property.holds) : this.any(inner, side);
        return `${property ? this.#context.term(property.term) : name}: ${said}`;
      });
    return parts.length > 0 ? cut(parts.join(', ')) : this.#context.term('jfbHistory_none');
  }

  /** A value whose kind the vocabulary does not know: as its JSON reads, said plainly. */
  any(value: unknown, side: Side): string {
    const term = this.#context.term;
    if (value === null || value === undefined || value === '') return term('jfbHistory_none');
    if (typeof value === 'boolean') return this.#yesNo(value);
    if (typeof value === 'number') return this.#number(value);
    if (typeof value === 'string') return cut(value.includes('<') ? plain(value) : value);
    if (Array.isArray(value)) {
      if (value.length === 0) return term('jfbHistory_none');
      if (value.every((item) => typeof item === 'string' || typeof item === 'number'))
        return cut(value.map(String).join(', '));
      const named = value.map((item) => this.#itemName(item, side));
      return named.every((name) => name !== '') ? cut(named.join(', ')) : term('jfbHistory_items', value.length);
    }
    if (isObject(value)) {
      const texts = Object.values(value);
      if (texts.length > 0 && texts.every((text) => typeof text === 'string') && '' in value)
        return this.value(value, 'texts', side);
      return term('jfbHistory_settingsCount', Object.keys(value).length);
    }
    return String(value);
  }

  /** What an item of a list is called: its address, label or name, a field by its name. */
  #itemName(item: unknown, side: Side): string {
    if (!isObject(item)) return typeof item === 'string' ? item : '';
    for (const name of ['address', 'email', 'name', 'value'])
      if (typeof item[name] === 'string' && item[name] !== '') return item[name];
    if (isObject(item['label'])) return textIn(item['label'] as LocalizedText, this.#context.culture);
    if (typeof item['field'] === 'string') return this.#field(item['field'], side);
    return '';
  }

  /** A type's own setting by its label, a nested one's within it; its own name where the type serves none. */
  #setting(
    schema: readonly FieldSettingResponseModel[] | undefined,
    path: readonly string[],
  ): { name: string; setting: FieldSettingResponseModel | undefined; rest: readonly string[] } {
    const [name = '', inner] = path;
    const setting = schema?.find((one) => one.name === name);
    const nested = inner !== undefined ? setting?.settings.find((one) => one.name === inner) : undefined;
    if (nested)
      return {
        name: `${this.#context.say(setting?.label) || name}: ${this.#context.say(nested.label) || inner}`,
        setting: nested,
        rest: path.slice(2),
      };
    return { name: this.#context.say(setting?.label) || name, setting, rest: path.slice(1) };
  }

  /** A type's setting's value: a choice by its label, else as it reads. */
  #settingValue(setting: FieldSettingResponseModel | undefined, value: unknown, side: Side): string {
    const choice = setting?.choices.find((one) => one.value === value);
    if (choice) return this.#context.say(choice.label);
    if (setting?.kind === 'boolean' && typeof value === 'boolean') return this.#yesNo(value);
    return this.any(value, side);
  }

  /** A property's name and how its values read, by its path from its element. */
  property(
    change: DefinitionChangeResponseModel,
    path: readonly string[],
  ): {
    name: string;
    say: (value: unknown, side: Side) => string;
    /** Whether its value may be a list said by what was added and what was removed. */
    list?: boolean;
  } {
    const term = this.#context.term;
    let object = ROOTS[change.kind === 'settings' ? (change.settingsGroup ?? '') : change.kind] ?? '';
    const names: string[] = [];
    for (let at = 0; at < path.length; at++) {
      const segment = path[at] ?? '';
      // A type's own settings, a field's or an action's.
      if (segment === 'settings' && (object === 'FormField' || object === 'FormAction')) {
        const schema =
          object === 'FormField'
            ? this.#context.fieldTypes[this.#typeOf(change)]?.settings
            : this.#context.actionTypes[this.#typeOf(change)]?.settings;
        if (at + 1 >= path.length)
          return {
            name: this.#joined([...names, term('jfbHistory_property_settings')]),
            say: (v, s) => this.any(v, s),
          };
        const { name, setting, rest } = this.#setting(schema, path.slice(at + 1));
        names.push(name);
        // A text setting's language comes last in its path; what is deeper keeps its own names.
        if (rest.length > 0) names.push(...this.#culture(rest));
        return { name: this.#joined(names), say: (v, s) => this.#settingValue(setting, v, s), list: true };
      }
      // An option of a field, by its value, or a grid's row, by its key.
      if ((object === 'FieldOptions' && segment === 'items') || (object === 'FormField' && segment === 'rows')) {
        const rows = segment === 'rows';
        const id = path[at + 1];
        if (id === undefined)
          return {
            name: this.#joined([...names, term(rows ? 'jfbHistory_orderOfRows' : 'jfbHistory_orderOfOptions')]),
            say: (v, s) => this.#order(change, v, s, rows),
          };
        names.push(term(rows ? 'jfbHistory_row' : 'jfbHistory_option', this.#itemLabel(change, id, rows)));
        object = rows ? 'GridRow' : 'FieldOption';
        at++;
        const whole = object;
        if (at + 1 >= path.length) return { name: this.#joined(names), say: (v, s) => this.value(v, 'any', s, whole) };
        continue;
      }
      const property = VOCABULARY[object]?.[segment];
      if (!property) {
        names.push(...path.slice(at).filter((one) => one !== ''));
        return { name: this.#joined(names), say: (v, s) => this.any(v, s), list: true };
      }
      // A field's messages and options name themselves deeper in: "Message when ...", "Options from", an option.
      if ((property.holds === 'FieldMessages' || property.holds === 'FieldOptions') && at + 1 < path.length) {
        object = property.holds;
        continue;
      }
      names.push(term(property.term));
      if (property.holds && at + 1 < path.length) {
        object = property.holds;
        continue;
      }
      // A text's language closes its name; deeper in a value the vocabulary doesn't name, the path names itself.
      const rest = path.slice(at + 1);
      if (rest.length > 0)
        names.push(
          ...(property.value === 'texts' || property.value === 'html'
            ? this.#culture(rest)
            : rest.filter((one) => one !== '')),
        );
      return {
        name: this.#joined(names),
        say: (v, s) => this.value(v, property.value, s, property.holds),
        list: property.value === 'lines' || property.value === 'any',
      };
    }
    return { name: this.#joined(names), say: (v, s) => this.any(v, s), list: true };
  }

  /** A text's language, where it is another than the default: "German"; a segment that is no language, by itself. */
  #culture(rest: readonly string[]): string[] {
    const culture = rest[0] ?? '';
    if (!isCulture(culture)) return rest.filter((segment) => segment !== '');
    return culture === '' || culture.toLowerCase() === this.#context.culture.toLowerCase()
      ? []
      : [`(${this.languageOf(culture)})`];
  }

  #joined(names: readonly string[]): string {
    // A language closes the name it follows: "Label (German)".
    const out: string[] = [];
    for (const name of names) {
      if (name.startsWith('(') && out.length > 0) out[out.length - 1] = `${out[out.length - 1]} ${name}`;
      else out.push(name);
    }
    const [first = '', ...more] = out;
    return more.reduce((all, name) => this.#context.term('jfbHistory_what', all, name), first);
  }

  #typeOf(change: DefinitionChangeResponseModel): string {
    if (change.type) return change.type;
    const own = (change.after ?? change.before) as Json | null | undefined;
    return typeof own?.['type'] === 'string' ? own['type'] : '';
  }

  /** An option's label by its value, or a row's by its key, as the side holds it, else as the other does. */
  #itemLabel(change: DefinitionChangeResponseModel, id: string, rows: boolean, side: Side = 'newer'): string {
    for (const own of side === 'older' ? [change.before, change.after] : [change.after, change.before]) {
      if (!isObject(own)) continue;
      const items = rows ? own['rows'] : isObject(own['options']) ? own['options']['items'] : undefined;
      if (!Array.isArray(items)) continue;
      const item = items.find((one) => isObject(one) && one[rows ? 'key' : 'value'] === id);
      if (isObject(item) && isObject(item['label'])) {
        const label = textIn(item['label'] as LocalizedText, this.#context.culture);
        if (label) return label;
      }
    }
    return id;
  }

  /** The order of a field's options or rows, by their labels. */
  #order(change: DefinitionChangeResponseModel, value: unknown, side: Side, rows: boolean): string {
    if (!Array.isArray(value)) return this.any(value, side);
    // A list whose values the server could not key (two options of one value) comes as the items themselves.
    const label = (item: unknown): string =>
      typeof item === 'string'
        ? this.#itemLabel(change, item, rows, side)
        : (isObject(item) &&
            isObject(item['label']) &&
            textIn(item['label'] as LocalizedText, this.#context.culture)) ||
          this.#itemName(item, side) ||
          this.#context.term('jfbHistory_unnamed');
    return cut(value.map(label).join(', '));
  }

  /** The changes of a comparison, in its parts. */
  parts(changes: readonly DefinitionChangeResponseModel[]): ChangePart[] {
    const term = this.#context.term;
    const lines = new Map<ChangePartName, string[]>();
    const rows = new Map<ChangePartName, ChangeRow[]>();
    const add = <T>(map: Map<ChangePartName, T[]>, part: ChangePartName, item: T) =>
      map.set(part, [...(map.get(part) ?? []), item]);
    // Elements added, removed or moved, by kind, each a sentence that names them.
    const grouped = new Map<string, string[]>();
    for (const change of changes) {
      const part = PARTS[change.kind];
      if (!part) continue;
      if (change.kind === 'rule' || change.kind === 'validationRule') {
        this.#rule(
          change,
          (line) => add(lines, part, line),
          (row) => add(rows, part, row),
        );
        continue;
      }
      if (change.change === 'added' || change.change === 'removed' || (change.change === 'changed' && change.moved)) {
        if (change.kind !== 'settings') {
          const how = change.change === 'changed' ? 'moved' : change.change;
          const name = change.kind === 'outcome' ? this.#outcomeOf(change) : this.elementOf(change);
          const key = `${part}|${change.kind}|${how}`;
          grouped.set(key, [...(grouped.get(key) ?? []), name]);
        }
      }
      if (change.change !== 'changed') continue;
      for (const property of change.properties) {
        const path = property.path;
        // A former alias comes with its alias, which says the change once.
        if (change.kind === 'field' && path[0] === 'formerAliases') continue;
        const { name, say, list } = this.property(change, path);
        const what = term('jfbHistory_what', this.#rowElement(change), name);
        const listed = list ? this.#listChange(property.from, property.to) : null;
        if (listed) {
          add(lines, part, this.#listLine(what, listed));
          continue;
        }
        add(rows, part, this.#apart({ what, older: say(property.from, 'older'), newer: say(property.to, 'newer') }));
      }
    }
    for (const [key, names] of grouped) {
      const [part = 'fields', kind = '', how = ''] = key.split('|');
      add(
        lines,
        part as ChangePartName,
        term(
          'jfbHistory_line',
          term(`jfbHistory_kind_${kind}`, names.length),
          term(`jfbHistory_change_${how}`),
          names.join(', '),
        ),
      );
    }
    const order: ChangePartName[] = ['fields', 'settings', 'actions', 'rules'];
    return order
      .map((name) => ({ name, lines: lines.get(name) ?? [], rows: rows.get(name) ?? [] }))
      .filter((part) => part.lines.length > 0 || part.rows.length > 0);
  }

  /** One difference: its own change first, then those of what it holds, which come and go with it. */
  difference(changes: readonly DefinitionChangeResponseModel[]): DifferenceWords {
    const term = this.#context.term;
    const own = changes[0];
    if (!own) return { title: '', rows: [] };
    const yesNo = (side: boolean) => this.#yesNo(side);
    if (own.kind === 'rule' || own.kind === 'validationRule') {
      const validation = own.kind === 'validationRule';
      const title = validation
        ? this.elementOf(own)
        : term('jfbHistory_what', this.nameOf(own.parentKey) || term('jfbHistory_unnamed'), term('jfbHistory_rule'));
      const say = (rule: unknown, side: Side) => (rule ? this.ruleOf(rule, side, validation) : term('jfbHistory_none'));
      return {
        title,
        rows: [
          { what: term('jfbHistory_review_rule'), older: say(own.before, 'older'), newer: say(own.after, 'newer') },
        ],
      };
    }
    const title = this.#title(own);
    if (own.change !== 'changed') {
      const rows: ChangeRow[] = [
        {
          what: term('jfbHistory_review_inForm'),
          older: yesNo(own.change === 'removed'),
          newer: yesNo(own.change === 'added'),
        },
      ];
      // What it holds comes and goes with it, so the side that has it names it.
      const held = changes
        .slice(1)
        .filter((change) => change.kind !== 'rule')
        .map((change) => this.#title(change));
      if (held.length > 0) {
        const list = held.join(', ');
        const none = term('jfbHistory_none');
        rows.push({
          what: term('jfbHistory_review_holds'),
          older: own.change === 'removed' ? list : none,
          newer: own.change === 'added' ? list : none,
        });
      }
      return { title, rows };
    }
    // A change of its place alone is the choice of its place.
    if (own.kind !== 'settings' && own.properties.length === 0)
      return {
        title: term('jfbHistory_review_placeOf', title),
        rows: [this.#placeRow(own)],
      };
    const rows: ChangeRow[] = [];
    for (const property of own.properties) {
      if (own.kind === 'field' && property.path[0] === 'formerAliases') continue;
      const { name, say } = this.property(own, property.path);
      rows.push(this.#apart({ what: name, older: say(property.from, 'older'), newer: say(property.to, 'newer') }));
    }
    if (own.moved || own.previousParentKey) rows.push(this.#placeRow(own));
    return { title, rows };
  }

  #placeRow(change: DefinitionChangeResponseModel): ChangeRow {
    return {
      what: this.#context.term('jfbHistory_review_place'),
      older: this.#placeOf(change.key ?? '', 'older'),
      newer: this.#placeOf(change.key ?? '', 'newer'),
    };
  }

  /** What a difference is about: its kind and name, a settings group, or an outcome by its place in After sending. */
  #title(change: DefinitionChangeResponseModel): string {
    const term = this.#context.term;
    if (change.kind === 'settings') return term('jfbHistory_review_settings', this.elementOf(change));
    if (change.kind === 'outcome') return this.#rowElement(change);
    return term('jfbHistory_review_title', term(`jfbHistory_kind_${change.kind}`, 1), this.elementOf(change));
  }

  /** Where an element is in a version: first in what holds it, or after the element before it. */
  #placeOf(key: string, side: Side): string {
    const term = this.#context.term;
    const definition = this.#definition(side) as Json | null;
    if (!definition) return term('jfbHistory_none');
    const find = (list: readonly Json[], parent: string | null, holds: string): string | null => {
      for (let at = 0; at < list.length; at++) {
        const node = list[at] as Json;
        if (node['key'] === key) {
          const before = at > 0 ? (list[at - 1] as Json)['key'] : null;
          if (typeof before === 'string')
            return term('jfbHistory_review_after', this.nameOf(before, side) || term('jfbHistory_unnamed'));
          return term('jfbHistory_review_first', parent ? this.nameOf(parent, side) : '');
        }
        const inner = holds ? (node[holds] as readonly Json[] | undefined) : undefined;
        const found = inner ? find(inner, String(node['key'] ?? ''), 'fields') : null;
        if (found) return found;
      }
      return null;
    };
    return (
      find((definition['pages'] ?? []) as readonly Json[], null, 'groups') ??
      (['actions', 'outcomes', 'validationRules'] as const)
        .map((holder) => find((definition[holder] ?? []) as readonly Json[], null, ''))
        .find((one) => one !== null) ??
      term('jfbHistory_none')
    );
  }

  /** An element as a row names it: an outcome by its place in After sending, which its own cells say the rest of. */
  #rowElement(change: DefinitionChangeResponseModel): string {
    if (change.kind !== 'outcome') return this.elementOf(change);
    const place = (side: Side) =>
      ((this.#definition(side)?.['outcomes'] ?? []) as readonly DesignOutcome[]).findIndex(
        (one) => one.key === change.key,
      );
    const at = place('newer') >= 0 ? place('newer') : place('older');
    const term = this.#context.term;
    return term('jfbHistory_what', term('jfbSettings_group_after'), term('jfbSettings_outcome_headline', at + 1));
  }

  /** A list's change as the items added and removed; null where either side is no list of items with names. */
  #listChange(from: unknown, to: unknown): { added: string[]; removed: string[] } | null {
    if (!Array.isArray(from) && !Array.isArray(to)) return null;
    const names = (value: unknown, side: Side): string[] | null => {
      if (value === null || value === undefined) return [];
      if (!Array.isArray(value)) return null;
      const named = value.map((item) =>
        typeof item === 'string' || typeof item === 'number' ? String(item) : this.#itemName(item, side),
      );
      return named.every((one) => one !== '') ? named : null;
    };
    const older = names(from, 'older');
    const newer = names(to, 'newer');
    if (!older || !newer) return null;
    const added = newer.filter((one) => !older.includes(one));
    const removed = older.filter((one) => !newer.includes(one));
    // The same items in another order read best side by side.
    return added.length > 0 || removed.length > 0 ? { added, removed } : null;
  }

  #listLine(what: string, { added, removed }: { added: string[]; removed: string[] }): string {
    const term = this.#context.term;
    if (removed.length === 0) return term('jfbHistory_listAdded', what, added.join(', '));
    if (added.length === 0) return term('jfbHistory_listRemoved', what, removed.join(', '));
    return term('jfbHistory_listChanged', what, added.join(', '), removed.join(', '));
  }

  /** A row whose two values read alike though they differ says so, rather than showing the same words twice. */
  #apart(row: ChangeRow): ChangeRow {
    return row.older === row.newer ? { ...row, newer: this.#context.term('jfbHistory_changedAlike', row.newer) } : row;
  }

  #outcomeOf(change: DefinitionChangeResponseModel): string {
    const own = (change.after ?? change.before) as DesignOutcome | null | undefined;
    return own
      ? this.#outcomeName({ ...own, key: change.key ?? '' } as DesignOutcome)
      : this.#context.term('jfbHistory_unnamed');
  }

  #rule(change: DefinitionChangeResponseModel, line: (text: string) => void, row: (row: ChangeRow) => void) {
    const term = this.#context.term;
    const validation = change.kind === 'validationRule';
    const element = validation
      ? this.nameOf(((change.after ?? change.before) as Json | null)?.['field'] as string) || term('jfbHistory_aField')
      : this.nameOf(change.parentKey) || term('jfbHistory_unnamed');
    if (change.change === 'added') {
      line(
        term(
          validation ? 'jfbHistory_checkAdded' : 'jfbHistory_ruleAdded',
          element,
          this.ruleOf(change.after, 'newer', validation),
        ),
      );
      return;
    }
    if (change.change === 'removed') {
      line(
        term(
          validation ? 'jfbHistory_checkRemoved' : 'jfbHistory_ruleRemoved',
          element,
          this.ruleOf(change.before, 'older', validation),
        ),
      );
      return;
    }
    const older = this.ruleOf(change.before, 'older', validation);
    const newer = this.ruleOf(change.after, 'newer', validation);
    row(
      this.#apart({
        what: validation
          ? term('jfbHistory_check', element)
          : term('jfbHistory_what', element, term('jfbHistory_rule')),
        older,
        newer,
      }),
    );
  }
}

/** A comparison of two versions in words, in its parts; an empty list where nothing differs. */
export function comparisonWords(
  changes: readonly DefinitionChangeResponseModel[],
  context: ChangeWordsContext,
): ChangePart[] {
  return new Words(context).parts(changes);
}

/** One difference of Review both versions in words: its own change first, then those of what it holds. */
export function differenceWords(
  changes: readonly DefinitionChangeResponseModel[],
  context: ChangeWordsContext,
): DifferenceWords {
  return new Words(context).difference(changes);
}

/** Each property path of a comparison, named as a row names it: for tests and for whoever words one property. */
export function propertyName(
  change: DefinitionChangeResponseModel,
  path: readonly string[],
  context: ChangeWordsContext,
): string {
  return new Words(context).property(change, path).name;
}
