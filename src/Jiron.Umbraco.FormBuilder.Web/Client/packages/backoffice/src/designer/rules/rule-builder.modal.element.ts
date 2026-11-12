// The rules builder (ROADMAP 9.5a, 9.5c, ADR-0175, ADR-0178): a sidebar that builds or changes one rule. Its first line
// says what the rule does and to what ("Show this field: Which country? when", "Show a message on this field: Email
// when", the message's box under it); each condition is a row in plain language, a question, how it compares and a
// value; "all" and "any" groups nest to the engine's tenth level; Try it says what the element does with answers given;
// Use this rule keeps a rule without using it. Update writes nothing while a row lacks something, the engine's check
// refuses it or a message is empty: the row or the box says why, and the first of them takes the focus.
import type { Kind, Operand, Rule } from '@jfb/logic';
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { allFields, locate, validationRulesOf, type DesignField } from '../definition';
import { resolveOptions } from '../inspector/option-data';
import { ownText, withText } from '../inspector/setting-values';
import { textIn } from '../preview/field-models';
import { readMemberGroups } from './rule-data';
import { ownOptionLabel, valueText } from './rule-names';
import {
  addCondition,
  addGroup,
  builderEffectsOf,
  clashOf,
  conditionsOf,
  draftOf,
  emptyCondition,
  fieldKinds,
  findItem,
  hasMessage,
  isDraftGroup,
  kindIn,
  mayNest,
  newDraft,
  operatorsOf,
  otherFieldOf,
  problemsOf,
  relativeOf,
  ROOT,
  ruleOf,
  scopeContexts,
  shapeOf,
  subjectIdOf,
  subjectsFor,
  updateItem,
  withOperator,
  withSubject,
  type DraftCondition,
  type DraftGroup,
  type DraftRule,
  type Effect,
  type RowProblem,
  type RuleScope,
  type RuleSubject,
  type Visit,
} from './rule-draft';
import type { RuleBuilderModalData, RuleBuilderModalValue } from './rule-builder.modal.token';
import { conditionWords, operatorTerm, subjectWords, valueWords, type RuleWordsContext } from './rule-words';
import { answersOf, triedSubjects, tryContext, tryRule, verdictTerm, type TryBox, type TryVisit } from './rule-try';

interface Option {
  readonly value: string;
  readonly label: string;
}

/** The part of a row a problem is about. */
type Part = 'subject' | 'operator' | 'source' | 'value';

/** Whether a control is invalid, and the problem that says why. */
interface State {
  readonly invalid: string;
  readonly described: string | undefined;
}

const INPUT_TYPES: Partial<Record<Kind, string>> = { date: 'date', time: 'time', dateTime: 'datetime-local' };
const UNITS = ['d', 'w', 'm', 'y'] as const;

/** Today as the browser's clock has it, `YYYY-MM-DD`. */
const localToday = (): string => {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
};

/** A relative date's one step, or null for an offset of several steps, which the builder shows as written. */
function stepOf(offset: string): { amount: number; unit: string; before: boolean } | null {
  if (offset === '') return { amount: 0, unit: 'd', before: false };
  const match = /^([+-])(\d{1,4})([dwmy])$/.exec(offset);
  return match ? { amount: Number(match[2]), unit: match[3] ?? 'd', before: match[1] === '-' } : null;
}

export class RuleBuilderModalElement extends UmbModalBaseElement<RuleBuilderModalData, RuleBuilderModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _draft: { state: true },
    _tried: { state: true },
    _refusal: { state: true },
    _trying: { state: true },
    _boxes: { state: true },
    _visit: { state: true },
    _options: { state: true },
    _groups: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      /* The sidebar's whole height, so its actions sit at its foot. */
      :host {
        display: block;
        height: 100%;
      }

      /* One column that never grows past the sidebar: a long question's list wraps or cuts inside its own box. */
      .builder {
        container-type: inline-size;
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
      }

      select,
      input[type='text'],
      input[type='number'],
      input[type='date'],
      input[type='time'],
      input[type='datetime-local'],
      textarea {
        box-sizing: border-box;
        min-inline-size: 0;
        max-inline-size: 100%;
        min-height: 36px;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      textarea {
        inline-size: 100%;
        min-height: 72px;
        resize: vertical;
      }

      select:focus-visible,
      input:focus-visible,
      textarea:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 0;
        border-color: var(--jfb-bo-color-focus);
      }

      [aria-invalid='true'] {
        border-color: var(--jfb-bo-color-danger-text);
      }

      .first-line {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .first-line #target {
        flex: 1 1 16rem;
      }

      .group {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .nested {
        padding: var(--uui-size-space-4);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .match {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .joiner {
        color: var(--jfb-bo-color-text-muted);
      }

      .condition {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--uui-size-space-1) var(--uui-size-space-2);
        align-items: start;
      }

      .parts {
        display: grid;
        grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr) minmax(0, 1.4fr);
        gap: var(--uui-size-space-3);
        align-items: start;
      }

      .parts > select {
        inline-size: 100%;
      }

      .value {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .value > select,
      .value > input {
        flex: 1 1 8rem;
      }

      .value > input.amount {
        flex: 0 0 5.5rem;
      }

      /* What a value comes from takes its own line, so neither it nor the boxes after it are cut short. */
      .value > select.source {
        flex: 1 1 100%;
      }

      .value.pair > input {
        flex: 1 1 5rem;
      }

      .adds .remove-group {
        margin-inline-start: auto;
      }

      .value fieldset {
        flex: 1 1 100%;
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
        display: grid;
        gap: var(--uui-size-space-1);
      }

      legend {
        padding: 0;
        margin-block-end: var(--uui-size-space-1);
      }

      .checks label,
      label.check {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .problem {
        grid-column: 1 / -1;
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .why {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .help {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .adds {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }

      .try {
        display: grid;
        gap: var(--uui-size-space-3);
        padding-block-start: var(--uui-size-space-4);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .disclosure {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        justify-self: start;
        min-height: 36px;
        padding: 0 var(--uui-size-space-3) 0 var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: inherit;
        font: inherit;
        font-weight: 700;
        cursor: pointer;
      }

      .disclosure:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .disclosure:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 0;
      }

      .try-panel {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .try-boxes {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
        gap: var(--uui-size-space-3) var(--uui-size-space-4);
        /* A box keeps its own height beside a taller one. */
        align-items: start;
      }

      .try-boxes > .box,
      .try-boxes > fieldset {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      .remove-value {
        min-height: 36px;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .remove-value:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 0;
      }

      .verdict {
        margin: 0;
        font-weight: 700;
      }

      .use,
      .message {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      .message {
        margin-block-start: var(--uui-size-space-4);
      }

      .message > label {
        font-weight: 700;
      }

      .message > input {
        inline-size: 100%;
      }

      /* A narrow sidebar puts each part of a row on a line of its own, at its full width; in the first line the target
         takes its own, so its name is not cut beside "when", which then leads into the conditions below it. */
      @container (max-width: 560px) {
        .parts {
          grid-template-columns: minmax(0, 1fr);
        }

        .first-line #target {
          flex-basis: 100%;
        }
      }
    `,
  ];

  declare private _draft: DraftRule;
  declare private _tried: boolean;
  declare private _refusal: string | null;
  declare private _trying: boolean;
  declare private _boxes: Readonly<Record<string, TryBox>>;
  declare private _visit: TryVisit;
  declare private _options: ReadonlyMap<string, readonly Option[] | null>;
  declare private _groups: readonly string[] | null | undefined;

  #next = 0;
  #newId = () => `c${++this.#next}`;
  #requested = new Set<string>();

  override connectedCallback() {
    super.connectedCallback();
    const data = this.data;
    this._tried = false;
    this._refusal = null;
    this._trying = false;
    this._boxes = {};
    this._options = new Map();
    this._groups = undefined;
    const standard = data?.languages.find(() => true)?.culture ?? data?.culture ?? '';
    this._visit = { culture: standard, signedIn: false, groups: [], query: {}, today: localToday() };
    if (!data) return;
    // A rule that is broken already opens with its rows saying why (ADR-0175).
    this._tried = data.rule !== null;
    this._draft = data.rule
      ? draftOf(data.rule, data.origin, this.#newId)
      : newDraft(
          crypto.randomUUID(),
          data.origin,
          data.outcome ? 'use' : data.action ? 'run' : data.effect,
          this.#newId,
        );
    void readMemberGroups(this).then((groups) => (this._groups = groups ?? null));
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.data?.closed?.();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    this.data?.opened?.();
    // The sidebar takes the focus itself as it opens; the rule's first empty question, or what it does, takes it after.
    requestAnimationFrame(() => {
      const empty = conditionsOf(this._draft).find((one) => subjectIdOf(one) === '');
      (empty
        ? this.#control(empty.id, 'select.subject')
        : this.shadowRoot?.querySelector<HTMLElement>('#effect')
      )?.focus();
    });
  }

  // What the builder knows.

  #factsOf = (type: string) => this.data?.types[type];

  #visitFacts(): Visit {
    const settings = this.data?.definition['settings'] as { developer?: { queryValues?: unknown } } | undefined;
    const values = settings?.developer?.queryValues;
    return {
      languages: (this.data?.languages.length ?? 0) > 1,
      queryValues: Array.isArray(values) ? values.filter((one): one is string => typeof one === 'string') : [],
    };
  }

  #scope(): RuleScope {
    const definition = this.data?.definition ?? { pages: [] };
    return {
      fields: fieldKinds(definition, this.#factsOf, !this.data?.outcome && !this.data?.action),
      contexts: scopeContexts(this.#visitFacts()),
    };
  }

  #subjects(keep = ''): RuleSubject[] {
    const data = this.data;
    if (!data) return [];
    return subjectsFor(
      data.definition,
      this.#factsOf,
      this._draft.target,
      this._draft.effect,
      this.#visitFacts(),
      keep,
    );
  }

  #nameOf = (key: string): string => this.data?.names[key] ?? this.localize.term('jfbRules_fieldGone');

  /** The field whose options a subject's values are: the field itself, or the grid a row belongs to. */
  #optionField(key: string): DesignField | undefined {
    const definition = this.data?.definition;
    if (!definition) return undefined;
    const found = locate(definition, key);
    if (found?.kind === 'field') return found.element;
    return allFields(definition).find((field) => (field.rows ?? []).some((row) => row.key === key));
  }

  /** A choice subject's options as the canvas resolves them; undefined while they are read, null where they can't be. */
  #optionsOf(key: string): readonly Option[] | null | undefined {
    const known = this._options.get(key);
    if (known !== undefined || this.#requested.has(key)) return known;
    this.#requested.add(key);
    const field = this.#optionField(key);
    const data = this.data;
    if (!field || !data) return null;
    // A yes or no has its two answers of its own, named as the field names them (16.6's review: a typed "Yes" never held).
    if (field.type === 'yesno') {
      const named = (answer: 'yes' | 'no') =>
        ownOptionLabel(data.definition, field.key, answer, data.culture, (value) =>
          this.localize.term(value === 'yes' ? 'general_yes' : 'general_no'),
        );
      const options = [
        { value: 'yes', label: named('yes') },
        { value: 'no', label: named('no') },
      ];
      this._options = new Map(this._options).set(key, options);
      return options;
    }
    void resolveOptions(this, data.form, field, data.culture).then((answer) => {
      const options =
        answer?.isAvailable === true
          ? answer.options.map((option) => ({ value: option.value, label: option.label || option.value }))
          : null;
      this._options = new Map(this._options).set(key, options);
    });
    return undefined;
  }

  #words(): RuleWordsContext {
    const kinds = this.#scope().fields;
    const culture = this.data?.culture ?? 'en';
    return {
      term: (key, ...args) => this.localize.term(key, ...args),
      nameOf: this.#nameOf,
      kindOf: (field) => kinds[field],
      optionOf: (field, value) => this._options.get(field)?.find((option) => option.value === value)?.label ?? value,
      languageOf: (code) => this.data?.languages.find((language) => language.culture === code)?.name ?? code,
      valueOf: (kind, text) => valueText(culture, kind, text),
      textOf: (texts) => textIn(texts, culture),
    };
  }

  // Changing the rule.

  #setCondition(id: string, update: (condition: DraftCondition) => DraftCondition) {
    this._draft = updateItem(this._draft, id, (item) => (isDraftGroup(item) ? item : update(item)));
    this._refusal = null;
  }

  #setMatch(id: string, match: 'all' | 'any') {
    this._draft = updateItem(this._draft, id, (item) => (isDraftGroup(item) ? { ...item, match } : item));
  }

  #control(id: string, selector: string): HTMLElement | null {
    return this.shadowRoot?.querySelector<HTMLElement>(`[data-id="${id}"] ${selector}`) ?? null;
  }

  async #focusLater(id: string, selector: string) {
    await this.updateComplete;
    this.#control(id, selector)?.focus();
  }

  #addCondition(group: string) {
    const id = this.#newId();
    this._draft = addCondition(this._draft, group, id);
    void this.#focusLater(id, 'select.subject');
  }

  #addGroup(group: string) {
    const before = new Set(conditionsOf(this._draft).map((one) => one.id));
    this._draft = addGroup(this._draft, group, this.#newId);
    const added = conditionsOf(this._draft).find((one) => !before.has(one.id));
    if (added) void this.#focusLater(added.id, 'select.subject');
  }

  /** Removes a condition or group; the focus goes to the condition before it, else to the first one left. */
  #remove(id: string) {
    const order = conditionsOf(this._draft).map((one) => one.id);
    const found = findItem(this._draft, id)?.item;
    // A group's place is that of its first condition.
    const first = found && isDraftGroup(found) ? conditionsOf({ ...this._draft, root: found })[0]?.id : id;
    let next = updateItem(this._draft, id, () => null);
    // A rule always shows a row to start from.
    if (next.root.items.length === 0)
      next = { ...next, root: { ...next.root, items: [emptyCondition(this.#newId())] } };
    this._draft = next;
    this._refusal = null;
    const left = conditionsOf(next).map((one) => one.id);
    const at = order.indexOf(first ?? id);
    const before = order
      .slice(0, Math.max(at, 0))
      .reverse()
      .find((one) => left.includes(one));
    const focus = before ?? left[0];
    if (focus) void this.#focusLater(focus, 'select.subject');
  }

  /**
   * The effects the rule may have on an element: those the element takes, and, on a grid, the message of the validation
   * rule opened where it shows it on one of the grid's rows, which the canvas shows as the grid (ADR-0178).
   */
  #effectsOf(key: string): Effect[] {
    const data = this.data;
    if (!data) return [];
    if (data.outcome) return ['use'];
    if (data.action) return ['run'];
    const effects = builderEffectsOf(data.definition, key, this.#factsOf);
    if (effects.includes('message') || !data.rule) return effects;
    const opened = data.rule.key;
    const field = validationRulesOf(data.definition).find((rule) => rule.key === opened)?.field;
    const found = locate(data.definition, key);
    const rows = found?.kind === 'field' ? (found.element.rows ?? []) : [];
    return rows.some((row) => row.key === field) ? [...effects, 'message'] : effects;
  }

  /** Whether the element no longer takes what the rule does, as after its type changed; the first line then says so. */
  #effectGone(): boolean {
    return !this.#effectsOf(this._draft.target).includes(this._draft.effect);
  }

  /** Moves the rule to another element; a rule still empty then looks at the element it was started from. */
  #retarget(key: string) {
    const data = this.data;
    if (!data) return;
    const effects = this.#effectsOf(key);
    const effect = effects.includes(this._draft.effect) ? this._draft.effect : (effects[0] ?? this._draft.effect);
    let draft: DraftRule = { ...this._draft, target: key, effect };
    const conditions = conditionsOf(draft);
    const only = conditions.length === 1 ? conditions[0] : undefined;
    const origin = this.#scope().fields[data.origin];
    if (only && subjectIdOf(only) === '' && key !== data.origin && origin !== undefined) {
      const subject = subjectsFor(data.definition, this.#factsOf, key, effect, this.#visitFacts()).find(
        (one) => one.field === data.origin,
      );
      if (subject)
        draft = updateItem(draft, only.id, (item) =>
          isDraftGroup(item) ? item : withSubject(item, subject, undefined),
        );
    }
    this._draft = draft;
    this._refusal = null;
  }

  #clash(): string | null {
    const data = this.data;
    if (!data) return null;
    const clash = clashOf(data.definition, this._draft.target, this._draft.effect, this._draft.key);
    if (!clash) return null;
    const name = this.#nameOf(this._draft.target);
    return this.localize.term(
      this._draft.effect === 'require' ? 'jfbRules_clashRequire' : 'jfbRules_clashVisibility',
      name,
    );
  }

  /** Whether the rule shows a message that says nothing yet, which its box then says. */
  #lacksMessage(): boolean {
    return this._draft.effect === 'message' && !hasMessage(this._draft);
  }

  /** Keeps the rule; false while a row, the message's box or the first line says why it can't be kept. */
  #submit(): boolean {
    const data = this.data;
    if (!data) return false;
    if (this.#lacksMessage()) {
      this._tried = true;
      void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('#message')?.focus());
      return false;
    }
    const problems = problemsOf(this._draft, this.#scope());
    if (problems.size > 0) {
      this._tried = true;
      const [first] = problems.keys();
      if (first) void this.#focusLater(first, '[data-problem], [aria-invalid="true"]');
      return false;
    }
    if (this.#effectGone() || this.#clash()) {
      this.shadowRoot?.querySelector<HTMLElement>('#effect')?.focus();
      return false;
    }
    const rule = ruleOf(this._draft);
    if (data.refusal(this._draft.target, rule) === 'cycle') {
      this._refusal = this.localize.term('jfbRules_cycle', this.#nameOf(this._draft.target));
      this.shadowRoot?.querySelector<HTMLElement>('#target')?.focus();
      return false;
    }
    this.updateValue({ target: this._draft.target, rule });
    this._submitModal();
    return true;
  }

  #delete() {
    const data = this.data;
    if (!data?.rule) return;
    this.updateValue({ target: data.origin, rule: null });
    this._submitModal();
  }

  // Drawing.

  #effectWords(effect: Effect) {
    return this.localize.term(`jfbRules_effect_${effect}`);
  }

  #firstLine() {
    const data = this.data;
    if (!data) return nothing;
    // An outcome's rule says only when visitors get it: there is nothing else for it to do, nor anywhere else to be.
    if (data.outcome) {
      const { number } = data.outcome;
      return html`<p class="first-line" id="effect" tabindex="-1">
        ${number === null ? this.localize.term('jfbRules_useWhenNew') : this.localize.term('jfbRules_useWhen', number)}
      </p>`;
    }
    // An action's rule says only when it runs, as an outcome's.
    if (data.action) {
      const { name } = data.action;
      return html`<p class="first-line" id="effect" tabindex="-1">
        ${name === null ? this.localize.term('jfbRules_runWhenNew') : this.localize.term('jfbRules_runWhen', name)}
      </p>`;
    }
    const gone = this.#effectGone();
    // An effect the element no longer takes stays shown, as the rule still has it, until another is chosen.
    const effects = [...this.#effectsOf(this._draft.target), ...(gone ? [this._draft.effect] : [])];
    const clash = gone ? this.localize.term('jfbRules_effectGone', this.#nameOf(this._draft.target)) : this.#clash();
    const message = clash ?? this._refusal;
    const origin = locate(data.definition, data.origin);
    const takes = (key: string) => builderEffectsOf(data.definition, key, this.#factsOf).length > 0;
    return html`<div class="first-line">
        <select
          id="effect"
          aria-label=${this.localize.term('jfbRules_effectLabel')}
          aria-describedby=${message ? 'first-line-why' : nothing}
          aria-invalid=${clash ? 'true' : 'false'}
          @change=${(event: Event) => {
            this._draft = { ...this._draft, effect: (event.target as HTMLSelectElement).value as Effect };
            this._refusal = null;
          }}
        >
          ${effects.map(
            (effect) =>
              html`<option value=${effect} ?selected=${effect === this._draft.effect}>
                ${this.#effectWords(effect)}
              </option>`,
          )}
        </select>
        <select
          id="target"
          aria-label=${this.localize.term(
            this._draft.effect === 'message' ? 'jfbRules_targetLabelMessage' : 'jfbRules_targetLabel',
          )}
          aria-invalid=${this._refusal ? 'true' : 'false'}
          aria-describedby=${message ? 'first-line-why' : nothing}
          @change=${(event: Event) => this.#retarget((event.target as HTMLSelectElement).value)}
        >
          ${
            origin
              ? html`<option value=${data.origin} ?selected=${this._draft.target === data.origin}>
                  ${this.localize.term(`jfbRules_this_${origin.kind}`, this.#nameOf(data.origin))}
                </option>`
              : nothing
          }
          ${data.definition.pages.map(
            (page) =>
              html`<optgroup label=${this.#nameOf(page.key)}>
                ${[
                  page.key !== data.origin && takes(page.key)
                    ? html`<option value=${page.key} ?selected=${this._draft.target === page.key}>
                        ${this.localize.term('jfbRules_targetPage', this.#nameOf(page.key))}
                      </option>`
                    : nothing,
                  ...page.groups.flatMap((group) => [
                    group.key !== data.origin
                      ? html`<option value=${group.key} ?selected=${this._draft.target === group.key}>
                          ${this.localize.term('jfbRules_targetGroup', this.#nameOf(group.key))}
                        </option>`
                      : nothing,
                    ...allFields({ pages: [{ key: '', groups: [group] }] })
                      .filter((field) => field.key !== data.origin && takes(field.key))
                      .map(
                        (field) =>
                          html`<option value=${field.key} ?selected=${this._draft.target === field.key}>
                            ${this.#nameOf(field.key)}
                          </option>`,
                      ),
                  ]),
                ]}
              </optgroup>`,
          )}
        </select>
        <span>${this.localize.term('jfbRules_when')}</span>
      </div>
      ${message ? html`<p class="why" id="first-line-why">${message}</p>` : nothing}
      ${this._draft.effect === 'message' ? this.#messageBox() : nothing}`;
  }

  /** The default language, whose texts visitors read where the one shown has none. */
  #defaultCulture(): string {
    const data = this.data;
    return data?.languages.find((language) => language.isDefault)?.culture ?? data?.culture ?? '';
  }

  #showsDefault(): boolean {
    return (this.data?.culture ?? '').toLowerCase() === this.#defaultCulture().toLowerCase();
  }

  /** A validation rule's message in the language the designer shows; the default's under an empty box in another. */
  #messageBox() {
    const culture = this.data?.culture ?? '';
    const isDefault = this.#showsDefault();
    const texts = this._draft.message;
    const shown = isDefault ? textIn(texts, culture) : ownText(texts, culture);
    const placeholder = isDefault ? '' : textIn(texts, this.#defaultCulture());
    const lacking = this._tried && this.#lacksMessage();
    return html`<div class="message">
      <label for="message">${this.localize.term('jfbRules_messageLabel')}</label>
      <input
        type="text"
        id="message"
        autocomplete="off"
        lang=${culture || nothing}
        placeholder=${placeholder || nothing}
        aria-describedby=${lacking ? 'message-help message-why' : 'message-help'}
        aria-invalid=${lacking ? 'true' : 'false'}
        .value=${shown}
        @input=${(event: Event) => {
          const written = withText(texts, culture, (event.target as HTMLInputElement).value, isDefault);
          this._draft = { ...this._draft, message: written ?? null };
        }}
      />
      <p class="help" id="message-help">${this.localize.term('jfbRules_messageHelp')}</p>
      ${lacking ? html`<p class="why" id="message-why">${this.localize.term('jfbRules_problem_message')}</p>` : nothing}
    </div>`;
  }

  #numbers(): Map<string, number> {
    return new Map(conditionsOf(this._draft).map((one, index) => [one.id, index + 1]));
  }

  #group(
    group: DraftGroup,
    depth: number,
    numbers: Map<string, number>,
    problems: Map<string, RowProblem>,
    remove: TemplateResult | typeof nothing = nothing,
  ): TemplateResult {
    const joiner = this.localize.term(group.match === 'any' ? 'jfbRules_or' : 'jfbRules_and');
    return html`${group.items.length > 1 ? this.#match(group) : nothing}
      ${group.items.map(
        (item, index) =>
          html`${index > 0 ? html`<div class="joiner" aria-hidden="true">${joiner}</div>` : nothing}
          ${isDraftGroup(item) ? this.#nested(item, depth + 1, numbers, problems) : this.#row(item, numbers, problems)}`,
      )}
      <div class="adds">
        <uui-button
          compact
          label=${this.localize.term('jfbRules_addCondition')}
          @click=${() => this.#addCondition(group.id)}
        >
          <uui-icon name="icon-add"></uui-icon>${this.localize.term('jfbRules_addCondition')}
        </uui-button>
        ${
          mayNest(this._draft, group.id)
            ? html`<uui-button
                compact
                label=${this.localize.term('jfbRules_addGroup')}
                @click=${() => this.#addGroup(group.id)}
              >
                <uui-icon name="icon-add"></uui-icon>${this.localize.term('jfbRules_addGroup')}
              </uui-button>`
            : nothing
        }
        ${remove}
      </div>`;
  }

  #match(group: DraftGroup) {
    return html`<div class="match">
      <select
        aria-label=${this.localize.term('jfbRules_matchLabel')}
        @change=${(event: Event) => this.#setMatch(group.id, (event.target as HTMLSelectElement).value as 'all' | 'any')}
      >
        <option value="all" ?selected=${group.match === 'all'}>${this.localize.term('jfbRules_matchAll')}</option>
        <option value="any" ?selected=${group.match === 'any'}>${this.localize.term('jfbRules_matchAny')}</option>
      </select>
    </div>`;
  }

  #groupNumber(id: string): number {
    const groups: string[] = [];
    const walk = (group: DraftGroup) => {
      for (const one of group.items)
        if (isDraftGroup(one)) {
          groups.push(one.id);
          walk(one);
        }
    };
    walk(this._draft.root);
    return groups.indexOf(id) + 1;
  }

  #nested(group: DraftGroup, depth: number, numbers: Map<string, number>, problems: Map<string, RowProblem>) {
    const number = this.#groupNumber(group.id);
    const name = this.localize.term('jfbRules_groupLabel', number);
    // The group's own actions end its last line: what it adds, then taking it away.
    const remove = html`<uui-button
      compact
      class="remove-group"
      label=${this.localize.term('jfbRules_removeGroup', number)}
      @click=${() => this.#remove(group.id)}
    >
      <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>${this.localize.term('jfbRules_removeGroupShort')}
    </uui-button>`;
    return html`<div class="group nested" role="group" aria-label=${name} data-id=${group.id}>
      ${this.#group(group, depth, numbers, problems, remove)}
    </div>`;
  }

  #row(condition: DraftCondition, numbers: Map<string, number>, problems: Map<string, RowProblem>) {
    const number = numbers.get(condition.id) ?? 0;
    const scope = this.#scope();
    const kind = kindIn(condition, scope);
    const id = subjectIdOf(condition);
    const subjects = this.#subjects(id);
    const subject = subjects.find((one) => one.id === id);
    const problem = this._tried ? problems.get(condition.id) : undefined;
    const problemId = `problem-${condition.id}`;
    const words = conditionWords(this.#words(), condition);
    // The control a problem is about says it: the question, the operator or the value.
    const on = (part: Part) =>
      problem !== undefined &&
      (problem === 'subject' || problem === 'unknown'
        ? part === 'subject'
        : problem === 'operator' || problem === 'operatorNotForKind'
          ? part === 'operator'
          : problem === 'unknownOther'
            ? part === 'source'
            : part === 'value');
    const invalid = (part: Part) => ({
      invalid: on(part) ? 'true' : 'false',
      described: on(part) ? problemId : undefined,
    });
    const subjectState = invalid('subject');
    const operatorState = invalid('operator');
    return html`<div class="condition" data-id=${condition.id}>
      <div class="parts">
        <select
          class="subject"
          aria-label=${this.localize.term('jfbRules_subjectLabel', number)}
          aria-invalid=${subjectState.invalid}
          aria-describedby=${subjectState.described ?? nothing}
          @change=${(event: Event) => {
            const chosen = subjects.find((one) => one.id === (event.target as HTMLSelectElement).value);
            this.#setCondition(condition.id, (one) => withSubject(one, chosen, kind));
          }}
        >
          <option value="" ?selected=${!subject}>${this.localize.term('jfbRules_subjectNone')}</option>
          ${this.#subjectOptions(subjects, id)}
        </select>
        ${
          subject && kind
            ? html`<select
                class="operator"
                aria-label=${this.localize.term('jfbRules_operatorLabel', number)}
                aria-invalid=${operatorState.invalid}
                aria-describedby=${operatorState.described ?? nothing}
                @change=${(event: Event) =>
                  this.#setCondition(condition.id, (one) =>
                    withOperator(one, (event.target as HTMLSelectElement).value, kind),
                  )}
              >
                ${operatorsOf(subject, condition.op).map(
                  (op) =>
                    html`<option value=${op} ?selected=${op === condition.op}>
                      ${this.localize.term(operatorTerm(op, kind, condition.context))}
                    </option>`,
                )}
              </select>`
            : nothing
        }
        ${
          subject && kind && condition.op
            ? this.#value(condition, subject, number, invalid('value'), invalid('source'))
            : nothing
        }
      </div>
      <uui-button
        compact
        label=${this.localize.term('jfbRules_removeCondition', words || String(number))}
        @click=${() => this.#remove(condition.id)}
      >
        <!-- A row taken away shows the bin, as an option's or a grid row's does. -->
        <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>
      </uui-button>
      ${problem ? html`<p class="problem" id=${problemId}>${this.localize.term(`jfbRules_problem_${problem}`)}</p>` : nothing}
    </div>`;
  }

  #subjectOptions(subjects: readonly RuleSubject[], current: string) {
    const data = this.data;
    if (!data) return nothing;
    const words = this.#words();
    const option = (subject: RuleSubject) =>
      html`<option value=${subject.id} ?selected=${subject.id === current}>${subjectWords(words, subject)}</option>`;
    return html`${data.definition.pages.map((page, index) => {
        const here = subjects.filter((one) => one.page === index);
        return here.length > 0
          ? html`<optgroup label=${this.#nameOf(page.key)}>${here.map(option)}</optgroup>`
          : nothing;
      })}
      <optgroup label=${this.localize.term('jfbRules_visit')}>
        ${subjects.filter((one) => one.page === null).map(option)}
      </optgroup>`;
  }

  #value(condition: DraftCondition, subject: RuleSubject, number: number, state: State, source: State) {
    const op = condition.op ?? '';
    const shape = shapeOf(subject.kind, op);
    const set = (value: Operand | undefined) => this.#setCondition(condition.id, (one) => ({ ...one, value }));
    const label = this.localize.term('jfbRules_valueLabel', number);
    // A value written by hand or through the API where the operator takes none can be taken away here.
    if ((shape === 'none' || (shape === 'checked' && subject.kind === 'checkbox')) && condition.value !== undefined)
      return html`<div class="value">
        <button
          type="button"
          class="remove-value"
          data-problem
          aria-describedby=${state.described ?? nothing}
          @click=${() => set(undefined)}
        >
          ${this.localize.term('jfbRules_removeValue')}
        </button>
      </div>`;
    switch (shape) {
      case 'one':
        return html`<div class="value">
          ${this.#operand(subject, condition.value, set, number, label, state, source)}
        </div>`;
      case 'pair': {
        const pair = Array.isArray(condition.value) ? (condition.value as readonly Operand[]) : [];
        const end = (at: 0 | 1) => (value: Operand | undefined) => {
          const next: Operand[] = [pair[0] ?? '', pair[1] ?? ''];
          next[at] = value ?? '';
          set(next);
        };
        return html`<div class="value pair">
          ${this.#operand(subject, pair[0], end(0), number, label, state, source)}
          <span>${this.localize.term('jfbRules_and')}</span>
          ${this.#operand(subject, pair[1], end(1), number, this.localize.term('jfbRules_endLabel', number), state, source)}
        </div>`;
      }
      case 'list':
        return html`<div class="value">${this.#list(condition, subject, label, state)}</div>`;
      case 'count':
        return html`<div class="value">
          <input
            type="number"
            class="amount"
            min="0"
            step="1"
            inputmode="numeric"
            aria-label=${label}
            aria-invalid=${state.invalid}
            aria-describedby=${state.described ?? nothing}
            .value=${typeof condition.value === 'string' ? condition.value : ''}
            @input=${(event: Event) => set((event.target as HTMLInputElement).value)}
          />
          <span
            >${this.localize.term(subject.kind === 'files' ? 'jfbRules_countSuffixFiles' : 'jfbRules_countSuffixTicked')}</span
          >
        </div>`;
      case 'pattern':
        return html`<div class="value">
          <input
            type="text"
            spellcheck="false"
            aria-label=${label}
            aria-invalid=${state.invalid}
            aria-describedby=${state.described ?? nothing}
            .value=${typeof condition.value === 'string' ? condition.value : ''}
            @input=${(event: Event) => set((event.target as HTMLInputElement).value)}
          />
        </div>`;
      case 'checked':
        return subject.kind === 'checkbox'
          ? nothing
          : html`<div class="value">${this.#plain(subject, condition.value, set, label, state)}</div>`;
      default:
        return nothing;
    }
  }

  /** One operand: a value, a day counted from today, or another answer, chosen first where there is more than one. */
  #operand(
    subject: RuleSubject,
    operand: Operand | undefined,
    set: (value: Operand | undefined) => void,
    number: number,
    label: string,
    state: State,
    sourceState: State,
  ) {
    const others = this.#subjects()
      .filter((one) => one.field !== null && one.kind === subject.kind && one.field !== subject.field)
      .map((one) => one.field as string);
    const other = otherFieldOf(operand);
    if (other !== null && !others.includes(other)) others.push(other);
    // Only a date counts from today; today's date itself is compared with a date.
    const counted = subject.kind === 'date' && subject.context !== 'today';
    const relative = relativeOf(operand);
    const source = other !== null ? `field:${other}` : relative !== null ? 'today' : 'value';
    const sources = [
      { id: 'value', words: this.localize.term('jfbRules_sourceValue') },
      ...(counted ? [{ id: 'today', words: this.localize.term('jfbRules_sourceToday') }] : []),
      ...others.map((key) => ({
        id: `field:${key}`,
        words: this.localize.term('jfbRules_sourceAnswer', this.#nameOf(key)),
      })),
    ];
    const choose =
      sources.length > 1
        ? html`<select
            class="source"
            aria-label=${this.localize.term('jfbRules_sourceLabel', number)}
            aria-invalid=${sourceState.invalid}
            aria-describedby=${sourceState.described ?? nothing}
            @change=${(event: Event) => {
              const chosen = (event.target as HTMLSelectElement).value;
              set(
                chosen === 'value' ? '' : chosen === 'today' ? { today: '' } : { field: chosen.slice('field:'.length) },
              );
            }}
          >
            ${sources.map((one) => html`<option value=${one.id} ?selected=${one.id === source}>${one.words}</option>`)}
          </select>`
        : nothing;
    if (source === 'today') return html`${choose}${this.#relative(relative ?? '', set, number, state)}`;
    if (source !== 'value') return choose;
    return html`${choose}${this.#plain(subject, operand, set, label, state)}`;
  }

  /** A day counted from today: how many, of what, before or after; an offset of several steps as written. */
  #relative(offset: string, set: (value: Operand | undefined) => void, number: number, state: State) {
    const step = stepOf(offset);
    if (!step)
      return html`<input
        type="text"
        aria-label=${this.localize.term('jfbRules_amountLabel', number)}
        aria-invalid=${state.invalid}
        aria-describedby=${state.described ?? nothing}
        .value=${offset}
        @input=${(event: Event) => set({ today: (event.target as HTMLInputElement).value })}
      />`;
    const write = (amount: number, unit: string, before: boolean) =>
      set({ today: amount === 0 ? '' : `${before ? '-' : '+'}${amount}${unit}` });
    return html`<input
        type="number"
        class="amount"
        min="0"
        max="9999"
        step="1"
        inputmode="numeric"
        aria-label=${this.localize.term('jfbRules_amountLabel', number)}
        aria-invalid=${state.invalid}
        aria-describedby=${state.described ?? nothing}
        .value=${String(step.amount)}
        @input=${(event: Event) => {
          const amount = Math.max(0, Math.min(9999, Math.trunc(Number((event.target as HTMLInputElement).value) || 0)));
          write(amount, step.unit, step.before);
        }}
      />
      <select
        aria-label=${this.localize.term('jfbRules_unitLabel', number)}
        @change=${(event: Event) => write(step.amount, (event.target as HTMLSelectElement).value, step.before)}
      >
        ${UNITS.map((unit) => html`<option value=${unit} ?selected=${unit === step.unit}>${this.localize.term(`jfbRules_unit_${unit}`)}</option>`)}
      </select>
      <select
        aria-label=${this.localize.term('jfbRules_directionLabel', number)}
        @change=${(event: Event) => write(step.amount, step.unit, (event.target as HTMLSelectElement).value === 'before')}
      >
        <option value="after" ?selected=${!step.before}>${this.localize.term('jfbRules_directionAfter')}</option>
        <option value="before" ?selected=${step.before}>${this.localize.term('jfbRules_directionBefore')}</option>
      </select>`;
  }

  /** The choices a value may be: a field's options, the site's languages or its member groups; null where they are typed. */
  #choicesOf(subject: RuleSubject): readonly Option[] | null {
    if (subject.context === 'culture')
      return (this.data?.languages ?? []).map((language) => ({ value: language.culture, label: language.name }));
    if (subject.context === 'memberGroup')
      return this._groups ? this._groups.map((name) => ({ value: name, label: name })) : null;
    if (subject.field !== null && (subject.kind === 'choice' || subject.kind === 'choices'))
      return this.#optionsOf(subject.field) ?? null;
    return null;
  }

  /** A value given directly: an option, a language or a group from a list, else a box for the kind's wire form. */
  #plain(
    subject: RuleSubject,
    operand: Operand | undefined,
    set: (value: Operand | undefined) => void,
    label: string,
    state: State,
  ) {
    const text = typeof operand === 'string' ? operand : '';
    const choices = this.#choicesOf(subject);
    if (choices) {
      // A value the list lacks (an option since removed) stays chosen, as it was written.
      const listed =
        choices.some((one) => one.value === text) || text === '' ? choices : [...choices, { value: text, label: text }];
      return html`<select
        class="plain"
        aria-label=${label}
        aria-invalid=${state.invalid}
        aria-describedby=${state.described ?? nothing}
        @change=${(event: Event) => set((event.target as HTMLSelectElement).value)}
      >
        <option value="" ?selected=${text === ''}>${this.localize.term('jfbRules_valueChoose')}</option>
        ${listed.map((one) => html`<option value=${one.value} ?selected=${one.value === text}>${one.label}</option>`)}
      </select>`;
    }
    const kind = subject.context === 'today' ? 'date' : subject.kind;
    return html`<input
      class="plain"
      type=${INPUT_TYPES[kind] ?? 'text'}
      inputmode=${kind === 'number' ? 'decimal' : nothing}
      step=${kind === 'time' || kind === 'dateTime' ? '60' : nothing}
      aria-label=${label}
      aria-invalid=${state.invalid}
      aria-describedby=${state.described ?? nothing}
      .value=${text}
      @input=${(event: Event) => set((event.target as HTMLInputElement).value)}
    />`;
  }

  /** "Is one of": the choices ticked, or texts one per line. */
  #list(condition: DraftCondition, subject: RuleSubject, label: string, state: State) {
    const given: readonly Operand[] = Array.isArray(condition.value)
      ? (condition.value as readonly Operand[])
      : condition.value === undefined
        ? []
        : [condition.value];
    const items = given.filter((one): one is string => typeof one === 'string');
    // What the boxes can't show stays in the list, said under it.
    const kept = given.filter((one) => typeof one !== 'string');
    const set = (value: Operand | undefined) =>
      this.#setCondition(condition.id, (one) => ({
        ...one,
        value: Array.isArray(value) ? [...(value as readonly Operand[]), ...kept] : value,
      }));
    const also =
      kept.length > 0
        ? html`<p class="help">
            ${this.localize.term(
              'jfbRules_alsoKept',
              kept.map((one) => valueWords(this.#words(), { ...condition, op: 'equals', value: one })).join(', '),
            )}
          </p>`
        : nothing;
    const choices = this.#choicesOf(subject);
    if (choices) {
      const listed = [
        ...choices,
        ...items
          .filter((one) => !choices.some((choice) => choice.value === one))
          .map((one) => ({ value: one, label: one })),
      ];
      return html`<fieldset class="checks" aria-describedby=${state.described ?? nothing}>
          <legend class="visually-hidden">${label}</legend>
          ${listed.map(
            (one) =>
              html`<label>
                <input
                  type="checkbox"
                  aria-invalid=${state.invalid}
                  .checked=${items.includes(one.value)}
                  @change=${(event: Event) => {
                    const ticked = (event.target as HTMLInputElement).checked;
                    const next = ticked ? [...items, one.value] : items.filter((item) => item !== one.value);
                    set(listed.map((choice) => choice.value).filter((value) => next.includes(value)));
                  }}
                />${one.label}
              </label>`,
          )}
        </fieldset>
        ${also}`;
    }
    const helpId = `list-help-${condition.id}`;
    return html`<textarea
        aria-label=${label}
        aria-invalid=${state.invalid}
        aria-describedby=${[helpId, state.described].filter(Boolean).join(' ')}
        .value=${items.join('\n')}
        @input=${(event: Event) =>
          set(
            (event.target as HTMLTextAreaElement).value
              .split('\n')
              .map((line) => line.trim())
              .filter((line) => line !== ''),
          )}
      ></textarea>
      <p class="help" id=${helpId}>${this.localize.term('jfbRules_listHelp')}</p>
      ${also}`;
  }

  // Try it.

  #try() {
    const problems = problemsOf(this._draft, this.#scope());
    return html`<div class="try">
      <button
        type="button"
        class="disclosure"
        aria-expanded=${this._trying ? 'true' : 'false'}
        aria-controls="try-panel"
        @click=${() => (this._trying = !this._trying)}
      >
        <uui-icon name=${this._trying ? 'icon-navigation-down' : 'icon-navigation-right'} aria-hidden="true"></uui-icon>
        ${this.localize.term('jfbRules_try')}
      </button>
      ${
        this._trying
          ? html`<div id="try-panel" class="try-panel">
              <p class="help">${this.localize.term('jfbRules_tryHelp')}</p>
              ${
                problems.size === 0
                  ? html`<div class="try-boxes">${triedSubjects(this._draft).map((id) => this.#tryBox(id))}</div>`
                  : nothing
              }
              <p class="verdict" role="status">${this.#verdict(problems.size === 0)}</p>
            </div>`
          : nothing
      }
    </div>`;
  }

  #verdict(complete: boolean): string {
    if (!complete) return this.localize.term('jfbRules_tryIncomplete');
    const scope = this.#scope();
    const values: Record<string, string[]> = {};
    for (const id of triedSubjects(this._draft)) {
      if (!id.startsWith('field:')) continue;
      const key = id.slice('field:'.length);
      const kind = scope.fields[key];
      if (kind) values[key] = answersOf(kind, this._boxes[id]);
    }
    const outcome = tryRule(ruleOf(this._draft) as unknown as Rule, scope.fields, values, tryContext(this._visit));
    if ('error' in outcome) return this.localize.term('jfbRules_tryIncomplete');
    return this.localize.term(verdictTerm(this._draft.effect, outcome.matches), this.#nameOf(this._draft.target));
  }

  #setBox(id: string, box: TryBox) {
    this._boxes = { ...this._boxes, [id]: box };
  }

  #tryBox(id: string) {
    if (id.startsWith('context:')) return this.#tryVisit(id.slice('context:'.length), this.#words());
    const key = id.slice('field:'.length);
    const kind = this.#scope().fields[key];
    const name = this.#nameOf(key);
    const box = this._boxes[id];
    const boxId = `try-${key}`;
    if (kind === 'checkbox')
      return html`<label class="check">
        <input
          type="checkbox"
          .checked=${box === true}
          @change=${(event: Event) => this.#setBox(id, (event.target as HTMLInputElement).checked)}
        />
        ${name}
      </label>`;
    if (kind === 'files')
      return html`<div class="box">
        <label for=${boxId}>${name}</label>
        <input
          id=${boxId}
          type="number"
          min="0"
          max="99"
          step="1"
          inputmode="numeric"
          aria-describedby=${`try-files-${key}`}
          .value=${String(typeof box === 'number' ? box : 0)}
          @input=${(event: Event) => this.#setBox(id, Number((event.target as HTMLInputElement).value) || 0)}
        />
        <span class="help" id=${`try-files-${key}`}>${this.localize.term('jfbRules_tryFiles')}</span>
      </div>`;
    const options = kind === 'choice' || kind === 'choices' ? (this.#optionsOf(key) ?? null) : null;
    if (kind === 'choices' && options) {
      const ticked = Array.isArray(box) ? (box as readonly string[]) : [];
      return html`<fieldset class="checks">
        <legend>${name}</legend>
        ${options.map(
          (option) =>
            html`<label>
              <input
                type="checkbox"
                .checked=${ticked.includes(option.value)}
                @change=${(event: Event) =>
                  this.#setBox(
                    id,
                    (event.target as HTMLInputElement).checked
                      ? [...ticked, option.value]
                      : ticked.filter((value) => value !== option.value),
                  )}
              />${option.label}
            </label>`,
        )}
      </fieldset>`;
    }
    if (options)
      return html`<div class="box">
        <label for=${boxId}>${name}</label>
        <select id=${boxId} @change=${(event: Event) => this.#setBox(id, (event.target as HTMLSelectElement).value)}>
          <option value="" ?selected=${!box}>${this.localize.term('jfbRules_tryNoAnswer')}</option>
          ${options.map((option) => html`<option value=${option.value} ?selected=${option.value === box}>${option.label}</option>`)}
        </select>
      </div>`;
    return html`<div class="box">
      <label for=${boxId}>${name}</label>
      <input
        id=${boxId}
        type=${(kind && INPUT_TYPES[kind]) ?? 'text'}
        inputmode=${kind === 'number' ? 'decimal' : nothing}
        step=${kind === 'time' || kind === 'dateTime' ? '60' : nothing}
        .value=${typeof box === 'string' ? box : ''}
        @input=${(event: Event) => this.#setBox(id, (event.target as HTMLInputElement).value)}
      />
    </div>`;
  }

  #tryVisit(context: string, words: RuleWordsContext) {
    const name = subjectWords(words, { context });
    const visit = this._visit;
    const set = (next: Partial<TryVisit>) => (this._visit = { ...visit, ...next });
    const boxId = `try-${context}`;
    if (context === 'memberSignedIn')
      return html`<label class="check">
        <input
          type="checkbox"
          .checked=${visit.signedIn}
          @change=${(event: Event) => set({ signedIn: (event.target as HTMLInputElement).checked })}
        />
        ${name} ${this.localize.term('jfbRules_op_signedIn')}
      </label>`;
    if (context === 'culture')
      return html`<div class="box">
        <label for=${boxId}>${name}</label>
        <select id=${boxId} @change=${(event: Event) => set({ culture: (event.target as HTMLSelectElement).value })}>
          ${(this.data?.languages ?? []).map(
            (language) =>
              html`<option value=${language.culture} ?selected=${language.culture === visit.culture}>
                ${language.name}
              </option>`,
          )}
        </select>
      </div>`;
    if (context === 'memberGroup') {
      if (this._groups)
        return html`<fieldset class="checks">
          <legend>${name}</legend>
          ${this._groups.map(
            (group) =>
              html`<label>
                <input
                  type="checkbox"
                  .checked=${visit.groups.includes(group)}
                  @change=${(event: Event) =>
                    set({
                      groups: (event.target as HTMLInputElement).checked
                        ? [...visit.groups, group]
                        : visit.groups.filter((one) => one !== group),
                    })}
                />${group}
              </label>`,
          )}
        </fieldset>`;
      return html`<div class="box">
        <label for=${boxId}>${this.localize.term('jfbRules_groupsTyped')}</label>
        <input
          id=${boxId}
          type="text"
          .value=${visit.groups.join(', ')}
          @input=${(event: Event) =>
            set({
              groups: (event.target as HTMLInputElement).value
                .split(',')
                .map((one) => one.trim())
                .filter((one) => one !== ''),
            })}
        />
      </div>`;
    }
    if (context === 'today')
      return html`<div class="box">
        <label for=${boxId}>${name}</label>
        <input
          id=${boxId}
          type="date"
          .value=${visit.today}
          @input=${(event: Event) => {
            const day = (event.target as HTMLInputElement).value;
            if (day) set({ today: day });
          }}
        />
      </div>`;
    const query = context.slice('query.'.length);
    return html`<div class="box">
      <label for=${boxId}>${name}</label>
      <input
        id=${boxId}
        type="text"
        .value=${visit.query[query] ?? ''}
        @input=${(event: Event) => set({ query: { ...visit.query, [query]: (event.target as HTMLInputElement).value } })}
      />
    </div>`;
  }

  #offHelp(): string {
    const data = this.data;
    if (this._draft.effect === 'message') return this.localize.term('jfbRules_offHelpMessage');
    if (this._draft.effect === 'require') return this.localize.term('jfbRules_offHelpRequired');
    if (this._draft.effect === 'use') return this.localize.term('jfbRules_offHelpUse');
    if (this._draft.effect === 'run') return this.localize.term('jfbRules_offHelpRun');
    const kind = data ? (locate(data.definition, this._draft.target)?.kind ?? 'field') : 'field';
    return this.localize.term(`jfbRules_offHelpShows_${kind}`);
  }

  override render() {
    const data = this.data;
    if (!data || !this._draft) return nothing;
    const numbers = this.#numbers();
    const problems = problemsOf(this._draft, this.#scope());
    return html`<umb-body-layout
      headline=${this.localize.term(data.rule ? 'jfbRules_headline' : 'jfbRules_headlineNew')}
    >
      <div class="builder">
        <div>${this.#firstLine()}</div>
        <div class="group" data-id=${ROOT}>${this.#group(this._draft.root, 1, numbers, problems)}</div>
        ${this.#try()}
        <div class="use">
          <uui-toggle
            label=${this.localize.term('jfbRules_useRule')}
            .checked=${this._draft.enabled}
            @change=${(event: Event) => (this._draft = { ...this._draft, enabled: (event.target as HTMLInputElement).checked })}
          ></uui-toggle>
          <p class="help">${this.#offHelp()}</p>
        </div>
      </div>
      ${
        data.rule
          ? html`<uui-button
              slot="footer-info"
              look="secondary"
              label=${this.localize.term('jfbRules_deleteRule')}
              @click=${() => this.#delete()}
            ></uui-button>`
          : nothing
      }
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term(data.rule ? 'jfbRules_update' : 'jfbRules_add')}
          @click=${() => this.#submit()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-rule-builder-modal'))
  customElements.define('jfb-rule-builder-modal', RuleBuilderModalElement);

export { RuleBuilderModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-rule-builder-modal': RuleBuilderModalElement;
  }
}
