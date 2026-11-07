// An uploaded image of an entry (ROADMAP 11.2, IA 6.3, Files): a dialog that shows it through the authorised download,
// with Download. The image is read as bytes with the backoffice's token and shown from a local address, which it drops
// when the dialog closes; SVG and HTML never come here (7.3).
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { saveBlob } from '../library/actions/export-form.action';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { fileBlob } from './entry-panel-api';
import type { EntryImageModalData } from './entry-modals';

export class EntryImageModalElement extends UmbModalBaseElement<EntryImageModalData, never> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _url: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      .meta {
        margin: 0 0 var(--uui-size-space-4);
        color: var(--jfb-bo-color-text-muted);
      }

      .stage {
        display: grid;
        place-items: center;
        padding: var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-background);
      }

      img {
        display: block;
        max-inline-size: 100%;
        max-block-size: 60vh;
        object-fit: contain;
      }

      .failed {
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _url: string | undefined;
  declare private _failed: boolean;
  #blob: Blob | undefined;

  constructor() {
    super();
    this._failed = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    if (this._url) URL.revokeObjectURL(this._url);
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #read() {
    if (!this.data) return;
    const blob = await fileBlob(this, this.data.form, this.data.file);
    this._failed = !blob;
    this.#blob = blob;
    if (blob) this._url = URL.createObjectURL(blob);
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.data?.name ?? ''}>
      <p class="meta">${this.data?.meta ?? ''}</p>
      <div class="stage">
        ${
          this._url
            ? html`<img src=${this._url} alt=${this.data?.name ?? ''} />`
            : this._failed
              ? html`<p class="failed" role="alert">${this.#term('image_failed')}</p>`
              : skeletonLines(this.#term('panel_loading'), [80, 60])
        }
      </div>
      <uui-button slot="actions" label=${this.#term('keys_close')} @click=${this._rejectModal}></uui-button>
      ${
        this.#blob
          ? html`<uui-button
              slot="actions"
              look="primary"
              label=${this.#term('file_download')}
              @click=${() => this.#blob && saveBlob(this.data?.name ?? 'file', this.#blob)}
            ></uui-button>`
          : nothing
      }
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-entry-image-modal'))
  customElements.define('jfb-entry-image-modal', EntryImageModalElement);

export { EntryImageModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-image-modal': EntryImageModalElement;
  }
}
