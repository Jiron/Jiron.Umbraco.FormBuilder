// An outcome's live preview in its sidebar (ROADMAP 10.1c, ADR-0187): the outcome as visitors get it, with the sample
// entry, in the site's look on a sheet like the canvas's; a screen reader reads it, but nothing in it is a stop of Tab
// and a press on it goes nowhere. A redirect shows as the one line of where visitors go. The last answer stays while
// the next is awaited, so a change never flashes.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { NO_PREVIEW, previewMarkup, type OutcomePreview, type PreviewState } from './outcome-preview';

// A press on the visitor's page, with any button of the pointer: a link or a button of the preview leads nowhere.
const goesNowhere = (event: Event) => event.preventDefault();

export class OutcomePreviewElement extends UmbLitElement {
  static override properties = {
    state: { attribute: false },
    sheets: { attribute: false },
    culture: { type: String },
    retry: { attribute: false },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      .preview {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      .help {
        margin: 0;
        color: var(--uui-color-text-alt);
      }

      /* The visitor's page: white, whatever the backoffice's theme, as on the canvas (ADR-0168). */
      .sheet {
        box-sizing: border-box;
        min-block-size: 96px;
        padding: var(--uui-size-space-6);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: #fff;
        color: #1d2433;
        overflow-wrap: anywhere;
      }

      .said {
        margin: 0;
      }

      /* Nothing on the visitor's page is pressed here. */
      .sheet .jfb-form,
      .sheet .jfb-form * {
        cursor: default;
      }

      .failed {
        display: grid;
        gap: var(--uui-size-space-3);
        justify-items: start;
      }

      .said code {
        font-family: var(--uui-font-family-monospace, monospace);
      }
    `,
  ];

  declare state: PreviewState;
  declare sheets: readonly CSSStyleSheet[];
  declare culture: string;
  /** Asks again after an ask that got no answer. */
  declare retry: () => void;

  /** What the sheet shows now, so a render that keeps it draws it once. */
  #drawn: { readonly into: Element; readonly preview: OutcomePreview } | null = null;

  constructor() {
    super();
    this.state = NO_PREVIEW;
    this.sheets = [];
    this.culture = '';
    this.retry = () => undefined;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbSettings_${key}`, ...args);

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const root = this.shadowRoot;
    if (changed.has('sheets') && root && 'adoptedStyleSheets' in root) {
      // The theme first, so the preview's own rules come after it.
      const own = OutcomePreviewElement.elementStyles.flatMap((style) =>
        style instanceof CSSStyleSheet ? [style] : style.styleSheet ? [style.styleSheet] : [],
      );
      root.adoptedStyleSheets = [...this.sheets, ...own];
    }
    const into = root?.querySelector('.jfb-form') ?? null;
    const preview = this.state.preview;
    if (!into || !preview?.html) {
      this.#drawn = null;
      return;
    }
    if (this.#drawn?.into === into && this.#drawn.preview === preview) return;
    into.replaceChildren(previewMarkup(document, preview.html, preview.delaySeconds));
    this.#drawn = { into, preview };
  }

  #shown() {
    const { status, preview } = this.state;
    // Said as it comes, as the designer's other failures are.
    if (status === 'failed')
      return html`<div class="failed">
        <p class="said" role="alert">${this.#term('outcome_previewFailed')}</p>
        <uui-button
          look="outline"
          label=${this.#term('outcome_previewRetry')}
          @click=${() => this.retry()}
        ></uui-button>
      </div>`;
    // A redirect shows visitors no page of the form, so its line stands on the sidebar, not on a visitor's page.
    if (preview && !preview.html && preview.address)
      return html`<p class="said">${this.#term('outcome_previewRedirect')} <code>${preview.address}</code></p>`;
    // The answer's markup goes in once it is drawn; the language and its direction are the visitors'.
    return html`<div class="sheet">
      <div
        class="jfb-form jfb-theme-default"
        lang=${this.culture || nothing}
        dir=${preview?.direction ?? nothing}
        @click=${goesNowhere}
        @auxclick=${goesNowhere}
      ></div>
    </div>`;
  }

  override render() {
    const busy = this.state.status === 'idle' || this.state.status === 'asking';
    return html`<section class="preview" aria-labelledby="preview-heading">
      <h4 id="preview-heading">${this.#term('outcome_preview')}</h4>
      <p class="help">${this.#term('outcome_previewHelp')}</p>
      <div class="shown" aria-busy=${busy ? 'true' : 'false'}>${this.#shown()}</div>
    </section>`;
  }
}

if (!customElements.get('jfb-outcome-preview')) customElements.define('jfb-outcome-preview', OutcomePreviewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-outcome-preview': OutcomePreviewElement;
  }
}
