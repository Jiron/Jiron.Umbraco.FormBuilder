// The rules overview (ROADMAP 9.5c, IA 5.1, ADR-0178): a sidebar from the canvas's "..." that lists every rule of the
// form by page, each element's rules said as the builder says them, then the outcomes' rules under After sending. Each
// row leads to its element (an outcome's to Settings, where outcomes are edited); a field whose hide and require rules
// hold for the same answers says so on its row. Add rule first asks what the rule affects, then the builder opens on
// it. A form without rules says, in one sentence, what most rules do.
import { conflictOf, formSight, type Conflict, type SearchScope } from '@jfb/logic';
import { FORMULAS } from '@jfb/logic/calc';
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { allFields, type DesignDefinition, type DesignRule } from '../definition';
import { logicOf } from '../editor-model';
import { builderEffectsOf, clashOf, fieldKinds } from './rule-draft';
import { ownOptionValues, rulesByPage, ruledOutcomes, type RuledElement, type RuledOutcome } from './rule-list';
import { elementNames, inspectorRuleWords, typeFactsOf } from './rule-names';
import {
  conditionWords,
  kindOfCondition,
  ruleWords,
  subjectWords,
  type RuleWordsContext,
  type WordsCondition,
} from './rule-words';
import type { RulesOverviewModalData, RulesOverviewModalValue } from './rules-overview.modal.token';

/** The ellipsis a message cut short ends with. */
const ELLIPSIS = String.fromCharCode(0x2026);

/** Today as the browser's clock has it, `YYYY-MM-DD`, from which days counted from today are tried. */
const localToday = (): string => {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
};

export class RulesOverviewModalElement extends UmbModalBaseElement<RulesOverviewModalData, RulesOverviewModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _adding: { state: true },
    _chosen: { state: true },
    _tried: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      /* The sidebar's whole height, so its actions sit at its foot. */
      :host {
        display: block;
        height: 100%;
      }

      /* The header insets its headline but not its action menu, so Add rule takes the headline's inset. */
      #add-rule {
        margin-inline-end: var(--uui-size-layout-1);
      }

      .overview {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      uui-box {
        --uui-box-default-padding: 0;
      }

      ul {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      li + li {
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .row {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        gap: var(--uui-size-space-1) var(--uui-size-space-3);
        inline-size: 100%;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      .row:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .row:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: -2px;
      }

      .row > uui-icon {
        margin-block-start: 3px;
        color: var(--jfb-bo-color-text-muted);
      }

      .lines {
        display: grid;
        gap: var(--uui-size-space-1);
        min-inline-size: 0;
        overflow-wrap: anywhere;
      }

      .name {
        color: var(--uui-color-interactive);
        font-weight: 700;
      }

      .row:hover .name,
      .row:focus-visible .name {
        text-decoration: underline;
      }

      .said {
        color: var(--jfb-bo-color-text-muted);
      }

      .off {
        margin-inline-start: var(--uui-size-space-2);
        padding: 0 var(--uui-size-space-2);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--jfb-bo-radius-round);
        color: var(--uui-color-text);
        font-size: var(--uui-type-small-size);
      }

      .conflict {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        gap: var(--uui-size-space-2);
        margin-block-start: var(--uui-size-space-2);
        color: var(--uui-color-text);
      }

      .conflict > uui-icon {
        margin-block-start: 3px;
        color: var(--uui-color-warning-standalone);
      }

      .add {
        display: grid;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-5);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .add label {
        font-weight: 700;
      }

      .add select {
        box-sizing: border-box;
        inline-size: 100%;
        min-height: 36px;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      .add select:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 0;
        border-color: var(--uui-color-focus);
      }

      .add select[aria-invalid='true'] {
        border-color: var(--uui-color-danger-standalone);
      }

      .help,
      .empty {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .why {
        margin: 0;
        color: var(--uui-color-danger-standalone);
      }

      .add-actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }
    `,
  ];

  declare private _adding: boolean;
  declare private _chosen: string;
  declare private _tried: boolean;

  override connectedCallback() {
    super.connectedCallback();
    this._adding = false;
    this._chosen = '';
    this._tried = false;
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.data?.closed?.();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    this.data?.opened?.();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  #words(): RuleWordsContext | null {
    const data = this.data;
    return data ? inspectorRuleWords(data.definition, data.types, data.culture, data.languages, this.#term) : null;
  }

  #names(definition: DesignDefinition): Record<string, string> {
    return elementNames(definition, this.data?.culture ?? '', this.#term);
  }

  #go(value: RulesOverviewModalValue) {
    this.value = value;
    this._submitModal();
  }

  // Adding a rule: first what it affects.

  async #startAdding() {
    this._adding = true;
    this._tried = false;
    await this.updateComplete;
    this.shadowRoot?.querySelector<HTMLElement>('#affects')?.focus();
  }

  async #stopAdding() {
    this._adding = false;
    this._chosen = '';
    await this.updateComplete;
    this.shadowRoot?.querySelector<HTMLElement>('#add-rule')?.focus();
  }

  #continue() {
    if (this._chosen === '') {
      this._tried = true;
      this.shadowRoot?.querySelector<HTMLElement>('#affects')?.focus();
      return;
    }
    this.#go({ go: 'add', key: this._chosen });
  }

  #addStep() {
    const data = this.data;
    if (!data) return nothing;
    const facts = typeFactsOf(data.types);
    // An element whose every effect already has its rule takes no new one, as in the inspector.
    const takes = (key: string) =>
      builderEffectsOf(data.definition, key, (type) => facts[type]).some(
        (effect) => !clashOf(data.definition, key, effect, ''),
      );
    const names = this.#names(data.definition);
    const lacking = this._tried && this._chosen === '';
    return html`<div class="add" role="group" aria-labelledby="affects-label">
      <label id="affects-label" for="affects">${this.#term('jfbRules_overviewAffects')}</label>
      <p class="help" id="affects-help">${this.#term('jfbRules_overviewAffectsHelp')}</p>
      <select
        id="affects"
        aria-describedby=${lacking ? 'affects-help affects-why' : 'affects-help'}
        aria-invalid=${lacking ? 'true' : 'false'}
        @change=${(event: Event) => (this._chosen = (event.target as HTMLSelectElement).value)}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key !== 'Enter') return;
          event.preventDefault();
          this.#continue();
        }}
      >
        <option value="" ?selected=${this._chosen === ''}>${this.#term('jfbRules_overviewChoose')}</option>
        ${data.definition.pages.map(
          (page) =>
            html`<optgroup label=${names[page.key] ?? ''}>
              ${[
                takes(page.key)
                  ? this.#option(page.key, this.#term('jfbRules_targetPage', names[page.key] ?? ''))
                  : nothing,
                ...page.groups.flatMap((group) => [
                  takes(group.key)
                    ? this.#option(group.key, this.#term('jfbRules_targetGroup', names[group.key] ?? ''))
                    : nothing,
                  ...allFields({ pages: [{ key: '', groups: [group] }] })
                    .filter((field) => takes(field.key))
                    .map((field) => this.#option(field.key, names[field.key] ?? '')),
                ]),
              ]}
            </optgroup>`,
        )}
      </select>
      ${lacking ? html`<p class="why" id="affects-why">${this.#term('jfbRules_overviewChooseFirst')}</p>` : nothing}
      <div class="add-actions">
        <uui-button
          look="primary"
          label=${this.#term('jfbRules_overviewContinue')}
          @click=${() => this.#continue()}
        ></uui-button>
        <uui-button label=${this.#term('general_cancel')} @click=${() => void this.#stopAdding()}></uui-button>
      </div>
    </div>`;
  }

  #option(key: string, words: string) {
    return html`<option value=${key} ?selected=${this._chosen === key}>${words}</option>`;
  }

  // The rows.

  /** A conflict of a field's rules, in words: the answers it holds for, or that it holds whatever they are. */
  #conflictWords(conflict: Conflict, words: RuleWordsContext): string {
    if (conflict.answers.length === 0) return this.#term('jfbRules_overviewConflictAlways');
    const said = conflict.answers.map(({ subject, values, noneOf }) => {
      const condition: WordsCondition = subject.startsWith('field:')
        ? { field: subject.slice('field:'.length) }
        : { context: subject.slice('context:'.length) };
      const kind = kindOfCondition(words, condition);
      // An answer the rules don't name stands for every other: none of the texts they name, or any at all.
      if (noneOf)
        return conditionWords(
          words,
          noneOf.length > 0 ? { ...condition, op: 'isNoneOf', value: [...noneOf] } : { ...condition, op: 'isNotEmpty' },
        );
      if (values.length === 0)
        return conditionWords(words, {
          ...condition,
          op: kind === 'checkbox' || condition.context === 'memberSignedIn' ? 'isNotChecked' : 'isEmpty',
        });
      if (kind === 'checkbox' || condition.context === 'memberSignedIn')
        return conditionWords(words, { ...condition, op: 'isChecked' });
      if (kind === 'files')
        return this.#term('jfbRules_overviewConflictFiles', subjectWords(words, condition), values.length);
      if (kind === 'choices') return conditionWords(words, { ...condition, op: 'isChecked', value: [...values] });
      return conditionWords(words, { ...condition, op: 'equals', value: values[0] ?? '' });
    });
    return this.#term('jfbRules_overviewConflict', said.join(` ${this.#term('jfbRules_and')} `));
  }

  /** Each field's conflict in words, found once: the form doesn't change while the overview is open. */
  #conflicts: Map<string, string> | null = null;

  #conflict(element: RuledElement, words: RuleWordsContext): string | null {
    const data = this.data;
    if (!data || element.kind !== 'field') return null;
    if (this.#conflicts === null) {
      const facts = typeFactsOf(data.types);
      const factsOf = (type: string) => facts[type];
      const scope: SearchScope = {
        fields: fieldKinds(data.definition, factsOf),
        options: (key) => ownOptionValues(data.definition, key),
        cultures: data.languages.map((language) => language.culture),
        today: localToday(),
        // A field the form hides for the answers tried has none, as on the website, so a conflict said is one there.
        hidden: formSight(logicOf(data.definition, { factsOf }), FORMULAS),
      };
      this.#conflicts = new Map();
      for (const ruled of rulesByPage(data.definition).flatMap(({ elements }) => elements)) {
        if (ruled.kind !== 'field') continue;
        // The field's own rules (a message rule the search passes by); the form check also weighs its group's and its
        // page's (ADR-0179).
        const rules = ruled.rules.map(({ rule }) => rule);
        const requiring = rules.find((rule) => rule.effect === 'require');
        const conflict = requiring ? conflictOf(requiring, rules, scope) : null;
        if (conflict) this.#conflicts.set(ruled.key, this.#conflictWords(conflict, words));
      }
    }
    return this.#conflicts.get(element.key) ?? null;
  }

  #said(rule: DesignRule, words: RuleWordsContext) {
    return html`<span class="said"
      >${ruleWords(words, rule)}
      ${rule.enabled === false ? html`<span class="off">${this.#term('jfbRules_overviewOff')}</span>` : nothing}</span
    >`;
  }

  #element(element: RuledElement, names: Record<string, string>, words: RuleWordsContext) {
    const name = names[element.key] ?? '';
    const conflict = this.#conflict(element, words);
    return html`<li>
      <button
        type="button"
        class="row"
        data-key=${element.key}
        @click=${() => this.#go({ go: 'element', key: element.key })}
      >
        <uui-icon name="icon-split" aria-hidden="true"></uui-icon>
        <span class="lines">
          <span class="name"
            >${
              element.kind === 'page'
                ? this.#term('jfbRules_targetPage', name)
                : element.kind === 'group'
                  ? this.#term('jfbRules_targetGroup', name)
                  : name
            }</span
          >
          ${element.rules.map(({ rule }) => this.#said(rule, words))}
          ${
            conflict
              ? html`<span class="conflict"
                  ><uui-icon name="icon-alert" aria-hidden="true"></uui-icon><span>${conflict}</span></span
                >`
              : nothing
          }
        </span>
      </button>
    </li>`;
  }

  #outcomeName(outcome: RuledOutcome): string {
    if (outcome.kind === 'redirect')
      return outcome.url !== ''
        ? this.#term('jfbRules_outcomeRedirect', outcome.url)
        : outcome.toPage
          ? this.#term('jfbRules_outcomeRedirectPage')
          : this.#term('jfbRules_outcomeNumber', outcome.index + 1);
    if (outcome.message === '') return this.#term('jfbRules_outcomeNumber', outcome.index + 1);
    // A long message is said by its start; the whole of it is in Settings.
    const text = outcome.message.length > 60 ? `${outcome.message.slice(0, 59).trimEnd()}${ELLIPSIS}` : outcome.message;
    return this.#term('jfbRules_outcomeMessage', text);
  }

  #outcome(outcome: RuledOutcome, words: RuleWordsContext) {
    return html`<li>
      <button type="button" class="row" data-outcome=${outcome.index} @click=${() => this.#go({ go: 'outcomes' })}>
        <uui-icon name="icon-split" aria-hidden="true"></uui-icon>
        <span class="lines">
          <span class="name">${this.#outcomeName(outcome)}</span>
          ${outcome.rules.map((rule) => this.#said(rule, words))}
        </span>
      </button>
    </li>`;
  }

  override render() {
    const data = this.data;
    const words = this.#words();
    if (!data || !words) return nothing;
    const names = this.#names(data.definition);
    const pages = rulesByPage(data.definition);
    const outcomes = ruledOutcomes(data.definition, data.culture);
    const empty = pages.length === 0 && outcomes.length === 0;
    return html`<umb-body-layout headline=${this.#term('jfbRules_overviewHeadline')}>
      <uui-button
        slot="action-menu"
        id="add-rule"
        look="outline"
        label=${this.#term('jfbRules_overviewAdd')}
        ?disabled=${this._adding}
        @click=${() => void this.#startAdding()}
      >
        <uui-icon name="icon-add"></uui-icon>${this.#term('jfbRules_overviewAdd')}
      </uui-button>
      <div class="overview">
        ${this._adding ? this.#addStep() : nothing}
        ${
          empty
            ? html`<p class="empty">${this.#term('jfbRules_overviewEmpty')}</p>`
            : html`${pages.map(
                (ruled) =>
                  html`<uui-box
                    headline-variant="h4"
                    headline=${
                      ruled.page.title && Object.values(ruled.page.title).some((text) => text.trim() !== '')
                        ? this.#term('jfbRules_overviewPageNamed', ruled.index + 1, names[ruled.page.key] ?? '')
                        : this.#term('jfbRules_overviewPage', ruled.index + 1)
                    }
                  >
                    <ul>
                      ${ruled.elements.map((element) => this.#element(element, names, words))}
                    </ul>
                  </uui-box>`,
              )}
              ${
                outcomes.length > 0
                  ? html`<uui-box headline-variant="h4" headline=${this.#term('jfbRules_overviewAfter')}>
                      <ul>
                        ${outcomes.map((outcome) => this.#outcome(outcome, words))}
                      </ul>
                    </uui-box>`
                  : nothing
              }`
        }
      </div>
      <div slot="actions">
        <uui-button label=${this.#term('general_close')} @click=${() => this._rejectModal()}></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-rules-overview-modal'))
  customElements.define('jfb-rules-overview-modal', RulesOverviewModalElement);

export { RulesOverviewModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-rules-overview-modal': RulesOverviewModalElement;
  }
}
