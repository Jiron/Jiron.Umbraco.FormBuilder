// The form check (ROADMAP 9.5f, IA 5.1, ADR-0181): a sidebar from the toolbar's badge that lists what the check of the
// draft found, errors first (they block publishing), then warnings, then suggestions, each problem with the element it
// is on and what is wrong in a sentence, and Fix it where the designer can lead to it. It follows the check while it
// is open: an answer still awaited shows the last one until it comes.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { DesignDefinition } from '../definition';
import { elementNames } from '../rules/rule-names';
import { issueWords, type CheckWordsContext } from './check-words';
import { bySeverity, fixOf, type CheckIssue, type CheckSeverity, type FixTarget } from './form-check';
import { UNCHECKED, type CheckState } from './form-checker';
import type { FormCheckModalData, FormCheckModalValue } from './form-check.modal.token';

/** How each weight of problem is drawn and headed. */
const SEVERITIES: readonly { readonly severity: CheckSeverity; readonly icon: string; readonly heading: string }[] = [
  { severity: 'error', icon: 'icon-alert', heading: 'jfbCheck_errors' },
  { severity: 'warning', icon: 'icon-info', heading: 'jfbCheck_warnings' },
  { severity: 'hint', icon: 'icon-lightbulb-active', heading: 'jfbCheck_hints' },
];

export class FormCheckModalElement extends UmbModalBaseElement<FormCheckModalData, FormCheckModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _check: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      /* The sidebar's whole height, so its actions sit at its foot. */
      :host {
        display: block;
        height: 100%;
      }

      .found {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      uui-box {
        --uui-box-default-padding: 0;
      }

      .blocks {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      ul {
        margin: 0;
        padding: 0;
        list-style: none;
        container-type: inline-size;
      }

      li {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr) auto;
        gap: var(--uui-size-space-3) var(--uui-size-space-4);
        align-items: start;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
      }

      /* On a phone's narrow sidebar, Fix it goes under the sentence, which keeps the width to read in. */
      @container (max-width: 400px) {
        li > uui-button {
          grid-column: 2;
          justify-self: start;
        }
      }

      li + li {
        border-block-start: 1px solid var(--uui-color-divider);
      }

      li > uui-icon {
        margin-block-start: 2px;
        font-size: 20px;
        color: var(--jfb-bo-color-text-muted);
      }

      li[data-severity='error'] > uui-icon {
        color: var(--uui-color-danger-standalone);
      }

      li[data-severity='warning'] > uui-icon {
        color: var(--uui-color-warning-standalone);
      }

      .lines {
        display: grid;
        gap: var(--uui-size-space-1);
        min-inline-size: 0;
        overflow-wrap: anywhere;
      }

      .name {
        font-weight: 700;
      }

      .said {
        color: var(--jfb-bo-color-text-muted);
      }

      .state {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .clear {
        display: grid;
        justify-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-layout-2) var(--uui-size-space-5);
        text-align: center;
      }

      .clear uui-icon {
        font-size: 24px;
        color: var(--uui-color-positive-standalone);
      }

      .clear p,
      .failed p {
        margin: 0;
      }

      .failed {
        display: grid;
        justify-items: start;
        gap: var(--uui-size-space-4);
      }
    `,
  ];

  declare private _check: CheckState;

  override connectedCallback() {
    super.connectedCallback();
    this._check = UNCHECKED;
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.data?.closed?.();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    this.data?.opened?.();
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    if (changed.has('data') && this.data) this.observe(this.data.check, (check) => (this._check = check), 'check');
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  #fix(target: FixTarget) {
    this.value = { fix: target };
    this._submitModal();
  }

  #issue(issue: CheckIssue, icon: string, definition: DesignDefinition, context: CheckWordsContext) {
    const words = issueWords(issue, definition, context);
    const target = fixOf(definition, issue.path, context.types);
    return html`<li data-severity=${issue.severity} data-code=${issue.code}>
      <uui-icon name=${icon} aria-hidden="true"></uui-icon>
      <span class="lines">
        <span class="name">${words.name}</span>
        <span class="said">${words.said}</span>
      </span>
      ${
        target
          ? html`<uui-button
              compact
              look="outline"
              label=${this.#term('jfbCheck_fixNamed', words.name)}
              @click=${() => this.#fix(target)}
              >${this.#term('jfbCheck_fix')}</uui-button
            >`
          : nothing
      }
    </li>`;
  }

  #found() {
    const check = this._check;
    if (check.status === 'failed')
      return html`<div class="failed" role="alert">
        <p>${this.#term('jfbCheck_failed')}</p>
        <uui-button
          look="secondary"
          label=${this.#term('jfbCheck_retry')}
          @click=${() => this.data?.retry()}
        ></uui-button>
      </div>`;
    const definition = check.definition;
    if (!definition)
      return html`<p class="state" role="status">
        <uui-loader-circle></uui-loader-circle>${this.#term('jfbCheck_checking')}
      </p>`;
    const found = bySeverity(check.issues);
    const context: CheckWordsContext = {
      term: this.#term,
      say: (text) => (text ? this.localize.string(text) : ''),
      types: this.data?.types ?? {},
      names: elementNames(definition, this.data?.culture ?? '', this.#term),
    };
    const again =
      check.status === 'checking'
        ? html`<p class="state" role="status">
            <uui-loader-circle></uui-loader-circle>${this.#term('jfbCheck_checkingAgain')}
          </p>`
        : nothing;
    if (check.issues.length === 0)
      return html`${again}
        <div class="clear">
          <uui-icon name="icon-check" aria-hidden="true"></uui-icon>
          <p>${this.#term('jfbCheck_none')}</p>
        </div>`;
    return html`<div class="found">
      ${again}
      ${SEVERITIES.map(({ severity, icon, heading }) => {
        const issues = found[severity];
        if (issues.length === 0) return nothing;
        return html`<uui-box headline-variant="h3" headline=${this.#term(heading, issues.length)}>
          ${
            severity === 'error'
              ? html`<span slot="header-actions" class="blocks">${this.#term('jfbCheck_blocks')}</span>`
              : nothing
          }
          <ul>
            ${issues.map((issue) => this.#issue(issue, icon, definition, context))}
          </ul>
        </uui-box>`;
      })}
    </div>`;
  }

  override render() {
    return html`<umb-body-layout headline=${this.#term('jfbCheck_headline')}>
      ${this.#found()}
      <div slot="actions">
        <uui-button label=${this.#term('general_close')} @click=${() => this._rejectModal()}></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-form-check-modal')) customElements.define('jfb-form-check-modal', FormCheckModalElement);

export { FormCheckModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-check-modal': FormCheckModalElement;
  }
}
