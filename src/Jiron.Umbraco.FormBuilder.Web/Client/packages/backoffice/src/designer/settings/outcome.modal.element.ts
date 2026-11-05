// An outcome's sidebar (ROADMAP 10.1b, IA 5.2, ADR-0185): what visitors get after sending (a message, a redirect, or a
// message and then a redirect), the message in the language the Settings view shows, whether it shows a copy of the
// answers, the page or the address the redirect leads to and the seconds before it, and the outcome's rule, which the
// rules builder edits over it, and its place in the list, which a pointer can choose without dragging. The form check is
// asked about the draft as Update would make it, once the changes pause, and its problems in the outcome show under
// their setting: those it had at once, others once their setting is left or Update is pressed; Update keeps the outcome
// once the check finds no error in it that the sidebar can fix, or gives no answer. Beside the settings, where the
// sidebar has room, below them where it has not, the outcome's live preview shows it as visitors get it with the sample
// entry, asked for as the check is (10.1c, ADR-0187).
import {
  css,
  html,
  live,
  nothing,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement, umbOpenModal } from '@umbraco-cms/backoffice/modal';
import '@umbraco-cms/backoffice/document';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { issueWords, type CheckWordsContext } from '../check/check-words';
import { parsePath, type CheckSeverity } from '../check/form-check';
import { FormChecker, UNCHECKED, type CheckState } from '../check/form-checker';
import type { DesignDefinition } from '../definition';
import { boxValue } from '../inspector/box-value';
import { controlStyles } from '../inspector/control.styles';
import { focusableIn } from '../inspector/item-focus';
import '../inspector/merge-tag-picker.element';
import { withTag } from '../inspector/merge-tags';
import '../inspector/rich-text.element';
import { problemText } from '../inspector/setting-editor';
import { ownText, same, withText, type Problem } from '../inspector/setting-values';
import { textIn } from '../preview/field-models';
import { themeSheets } from '../preview/look';
import { RULE_BUILDER_MODAL } from '../rules/rule-builder.modal.token';
import { plainText } from '../rules/rule-list';
import { elementNames, inspectorRuleWords, typeFactsOf } from '../rules/rule-names';
import { ruleWords } from '../rules/rule-words';
import { choiceStyles } from './choice.styles';
import {
  MOST_DELAY,
  MOST_URL,
  OUTCOME_KINDS,
  drawnSetting,
  editOf,
  outcomeOf,
  outcomesOf,
  placeOfNew,
  reachOf,
  settingAtPath,
  withOutcome,
  withOutcomes,
  type OutcomeEdit,
  type OutcomeSetting,
} from './outcome-model';
import { NO_PREVIEW, OutcomePreviewer, type PreviewState } from './outcome-preview';
import './outcome-preview.element';
import type { OutcomeModalData, OutcomeModalValue } from './outcome.modal.token';
import { described, whys, type Shown } from './problem-lines';
import { parseWhole } from './settings-model';

/** The id of the box each setting is edited in, in the order the sidebar shows them. */
const BOXES: Readonly<Record<OutcomeSetting, string>> = {
  kind: 'kind',
  message: 'message',
  showAnswers: 'show-answers',
  leadsTo: 'leads-to',
  page: 'page',
  address: 'address',
  delay: 'delay',
  rule: 'rule',
};

const IN_ORDER = Object.keys(BOXES) as OutcomeSetting[];

/** A problem the check found in the outcome; one in another language's message is said, and Update is not kept back. */
interface Found extends Shown {
  readonly blocks: boolean;
}

export class OutcomeModalElement extends UmbModalBaseElement<OutcomeModalData, OutcomeModalValue> {
  static override properties = {
    _edit: { state: true },
    _check: { state: true },
    _typed: { state: true },
    _shown: { state: true },
    _tried: { state: true },
    _checking: { state: true },
    _at: { state: true },
    _preview: { state: true },
    _sheets: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    controlStyles,
    choiceStyles,
    css`
      .outcome {
        container-type: inline-size;
        display: grid;
        gap: var(--uui-size-space-6);
      }

      /* The settings and the preview: one column, the preview after the settings; side by side where the sidebar has
         room, the preview kept in view while the settings scroll. */
      .room {
        container-type: inline-size;
      }

      .layout {
        display: grid;
        gap: var(--uui-size-space-6);
      }

      @container (min-width: 960px) {
        .layout {
          grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
          align-items: start;
          gap: var(--uui-size-layout-2);
        }

        jfb-outcome-preview {
          position: sticky;
          top: 0;
        }
      }

      .rule-words,
      .never {
        margin: 0;
      }

      .never {
        color: var(--jfb-bo-color-warning-text);
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
        .rule-button:hover {
          color: var(--uui-color-interactive-emphasis);
          border-color: var(--uui-color-interactive-emphasis);
        }
      }

      .rule-button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      /* The footer keeps Cancel and Update in one row; where a phone leaves no room for Delete outcome beside them, it
         goes to the end of the sidebar's content (as the gallery's Preview, ADR-0182). */
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

      @media (max-width: 600px) {
        .delete-footer {
          display: none;
        }

        .delete-body {
          display: block;
          padding-block-start: var(--uui-size-space-5);
          border-block-start: 1px solid var(--uui-color-divider-standalone);
        }
      }
    `,
  ];

  declare private _edit: OutcomeEdit;
  declare private _check: CheckState;
  /** Why a box's last value couldn't be kept, by its setting, until it can. */
  declare private _typed: ReadonlyMap<OutcomeSetting, string>;
  /** The settings whose problems the check's answers show: those the outcome had as it opened, and each one left. */
  declare private _shown: ReadonlySet<OutcomeSetting>;
  /** Whether Update was pressed, after which every problem shows. */
  declare private _tried: boolean;
  declare private _checking: boolean;
  /** The outcome's place in the list from 0, as the Place box chose it; null while it is a new one's place. */
  declare private _at: number | null;
  declare private _preview: PreviewState;
  /** The site's look, which the preview draws the outcome with. */
  declare private _sheets: readonly CSSStyleSheet[];

  #key = '';
  #previewer: OutcomePreviewer | undefined;
  /** Whether Update is waiting for the check, so a second press does not keep the outcome twice. */
  #submitting = false;
  /** Whether the rules builder is open over the sidebar, so a second does not open over it. */
  #building = false;
  #checker: FormChecker | undefined;
  /** Whether the check has answered yet: its first answer's problems are the outcome's own, which show at once. */
  #answered = false;

  constructor() {
    super();
    this._edit = editOf(null);
    this._check = UNCHECKED;
    this._typed = new Map();
    this._shown = new Set();
    this._tried = false;
    this._checking = false;
    this._at = null;
    this._preview = NO_PREVIEW;
    this._sheets = [];
  }

  override connectedCallback() {
    super.connectedCallback();
    const data = this.data;
    if (!data) return;
    this.#key = data.outcome?.key ?? crypto.randomUUID();
    this._edit = editOf(data.outcome);
    this._at = data.outcome ? data.number - 1 : null;
    this.#answered = false;
    this.#checker = new FormChecker(data.check, (state) => this.#heard(state));
    this.#checker.changed(this.#draft());
    this.#previewer = new OutcomePreviewer(
      (definition) => data.preview(definition, this.#key, data.culture),
      (state) => (this._preview = state),
    );
    this.#previewer.changed(this.#draft());
    void themeSheets(data.stylesheets).then((sheets) => (this._sheets = sheets));
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#checker?.reset();
    this.#previewer?.stop();
    this.data?.closed?.();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // The sidebar takes the focus itself as it opens; the setting a Fix it leads to, or what visitors get, takes it after.
    requestAnimationFrame(() => this.#focus(this.data?.focus ?? 'kind'));
  }

  // ---- Words --------------------------------------------------------------------------------------------------------

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbSettings_${key}`, ...args);

  #weight = (severity: CheckSeverity) => this.#term(`severity_${severity}`);

  #problemWords(problem: Problem): string {
    return problemText((key, ...args) => this.localize.term(`jfbInspector_${key}`, ...args), problem);
  }

  #defaultCulture(): string {
    const data = this.data;
    return data?.languages.find((language) => language.isDefault)?.culture ?? data?.culture ?? '';
  }

  #isDefault(): boolean {
    return (this.data?.culture ?? '').toLowerCase() === this.#defaultCulture().toLowerCase();
  }

  // ---- The outcome --------------------------------------------------------------------------------------------------

  #outcome() {
    return outcomeOf(this._edit, this.data?.outcome ?? null, this.#key);
  }

  /** The outcome's place in the list: as chosen, or a new one's where it goes by its rule. */
  #at(): number {
    const others = outcomesOf(this.data?.definition ?? { pages: [] }).filter((one) => one.key !== this.#key);
    return Math.min(this._at ?? placeOfNew(others, this._edit.rule), others.length);
  }

  /** The draft as Update would make it: the outcome at its place. */
  #draft(): DesignDefinition {
    const definition = this.data?.definition ?? { pages: [] };
    const others = outcomesOf(definition).filter((one) => one.key !== this.#key);
    return withOutcomes(definition, withOutcome(others, this.#outcome(), this.#at()));
  }

  /** A change of a setting: `done` once its box is left, after which the check's problems with it show. */
  #change(next: OutcomeEdit, setting: OutcomeSetting, done: boolean) {
    this._edit = next;
    if (done && !this._shown.has(setting)) this._shown = new Set([...this._shown, setting]);
    this.#checker?.changed(this.#draft());
    this.#previewer?.changed(this.#draft());
  }

  #typedProblem(setting: OutcomeSetting, text: string | null) {
    if ((this._typed.get(setting) ?? null) === text) return;
    const next = new Map(this._typed);
    if (text === null) next.delete(setting);
    else next.set(setting, text);
    this._typed = next;
  }

  // ---- The check ----------------------------------------------------------------------------------------------------

  #heard(state: CheckState) {
    this._check = state;
    if (this.#answered || state.status !== 'checked') return;
    this.#answered = true;
    this._shown = new Set([...this._shown, ...this.#found().keys()]);
  }

  /** A typed problem whose box is drawn now: one hidden by another kind or choice neither shows nor keeps Update back. */
  #typedAt(setting: OutcomeSetting): string | undefined {
    return drawnSetting(setting, this._edit) === setting ? this._typed.get(setting) : undefined;
  }

  /**
   * The check's problems in this outcome, by the setting they show at as the sidebar draws it now. An answer about the
   * outcome as it was before the last change shows only the problems of boxes still drawn: another kind's would land
   * at the wrong box. A problem in another language's message says the language, and Update is not kept back by it.
   */
  #found(): Map<OutcomeSetting, Found[]> {
    const found = new Map<OutcomeSetting, Found[]>();
    const data = this.data;
    const checked = this._check.definition;
    if (!data || !checked) return found;
    const outcomes = outcomesOf(checked);
    const index = outcomes.findIndex((outcome) => outcome.key === this.#key);
    if (index < 0) return found;
    const current = same(outcomes[index], this.#outcome());
    const term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);
    const words: CheckWordsContext = {
      term,
      say: (text) => (text ? this.localize.string(text) : ''),
      types: data.types,
      names: elementNames(checked, data.culture, term),
    };
    for (const issue of this._check.issues) {
      const steps = parsePath(issue.path);
      if (!steps || steps[0] !== 'outcomes' || steps[1] !== index) continue;
      const own = settingAtPath(steps.slice(2));
      const setting = drawnSetting(own, this._edit);
      if (!current && setting !== own) continue;
      const said = issueWords(issue, checked, words).said;
      const language = own === 'message' ? this.#elsewhere(steps[3]) : null;
      found.set(setting, [
        ...(found.get(setting) ?? []),
        language === null
          ? { text: said, severity: issue.severity, blocks: true }
          : { text: this.#term('outcome_inLanguage', language, said), severity: issue.severity, blocks: false },
      ]);
    }
    return found;
  }

  /** The name of the language a message's text is in, where the sidebar shows another; null for the one shown. */
  #elsewhere(key: string | number | undefined): string | null {
    const data = this.data;
    if (!data || typeof key !== 'string') return null;
    const shown = data.culture.toLowerCase();
    const culture = key === '' ? this.#defaultCulture().toLowerCase() : key.toLowerCase();
    if (culture === shown || shown.startsWith(`${culture}-`)) return null;
    return data.languages.find((one) => one.culture.toLowerCase() === culture)?.name ?? key;
  }

  /** The problems shown under a setting's box: one typed that can't be kept, then the check's that show by now. */
  #problemsAt(setting: OutcomeSetting, found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): Shown[] {
    const typed = this.#typedAt(setting);
    const checked = this._tried || this._shown.has(setting) ? (found.get(setting) ?? []) : [];
    return [...(typed ? [{ text: typed, severity: 'error' as const }] : []), ...checked];
  }

  // ---- Keeping ------------------------------------------------------------------------------------------------------

  #focus(setting: OutcomeSetting) {
    const box = this.shadowRoot?.getElementById(BOXES[drawnSetting(setting, this._edit)]);
    if (!box) return;
    // A rich text editor takes the focus itself, into its text.
    const focus =
      box.localName === 'fieldset'
        ? (box.querySelector<HTMLElement>('input:checked') ?? focusableIn(box))
        : box.localName === 'jfb-rich-text'
          ? box
          : focusableIn(box);
    focus?.focus();
  }

  async #submit() {
    if (this.#submitting) return;
    // The first box that says why, as the sidebar shows them, takes the focus.
    const typed = IN_ORDER.find((setting) => this.#typedAt(setting) !== undefined);
    if (typed) {
      this.#focus(typed);
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
      const first = IN_ORDER.find((setting) =>
        found.get(setting)?.some((one) => one.severity === 'error' && one.blocks),
      );
      if (first) {
        await this.updateComplete;
        this.#focus(first);
        return;
      }
    }
    this.value = { outcome: this.#outcome(), at: this.#at() };
    this._submitModal();
  }

  #delete() {
    this.value = { deleted: true };
    this._submitModal();
  }

  async #buildRule() {
    const data = this.data;
    if (!data || this.#building) return;
    this.#building = true;
    try {
      await this.#openBuilder(data);
    } finally {
      this.#building = false;
    }
  }

  async #openBuilder(data: OutcomeModalData) {
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
        origin: this.#key,
        outcome: { number: data.outcome ? this.#at() + 1 : null },
        rule: this._edit.rule,
        effect: 'use',
        culture: data.culture,
        languages: data.languages,
        types: typeFactsOf(data.types),
        names: elementNames(definition, data.culture, term),
        refusal: () => null,
      },
    }).catch(() => undefined);
    if (shown) await Promise.race([closed, new Promise((done) => setTimeout(done, 2000))]);
    if (value) this.#change({ ...this._edit, rule: value.rule }, 'rule', true);
    await this.updateComplete;
    this.#focus('rule');
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #kind(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const problems = this.#problemsAt('kind', found);
    return html`<fieldset class="item" id="kind" aria-describedby=${described('kind', false, problems) ?? nothing}>
      <legend class="label">${this.#term('outcome_kind')}</legend>
      <div class="choices">
        ${OUTCOME_KINDS.map(
          (kind) =>
            html`<label class="choice">
              <input
                type="radio"
                name="kind"
                value=${kind}
                .checked=${live(this._edit.kind === kind)}
                @change=${() => this.#change({ ...this._edit, kind }, 'kind', true)}
              />
              <span class="choice-name">${this.#term(`outcome_kind_${kind}`)}</span>
            </label>`,
        )}
      </div>
      ${whys('kind', problems, this.#weight)}
    </fieldset>`;
  }

  #message(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const data = this.data;
    if (!data) return html``;
    const problems = this.#problemsAt('message', found);
    const label = this.#term('outcome_message');
    const help = this.#term('outcome_messageHelp');
    const texts = this._edit.message;
    const isDefault = this.#isDefault();
    const standard = plainText(textIn(texts, this.#defaultCulture()));
    const change = (text: string, done: boolean) => {
      if (text.length > data.htmlLength) {
        if (done) this.#typedProblem('message', this.#problemWords({ kind: 'tooLong', limit: data.htmlLength }));
        return;
      }
      this.#typedProblem('message', null);
      const next = withText(this._edit.message, data.culture, text, isDefault) ?? null;
      this.#change({ ...this._edit, message: next }, 'message', done);
    };
    // The editor lies in a shadow root of its own, which ids can't reach: its description carries the problems too.
    return html`<div class="item">
      <p class="label" id="message-label">${label}</p>
      <p class="help">${help}</p>
      <jfb-rich-text
        id="message"
        .label=${label}
        .description=${[help, ...problems.map((problem) => problem.text)].join(' ')}
        .language=${data.culture}
        .placeholder=${isDefault || standard === '' ? data.defaultMessage : standard}
        .value=${isDefault ? textIn(texts, data.culture) : ownText(texts, data.culture)}
        .change=${change}
      ></jfb-rich-text>
      ${whys('message', problems, this.#weight)}
      <jfb-merge-tag-picker
        .place=${'outcome'}
        .form=${data.form}
        .owner=${''}
        .culture=${data.culture}
        .definition=${() => this.#draft()}
        .insert=${(tag: string) =>
          (this.shadowRoot?.getElementById('message') as { insertText?: (text: string) => void } | null)?.insertText?.(
            tag,
          )}
      ></jfb-merge-tag-picker>
    </div>`;
  }

  #showAnswers(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const problems = this.#problemsAt('showAnswers', found);
    return html`<div class="item">
      <div class="item-switch">
        <input
          type="checkbox"
          role="switch"
          id="show-answers"
          .checked=${live(this._edit.showAnswers)}
          aria-describedby=${described('show-answers', true, problems) ?? nothing}
          @change=${(event: Event) =>
            this.#change(
              { ...this._edit, showAnswers: (event.target as HTMLInputElement).checked },
              'showAnswers',
              true,
            )}
        />
        <label class="label" for="show-answers">${this.#term('outcome_showAnswers')}</label>
      </div>
      <p class="help" id="show-answers-help">${this.#term('outcome_showAnswersHelp')}</p>
      ${whys('show-answers', problems, this.#weight)}
    </div>`;
  }

  #leadsTo(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const problems = this.#problemsAt('leadsTo', found);
    const leadsTo = this._edit.leadsTo;
    return html`<div class="with-detail">
      <fieldset class="item" id="leads-to" aria-describedby=${described('leads-to', false, problems) ?? nothing}>
        <legend class="label">${this.#term('outcome_leadsTo')}</legend>
        <div class="choices">
          ${(['page', 'address'] as const).map(
            (choice) =>
              html`<label class="choice">
                <input
                  type="radio"
                  name="leads-to"
                  value=${choice}
                  .checked=${live(leadsTo === choice)}
                  @change=${() => this.#change({ ...this._edit, leadsTo: choice }, 'leadsTo', true)}
                />
                <span class="choice-name">${this.#term(`outcome_leadsTo_${choice}`)}</span>
              </label>`,
          )}
        </div>
        ${whys('leads-to', problems, this.#weight)}
      </fieldset>
      <div class="detail">${leadsTo === 'page' ? this.#page(found) : this.#address(found)}</div>
    </div>`;
  }

  // Umbraco's own document picker, which no label names: a group named by the label holds it (ADR-0173).
  #page(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const problems = this.#problemsAt('page', found);
    const content = this._edit.content;
    return html`<div class="item">
      <p class="label" id="page-label">${this.#term('outcome_page')}</p>
      <div role="group" aria-labelledby="page-label" aria-describedby=${described('page', false, problems) ?? nothing}>
        <umb-input-document
          id="page"
          .selection=${content ? [content] : []}
          .max=${1}
          @change=${(event: Event) => {
            const chosen = [...((event.target as { selection?: readonly string[] }).selection ?? [])];
            this.#change({ ...this._edit, content: chosen[0] ?? null }, 'page', true);
          }}
        ></umb-input-document>
      </div>
      ${whys('page', problems, this.#weight)}
    </div>`;
  }

  #address(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const data = this.data;
    const problems = this.#problemsAt('address', found);
    const typed = (raw: string, done: boolean) => {
      const text = raw.trim();
      if (text.length > MOST_URL) {
        if (done) this.#typedProblem('address', this.#problemWords({ kind: 'tooLong', limit: MOST_URL }));
        return;
      }
      this.#typedProblem('address', null);
      this.#change({ ...this._edit, url: text }, 'address', done);
    };
    return html`<div class="item">
      <label class="label" for="address">${this.#term('outcome_address')}</label>
      <p class="help" id="address-help">${this.#term('outcome_addressHelp')}</p>
      <input
        type="text"
        id="address"
        class="code"
        inputmode="url"
        spellcheck="false"
        autocomplete="off"
        aria-describedby=${described('address', true, problems) ?? nothing}
        aria-invalid=${problems.some((problem) => problem.severity === 'error') ? 'true' : 'false'}
        .value=${boxValue(this._edit.url, this._typed.has('address'))}
        @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
        @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
      />
      ${whys('address', problems, this.#weight)}
      ${
        data
          ? html`<jfb-merge-tag-picker
              .place=${'redirect'}
              .form=${data.form}
              .owner=${''}
              .culture=${data.culture}
              .definition=${() => this.#draft()}
              .insert=${(tag: string) => this.#insertTag(tag)}
            ></jfb-merge-tag-picker>`
          : nothing
      }
    </div>`;
  }

  // A tag put into the address where its caret was, the box given the focus with the caret after the tag, and the
  // change sent as leaving the box sends one.
  #insertTag(tag: string) {
    const box = this.shadowRoot?.getElementById('address');
    if (!(box instanceof HTMLInputElement)) return;
    const end = box.value.length;
    const { text, caret } = withTag(box.value, box.selectionStart ?? end, box.selectionEnd ?? end, tag);
    box.value = text;
    box.focus();
    box.setSelectionRange(caret, caret);
    box.dispatchEvent(new Event('change', { bubbles: true }));
  }

  #delay(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const problems = this.#problemsAt('delay', found);
    const typed = (raw: string, done: boolean) => {
      const seconds = parseWhole(raw, 0, MOST_DELAY);
      if (seconds === null || seconds === undefined) {
        if (done)
          this.#typedProblem(
            'delay',
            this.#term('wholeNumber', this.localize.number(0), this.localize.number(MOST_DELAY)),
          );
        return;
      }
      this.#typedProblem('delay', null);
      this.#change({ ...this._edit, delay: seconds }, 'delay', done);
    };
    return html`<div class="item">
      <label class="label" for="delay">${this.#term('outcome_delay')}</label>
      <p class="help" id="delay-help">${this.#term('outcome_delayHelp', this.localize.number(MOST_DELAY))}</p>
      <input
        type="text"
        id="delay"
        class="number"
        inputmode="numeric"
        autocomplete="off"
        aria-describedby=${described('delay', true, problems) ?? nothing}
        aria-invalid=${problems.some((problem) => problem.severity === 'error') ? 'true' : 'false'}
        .value=${boxValue(String(this._edit.delay), this._typed.has('delay'))}
        @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
        @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
      />
      ${whys('delay', problems, this.#weight)}
    </div>`;
  }

  // Where the outcome is in the list, which a pointer can choose without dragging a row (WCAG 2.5.7); nothing where the
  // list has no other place for it.
  #place(): TemplateResult | typeof nothing {
    const data = this.data;
    const count = data ? (data.outcome ? data.count : data.count + 1) : 0;
    if (count < 2) return nothing;
    const at = this.#at();
    return html`<div class="item">
      <label class="label" for="place">${this.#term('outcome_place')}</label>
      <p class="help" id="place-help">${this.#term('outcome_placeHelp')}</p>
      <select
        id="place"
        class="short"
        aria-describedby="place-help"
        .value=${live(String(at))}
        @change=${(event: Event) => {
          this._at = Number((event.target as HTMLSelectElement).value);
          // The preview shows the outcome by its key, wherever it is in the list: a new place asks it nothing.
          this.#checker?.changed(this.#draft());
        }}
      >
        ${Array.from(
          { length: count },
          (_, place) =>
            html`<option value=${String(place)} ?selected=${place === at}>
              ${this.#term('outcome_placeOf', place + 1, count)}
            </option>`,
        )}
      </select>
    </div>`;
  }

  #rule(found: ReadonlyMap<OutcomeSetting, readonly Shown[]>): TemplateResult {
    const data = this.data;
    if (!data) return html``;
    const problems = this.#problemsAt('rule', found);
    const rule = this._edit.rule;
    // Placed after an outcome for every visitor, no visitor gets it, whatever its rule says.
    const draft = outcomesOf(this.#draft());
    const never = reachOf(draft)[draft.findIndex((one) => one.key === this.#key)] === 'never';
    const words = rule
      ? ruleWords(
          inspectorRuleWords(this.#draft(), data.types, data.culture, data.languages, (key, ...args) =>
            this.localize.term(key, ...args),
          ),
          rule,
        )
      : this.#term('outcome_noRule');
    return html`<div class="item">
      <p class="label" id="rule-label">${this.#term('outcome_rule')}</p>
      <p class="rule-words" id="rule-words">
        ${rule && rule.enabled === false ? this.#term('outcome_ruleOff', words) : words}
      </p>
      ${never ? html`<p class="never" id="rule-never">${this.#term('outcome_reach_never')}</p>` : nothing}
      <button
        type="button"
        id="rule"
        class="rule-button"
        aria-describedby=${['rule-words', never ? 'rule-never' : '', described('rule', false, problems)]
          .filter(Boolean)
          .join(' ')}
        @click=${() => void this.#buildRule()}
      >
        ${this.#term(rule ? 'outcome_changeRule' : 'outcome_addRule')}
      </button>
      ${whys('rule', problems, this.#weight)}
    </div>`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const found = this.#found();
    const kind = this._edit.kind;
    return html`<umb-body-layout
      headline=${data.outcome ? this.#term('outcome_headline', data.number) : this.#term('outcome_headlineNew')}
    >
      <div class="room">
        <div class="layout">
          <div class="outcome">
            ${this.#kind(found)}
            ${kind !== 'redirect' ? html`${this.#message(found)} ${this.#showAnswers(found)}` : nothing}
            ${kind !== 'message' ? this.#leadsTo(found) : nothing}
            ${kind === 'messageThenRedirect' ? this.#delay(found) : nothing} ${this.#place()} ${this.#rule(found)}
          </div>
          <jfb-outcome-preview
            .state=${this._preview}
            .sheets=${this._sheets}
            .culture=${data.culture}
            .retry=${() => this.#previewer?.retry()}
          ></jfb-outcome-preview>
          ${
            data.outcome
              ? html`<div class="delete-body">
                  <uui-button
                    look="secondary"
                    label=${this.#term('outcome_delete')}
                    @click=${() => this.#delete()}
                  ></uui-button>
                </div>`
              : nothing
          }
        </div>
      </div>
      ${
        data.outcome
          ? html`<uui-button
              slot="footer-info"
              class="delete-footer"
              look="secondary"
              label=${this.#term('outcome_delete')}
              @click=${() => this.#delete()}
            ></uui-button>`
          : nothing
      }
      <div slot="actions" class="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.#term(data.outcome ? 'outcome_update' : 'outcome_add')}
          .state=${this._checking ? 'waiting' : undefined}
          @click=${() => void this.#submit()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-outcome-modal')) customElements.define('jfb-outcome-modal', OutcomeModalElement);

export { OutcomeModalElement as element };
