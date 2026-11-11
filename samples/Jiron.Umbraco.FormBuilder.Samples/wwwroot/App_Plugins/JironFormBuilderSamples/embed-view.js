// @ts-check
// The embed sample, a view of a site's own on the form workspace (docs/extending.md, section 7): how a developer places
// the open form on a page, in a Razor view and on another site. A `workspaceView` manifest with the condition
// `Umb.Condition.WorkspaceAlias` matching `Jiron.FormBuilder.Workspace.Form` adds it beside the form's own views; it reads
// the form from Umbraco's workspace context, which for a form gives the facts JfbFormWorkspaceContext declares.
import { css, html, LitElement, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';

/** @typedef {import('../../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbFormWorkspaceContext} JfbFormWorkspaceContext */

/** The form workspace's context: Umbraco's workspace context of the entity type `jfb-form`. */
const FORM_WORKSPACE = new UmbContextToken(
  'UmbWorkspaceContext',
  undefined,
  /** @returns {context is JfbFormWorkspaceContext} */
  (/** @type {any} */ context) => context?.getEntityType?.() === 'jfb-form',
);

/**
 * Text inside an HTML attribute's double quotes.
 * @param {string} text
 */
const attribute = (text) => text.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');

export default class SampleEmbedView extends UmbElementMixin(LitElement) {
  static properties = {
    _alias: { state: true },
    _copied: { state: true },
  };

  constructor() {
    super();
    /** @type {string | undefined} The form's alias, once the workspace read it. */
    this._alias = undefined;
    /** @type {'razor' | 'headless' | 'failed' | undefined} What was copied last, or that a copy failed. */
    this._copied = undefined;
    this.consumeContext(FORM_WORKSPACE, (context) => {
      this.observe(context?.form, (form) => (this._alias = form?.alias), 'form');
    });
  }

  render() {
    if (!this._alias) return nothing;
    const alias = attribute(this._alias);
    const site = attribute(window.location.origin);
    const razor = `<jfb-render form="${alias}" />`;
    const headless = [
      `<script type="module" src="${site}/jfb/assets/element/jfb-element.js"></script>`,
      `<jfb-form api="${site}" form="${alias}"></jfb-form>`,
    ].join('\n');
    const said =
      this._copied === 'failed'
        ? this.localize.term('jfbSamples_copyFailed')
        : this._copied
          ? this.localize.term('jfbSamples_copied')
          : nothing;
    return html`<uui-box headline-variant="h2" headline=${this.localize.term('jfbSamples_embedHeadline')}>
      ${this.#snippet('razor', 'jfbSamples_embedRazor', 'jfbSamples_embedRazorHelp', razor)}
      ${this.#snippet('headless', 'jfbSamples_embedHeadless', 'jfbSamples_embedHeadlessHelp', headless)}
      <p class="said" role="status">${said}</p>
    </uui-box>`;
  }

  /**
   * One way to place the form: its heading, what it is for, the code, and a button that copies the code.
   * @param {'razor' | 'headless'} id
   * @param {string} heading
   * @param {string} help
   * @param {string} code
   */
  #snippet(id, heading, help, code) {
    return html`<section aria-labelledby=${`${id}-heading`}>
      <h3 id=${`${id}-heading`}>${this.localize.term(heading)}</h3>
      <p id=${`${id}-help`}>${this.localize.term(help)}</p>
      <div class="code">
        <pre aria-labelledby=${`${id}-heading`} aria-describedby=${`${id}-help`} tabindex="0"><code>${code}</code></pre>
        <uui-button
          look="secondary"
          compact
          label=${`${this.localize.term('jfbSamples_copy')}: ${this.localize.term(heading)}`}
          .state=${this._copied === id ? 'success' : undefined}
          @click=${() => this.#copy(id, code)}
          >${this.localize.term('jfbSamples_copy')}</uui-button
        >
      </div>
    </section>`;
  }

  /**
   * Copies a snippet, and says whether it was copied.
   * @param {'razor' | 'headless'} id
   * @param {string} code
   */
  async #copy(id, code) {
    window.clearTimeout(this.#clearing);
    this._copied = undefined;
    try {
      await navigator.clipboard.writeText(code);
      this._copied = id;
    } catch {
      this._copied = 'failed';
    }
    // The tick and the words go after a moment, so the next copy is said again.
    this.#clearing = window.setTimeout(() => (this._copied = undefined), 3000);
  }

  /** @type {number | undefined} */
  #clearing = undefined;

  disconnectedCallback() {
    window.clearTimeout(this.#clearing);
    super.disconnectedCallback();
  }

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
    uui-box {
      max-inline-size: 60rem;
    }
    section + section {
      margin-block-start: var(--uui-size-space-6);
    }
    h3 {
      margin: 0 0 var(--uui-size-space-1);
      font-size: var(--uui-type-default-size);
    }
    p {
      margin: 0 0 var(--uui-size-space-3);
      color: var(--uui-color-text-alt);
    }
    .code {
      display: grid;
      grid-template-columns: 1fr auto;
      align-items: start;
      gap: var(--uui-size-space-3);
    }
    pre {
      margin: 0;
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      overflow-x: auto;
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface-alt);
      font-size: 0.8125rem;
      line-height: 1.6;
    }
    pre:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }
    .said {
      margin: var(--uui-size-space-4) 0 0;
    }
    .said:empty {
      margin: 0;
    }
    @media (max-width: 40rem) {
      .code {
        grid-template-columns: 1fr;
      }
    }
  `;
}

if (!customElements.get('sample-embed-view')) customElements.define('sample-embed-view', SampleEmbedView);
