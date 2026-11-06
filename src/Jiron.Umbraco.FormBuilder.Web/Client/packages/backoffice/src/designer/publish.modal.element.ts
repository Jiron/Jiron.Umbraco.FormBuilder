// The publish dialog (docs/ux/information-architecture.md, section 5.7): it asks before the draft goes live, with an
// optional comment that the version keeps. It shows the form check's result first (ROADMAP 9.5f, ADR-0181): errors
// block publishing, each with Fix it, and warnings are listed with theirs, since the badge counts both; Publish waits
// for the check's answer, and a check that gives none leaves publishing to the server's own check. Then what changed
// since the version visitors get, unsaved changes included, in History's parts, or what a first version holds
// (ROADMAP 10.3d): a region that stays in place says the wait, then one sentence, which a screen reader hears; the
// parts follow it, to read on.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { issueWords, type CheckWordsContext } from './check/check-words';
import { bySeverity, fixOf, type CheckIssue, type FixTarget } from './check/form-check';
import { UNCHECKED, type CheckState } from './check/form-checker';
import type { PublishChanges } from './history/publish-changes';
import { elementNames } from './rules/rule-names';
import type { PublishModalData, PublishModalValue } from './publish.modal.token';

/** The longest comment a version keeps. */
export const MAX_COMMENT = 500;

/** How many problems of each weight the dialog lists; the form check lists them all. */
export const MOST_LISTED = 5;

export class PublishModalElement extends UmbModalBaseElement<PublishModalData, PublishModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _comment: { state: true },
    _check: { state: true },
    _changes: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      p {
        margin: 0 0 var(--uui-size-space-5);
      }

      uui-textarea {
        width: 100%;
      }

      .result,
      .said {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      /* The result keeps its room only while it says something. */
      .result:has(.note, .state) {
        margin-block-end: var(--uui-size-space-5);
      }

      /* A note as the wireframes draw one: a quiet frame, its weight said by the icon beside its first line. */
      .note {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .note > p {
        margin: 0;
      }

      .lead {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
      }

      :is(.danger, .warning) .lead {
        font-weight: 700;
      }

      .lead uui-icon {
        flex: none;
        margin-block-start: 2px;
        font-size: 16px;
      }

      .danger .lead uui-icon {
        color: var(--jfb-bo-color-danger-text);
      }

      .warning .lead uui-icon {
        color: var(--jfb-bo-color-warning-text);
      }

      .clear .lead uui-icon {
        color: var(--jfb-bo-color-positive-text);
      }

      .failed .lead uui-icon {
        color: var(--jfb-bo-color-text-muted);
      }

      /* The problems line up under the lead's words, past its icon. */
      ul {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        padding-inline-start: calc(16px + var(--uui-size-space-3));
        list-style: none;
      }

      li {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--uui-size-space-3);
        align-items: start;
        overflow-wrap: anywhere;
      }

      .name {
        font-weight: 700;
      }

      .more,
      .state {
        color: var(--jfb-bo-color-text-muted);
      }

      .state {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      /* What changed: History's parts, each a quiet label over its lines. */
      .changes {
        margin-block-end: var(--uui-size-space-5);
      }

      .changes > p {
        margin: 0;
      }

      .changes section,
      .changes dl,
      .changes dd {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
      }

      .changes h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      .changes dt {
        color: var(--jfb-bo-color-text-muted);
      }

      .changes ul {
        gap: var(--uui-size-space-2);
        padding-inline-start: var(--uui-size-space-5);
        list-style: disc;
      }

      .changes li {
        display: list-item;
      }

      .changes dd p {
        margin: 0;
      }
    `,
  ];

  declare private _comment: string;
  declare private _check: CheckState;
  /** What changed, once it is said; undefined while it is read. */
  declare private _changes: PublishChanges | undefined;

  override connectedCallback() {
    super.connectedCallback();
    this._comment = '';
    this._check = UNCHECKED;
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.data?.closed?.();
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    if (changed.has('data') && this.data) {
      this.observe(this.data.check, (check) => (this._check = check), 'check');
      this._changes = undefined;
      void this.data.changes?.said.then((said) => (this._changes = said));
    }
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  #confirm() {
    this.value = { comment: this._comment };
    this._submitModal();
  }

  #fix(target: FixTarget) {
    this.value = { fix: target };
    this._submitModal();
  }

  /** Whether Publish waits: for the check's answer, or for its errors to be fixed. */
  #blocked(): boolean {
    const check = this._check;
    return check.status === 'checking' || (check.status === 'checked' && check.issues.some(isError));
  }

  #issues(issues: readonly CheckIssue[], words: CheckWordsContext): TemplateResult {
    const definition = this._check.definition;
    const shown = issues.slice(0, MOST_LISTED);
    return html`<ul>
        ${shown.map((issue) => {
          if (!definition) return nothing;
          const said = issueWords(issue, definition, words);
          const target = fixOf(definition, issue.path, words.types);
          return html`<li data-severity=${issue.severity} data-code=${issue.code}>
            <span><span class="name">${said.name}:</span> ${said.said}</span>
            ${
              target
                ? html`<uui-button
                    compact
                    look="outline"
                    label=${this.#term('jfbCheck_fixNamed', said.name)}
                    @click=${() => this.#fix(target)}
                    >${this.#term('jfbCheck_fix')}</uui-button
                  >`
                : nothing
            }
          </li>`;
        })}
      </ul>
      ${
        issues.length > shown.length
          ? html`<p class="more">${this.#term('jfbCheck_publishMore', issues.length - shown.length)}</p>`
          : nothing
      }`;
  }

  // The form check's result: what blocks, then what doesn't. The errors are an alert; everything else the region under
  // them says, which stays in place from "Checking the form..." to the answer, so a screen reader reads the answer out.
  #result(): TemplateResult {
    const check = this._check;
    const { error, warning } = check.status === 'checked' && check.definition ? bySeverity(check.issues) : NONE;
    const words: CheckWordsContext | null = check.definition
      ? {
          term: this.#term,
          say: (text) => (text ? this.localize.string(text) : ''),
          types: this.data?.types ?? {},
          names: elementNames(check.definition, this.data?.culture ?? '', this.#term),
        }
      : null;
    return html`<div class="result">
      ${
        error.length > 0 && words
          ? html`<section class="note danger" role="alert" aria-labelledby="check-errors">
              <p class="lead" id="check-errors">
                <uui-icon name="icon-alert" aria-hidden="true"></uui-icon
                ><span>${this.#term('jfbCheck_publishErrors', error.length)}</span>
              </p>
              ${this.#issues(error, words)}
            </section>`
          : nothing
      }
      <div class="said" role="status">${this.#said(check, error.length, warning, words)}</div>
    </div>`;
  }

  // What the status region says: the wait, no answer, no problems, or the warnings, which don't block.
  #said(
    check: CheckState,
    errors: number,
    warning: readonly CheckIssue[],
    words: CheckWordsContext | null,
  ): TemplateResult | typeof nothing {
    if (check.status === 'idle') return nothing;
    if (check.status === 'checking')
      return html`<p class="state"><uui-loader-circle></uui-loader-circle>${this.#term('jfbCheck_checking')}</p>`;
    if (check.status === 'failed' || !words)
      return html`<p class="note failed">
        <span class="lead"
          ><uui-icon name="icon-alert" aria-hidden="true"></uui-icon>${this.#term('jfbCheck_publishFailed')}</span
        >
      </p>`;
    if (errors === 0 && warning.length === 0)
      return html`<p class="note clear">
        <span class="lead"
          ><uui-icon name="icon-check" aria-hidden="true"></uui-icon>${this.#term('jfbCheck_publishClear')}</span
        >
      </p>`;
    if (warning.length === 0) return nothing;
    return html`<section class="note warning" aria-labelledby="check-warnings">
      <p class="lead" id="check-warnings">
        <uui-icon name="icon-info" aria-hidden="true"></uui-icon
        ><span
          >${this.#term(errors > 0 ? 'jfbCheck_publishAlsoWarnings' : 'jfbCheck_publishWarnings', warning.length)}</span
        >
      </p>
      ${this.#issues(warning, words)}
    </section>`;
  }

  // What changed since the version visitors get: the region says the wait, then a sentence, in place of the parts or
  // over them, where it is for a screen reader alone; the parts follow under their headline.
  #changes(): TemplateResult | typeof nothing {
    const asked = this.data?.changes;
    if (!asked) return nothing;
    const said = this._changes;
    const listed = said !== undefined && said.parts.length > 0;
    return html`<div class="changes">
      <p class=${said ? (listed ? 'visually-hidden' : '') : 'state'} role="status">
        ${said ? said.said : html`<uui-loader-circle></uui-loader-circle>${asked.comparing}`}
      </p>
      ${
        listed
          ? html`<section aria-labelledby="changes-title">
              <h4 id="changes-title">${said.headline}</h4>
              <dl>
                ${said.parts.map(
                  (part) =>
                    html`<dt>${part.name}</dt>
                      <dd>
                        <ul>
                          ${part.lines.map((line) => html`<li>${line}</li>`)}
                        </ul>
                        ${part.more ? html`<p class="more">${part.more}</p>` : nothing}
                      </dd>`,
                )}
              </dl>
            </section>`
          : nothing
      }
    </div>`;
  }

  override render() {
    const name = this.data?.name ?? '';
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_publishHeadline', name)}>
      ${this.#result()}
      ${
        // While an error blocks it, the dialog says what to fix and nothing of a version that can't be published yet.
        this._check.status === 'checked' && this._check.issues.some(isError)
          ? nothing
          : html`${this.#changes()}
              <p>${this.localize.term('jfbDesigner_publishText')}</p>
              <uui-label for="comment">${this.localize.term('jfbDesigner_comment')}</uui-label>
              <uui-textarea
                id="comment"
                label=${this.localize.term('jfbDesigner_comment')}
                maxlength=${MAX_COMMENT}
                .value=${this._comment}
                @input=${(event: Event) => (this._comment = (event.target as HTMLTextAreaElement).value)}
              ></uui-textarea>`
      }
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term('jfbDesigner_publishConfirm')}
        ?disabled=${this.#blocked()}
        @click=${() => this.#confirm()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

const isError = (issue: CheckIssue) => issue.severity === 'error';

/** No problems of either weight, before an answer. */
const NONE = { error: [], warning: [] } as const;

if (!customElements.get('jfb-publish-modal')) customElements.define('jfb-publish-modal', PublishModalElement);

export { PublishModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-publish-modal': PublishModalElement;
  }
}
