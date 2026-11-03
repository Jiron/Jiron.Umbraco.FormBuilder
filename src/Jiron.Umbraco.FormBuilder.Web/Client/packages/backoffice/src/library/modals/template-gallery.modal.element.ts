// The template gallery (docs/ux/information-architecture.md, section 4; ADR-0166): the built-in templates, then the site's saved
// ones, as cards in one radio group with a search, and the chosen template's preview beside them. Below a 1000 px window the
// preview no longer fits: Preview opens it over the cards, with All templates to go back. Use template creates the form.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formExchangeFromTemplate, templateList, type TemplateResponseModel } from '../../api';
import { sayRefusal } from '../library-changes';
import { GalleryStore } from '../stores';
import '../../designer/preview/form-preview.element';
import type { NewFormModalData, NewFormModalValue } from './tokens';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';

/** The window width from which the preview stands beside the cards. */
const SIDE_BY_SIDE = '(min-width: 1000px)';

export class TemplateGalleryModalElement extends UmbModalBaseElement<NewFormModalData, NewFormModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _previewing: { state: true },
    _sideBySide: { state: true },
    _creating: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
        height: 100%;
      }

      .search {
        width: 100%;
        margin-bottom: var(--uui-size-space-5);
      }

      .gallery {
        display: grid;
        grid-template-columns: minmax(240px, 2fr) 3fr;
        gap: var(--uui-size-space-6);
        align-items: start;
      }

      .gallery--single {
        grid-template-columns: 1fr;
      }

      /* Below the sidebar's own heading (h3), as its boxes' are. */
      h4 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-3);
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      h4:first-child {
        margin-top: 0;
      }

      .cards {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .card {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--uui-size-space-1) var(--uui-size-space-3);
        padding: var(--uui-size-space-4);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        cursor: pointer;
        transition:
          border-color 120ms ease,
          background-color 120ms ease,
          transform 120ms ease-out;
      }

      .card:active {
        transform: scale(0.99);
      }

      @media (hover: hover) and (pointer: fine) {
        .card:hover {
          border-color: var(--uui-color-border-emphasis);
        }
      }

      .card:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 2px;
      }

      .card[aria-checked='true'] {
        border-color: var(--uui-color-selected);
        background: var(--uui-color-surface-alt);
      }

      .radio {
        grid-row: span 2;
        width: 16px;
        height: 16px;
        margin-top: 2px;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--jfb-bo-radius-round);
        background: var(--uui-color-surface);
      }

      /* A checked card's radio has a dot, as a checked radio does. */
      .card[aria-checked='true'] .radio {
        border-color: var(--uui-color-selected);
        background: var(--uui-color-selected);
        box-shadow: inset 0 0 0 3px var(--uui-color-surface);
      }

      .name {
        font-weight: 700;
      }

      .description,
      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .preview h4 {
        margin: 0 0 var(--uui-size-space-2);
        font-size: var(--uui-type-h4-size);
      }

      .preview p {
        margin: 0 0 var(--uui-size-space-4);
      }

      .back {
        margin-bottom: var(--uui-size-space-4);
      }

      .skeleton {
        height: 64px;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      @media (prefers-reduced-motion: reduce) {
        .card {
          transition: none;
        }

        .card:active {
          transform: none;
        }
      }
    `,
  ];

  declare private _previewing: boolean;
  declare private _sideBySide: boolean;
  declare private _creating: boolean;

  readonly #store = new GalleryStore(
    () => tryExecute(this, templateList({ query: { take: 500 } }), { disableNotifications: true }),
    { name: (template) => this.#name(template), description: (template) => this.#description(template) },
    () => this.requestUpdate(),
  );

  readonly #media = window.matchMedia(SIDE_BY_SIDE);
  readonly #onMedia = () => {
    this._sideBySide = this.#media.matches;
    if (this._sideBySide) this._previewing = false;
  };

  constructor() {
    super();
    this._previewing = false;
    this._sideBySide = this.#media.matches;
    this._creating = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#media.addEventListener('change', this.#onMedia);
    void this.#store.load();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#media.removeEventListener('change', this.#onMedia);
  }

  #name(template: TemplateResponseModel): string {
    return template.builtIn && template.alias
      ? this.localize.termOrDefault(`jfbLibrary_template_${template.alias}`, template.name)
      : template.name;
  }

  #description(template: TemplateResponseModel): string {
    return template.builtIn && template.alias
      ? this.localize.termOrDefault(`jfbLibrary_template_${template.alias}Description`, '')
      : (template.description ?? '');
  }

  async #use() {
    const template = this.#store.chosen;
    if (!template || this._creating) return;
    this._creating = true;
    const { data: form, error } = await tryExecute(
      this,
      formExchangeFromTemplate({
        body: { templateKey: template.key, name: this.#name(template), folderKey: this.data?.folderKey ?? null },
      }),
      { disableNotifications: true },
    );
    this._creating = false;
    if (!form) {
      await sayRefusal(this, error);
      return;
    }

    this.updateValue({ formKey: form.key });
    this._submitModal();
  }

  // Preview and All templates swap what the sidebar shows, and the button pressed goes with it: the focus moves to the
  // way back, or to the chosen card.
  async #showPreview(on: boolean) {
    this._previewing = on;
    await this.updateComplete;
    this.shadowRoot?.querySelector<HTMLElement>(on ? '.back' : '.card[aria-checked="true"]')?.focus();
  }

  // The cards are one radio group: the arrow keys move the choice and the focus, as a radio group's do.
  #onKey(event: KeyboardEvent) {
    const by =
      event.key === 'ArrowDown' || event.key === 'ArrowRight'
        ? 1
        : event.key === 'ArrowUp' || event.key === 'ArrowLeft'
          ? -1
          : 0;
    if (by === 0) return;
    event.preventDefault();
    this.#store.step(by);
    void this.updateComplete.then(() =>
      this.shadowRoot?.querySelector<HTMLElement>('.card[aria-checked="true"]')?.focus(),
    );
  }

  #onSearch(event: Event) {
    this.#store.search((event.target as HTMLInputElement).value);
  }

  #card(template: TemplateResponseModel) {
    const checked = template.key === this.#store.selected;
    return html`<div
      class="card"
      role="radio"
      aria-checked=${checked ? 'true' : 'false'}
      tabindex=${checked ? 0 : -1}
      @click=${() => this.#store.select(template.key)}
      @dblclick=${() => void this.#use()}
    >
      <span class="radio"></span>
      <span class="name">${this.#name(template)}</span>
      <span class="description">${this.#description(template)}</span>
    </div>`;
  }

  #cards() {
    const store = this.#store;
    if (store.templates.state === 'loading')
      return html`<div class="cards" aria-busy="true">
        <div class="skeleton"></div>
        <div class="skeleton"></div>
        <span class="visually-hidden">${this.localize.term('jfbLibrary_galleryLoading')}</span>
      </div>`;
    if (store.templates.state === 'failed')
      return html`<div class="failed" role="alert">
        <span>${this.localize.term('jfbLibrary_galleryFailed')}</span>
        <uui-button
          look="outline"
          label=${this.localize.term('jfbLibrary_tryAgain')}
          @click=${() => void store.load()}
        ></uui-button>
      </div>`;

    const visible = store.visible;
    if (visible.length === 0)
      return html`<div class="failed">
        <span>${this.localize.term('jfbLibrary_galleryNoMatch', store.query)}</span>
        <uui-button
          look="outline"
          label=${this.localize.term('jfbLibrary_clearSearch')}
          @click=${() => store.search('')}
        ></uui-button>
      </div>`;

    const builtIn = visible.filter((template) => template.builtIn);
    const saved = visible.filter((template) => !template.builtIn);
    const grouped = builtIn.length > 0 && saved.length > 0;
    return html`<div role="radiogroup" aria-label=${this.localize.term('jfbLibrary_templates')} @keydown=${this.#onKey}>
      ${grouped ? html`<h4>${this.localize.term('jfbLibrary_builtIn')}</h4>` : nothing}
      <div class="cards">${builtIn.map((template) => this.#card(template))}</div>
      ${grouped ? html`<h4>${this.localize.term('jfbLibrary_savedHere')}</h4>` : nothing}
      <div class="cards">${saved.map((template) => this.#card(template))}</div>
    </div>`;
  }

  #preview() {
    const template = this.#store.chosen;
    if (!template) return nothing;
    const description = this.#description(template);
    return html`<section class="preview" aria-label=${this.localize.term('jfbLibrary_preview')}>
      ${
        this._previewing
          ? html`<uui-button
              class="back"
              look="secondary"
              label=${this.localize.term('jfbLibrary_allTemplates')}
              @click=${() => void this.#showPreview(false)}
            ></uui-button>`
          : nothing
      }
      ${
        // Beside the cards, the checked card already names and describes the template: the preview says it once more only alone.
        this._previewing ? html`<h4>${this.#name(template)}</h4>` : nothing
      }
      <p class="muted">${this.localize.term('jfbLibrary_holds', template.holds.pages, template.holds.fields)}</p>
      ${this._previewing && description ? html`<p>${description}</p>` : nothing}
      <jfb-form-preview .definition=${template.definition}></jfb-form-preview>
    </section>`;
  }

  override render() {
    const ready = this.#store.templates.state === 'ready' && this.#store.chosen !== undefined;
    const gallery = this._sideBySide
      ? html`<div class="gallery">${this.#cards()}${this.#preview()}</div>`
      : html`<div class="gallery gallery--single">${this._previewing ? this.#preview() : this.#cards()}</div>`;
    return html`<umb-body-layout headline=${this.localize.term('jfbLibrary_galleryHeadline')}>
      ${
        this._previewing
          ? nothing
          : html`<uui-input
              class="search"
              type="search"
              label=${this.localize.term('jfbLibrary_searchTemplates')}
              placeholder=${this.localize.term('jfbLibrary_searchTemplates')}
              .value=${this.#store.query}
              @input=${this.#onSearch}
            ></uui-input>`
      }
      ${gallery}
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        ${
          !this._sideBySide && !this._previewing && ready
            ? html`<uui-button
                look="secondary"
                label=${this.localize.term('jfbLibrary_preview')}
                @click=${() => void this.#showPreview(true)}
              ></uui-button>`
            : nothing
        }
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('jfbLibrary_useTemplate')}
          ?disabled=${!ready}
          .state=${this._creating ? 'waiting' : undefined}
          @click=${this.#use}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-template-gallery-modal'))
  customElements.define('jfb-template-gallery-modal', TemplateGalleryModalElement);

export { TemplateGalleryModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-template-gallery-modal': TemplateGalleryModalElement;
  }
}
