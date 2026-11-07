// The preview (docs/ux/information-architecture.md 5.7, ADR-0203): the saved draft as visitors would get it, written by
// the server into a frame sandboxed from the backoffice (scripts, forms and links that open a tab of their own, never
// its origin), which takes the chosen width, or the room there is with "Narrowed to fit", and the sidebar's height,
// scrolling inside as a window does. The toolbar holds the widths, which start at the canvas's, and the "..." the
// colour scheme, for a theme that offers the dark one, and the site's languages. The frame asks here for a new ticket
// over a port of its own when its own runs short (frame-host.ts). A draft whose rules have errors is not shown, and says where they are listed.
import {
  css,
  html,
  keyed,
  nothing,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { CANVAS_WIDTHS, type CanvasWidth } from '../canvas/widths';
import { FrameHost } from './frame-host';
import type { PreviewModalData } from './preview.modal.token';
import type { PreviewRead } from './preview-read';

/** The frame's sandbox: its own scripts and forms, and links in a tab of their own; never the backoffice's origin. */
export const FRAME_SANDBOX = 'allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox';

type Shown = PreviewRead | { readonly kind: 'loading' };

export class PreviewModalElement extends UmbModalBaseElement<PreviewModalData, never> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _shown: { state: true },
    _width: { state: true },
    _culture: { state: true },
    _dark: { state: true },
    _narrowed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      .room {
        --bo-accent: var(--uui-color-selected, #3544b1);
        --bo-on-accent: var(--uui-color-selected-contrast, #fff);
        --bo-focus: var(--jfb-bo-color-focus);
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        block-size: 100%;
        min-block-size: 480px;
      }

      .bar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .widths {
        display: inline-flex;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
      }

      .widths button {
        min-height: 36px;
        padding: 0 var(--uui-size-space-4);
        border: 0;
        border-inline-start: 1px solid var(--jfb-bo-color-control-border);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .widths button:first-child {
        border-inline-start: 0;
      }

      @media (hover: hover) {
        .widths button:hover,
        .menu button:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .widths button[aria-pressed='true'] {
        background: var(--bo-accent);
        color: var(--bo-on-accent);
        font-weight: 700;
      }

      :is(.widths, .menu) button:focus-visible {
        position: relative;
        outline: 2px solid var(--bo-focus);
        outline-offset: -4px;
      }

      .widths button[aria-pressed='true']:focus-visible {
        outline-color: var(--bo-on-accent);
      }

      .narrowed {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .more {
        margin-inline-start: auto;
      }

      .menu {
        display: grid;
        min-width: 220px;
        padding: var(--uui-size-space-2) 0;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        box-shadow: var(--uui-shadow-depth-3);
        transform-origin: top right;
        transition:
          opacity var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease),
          transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
      }

      @starting-style {
        .menu {
          opacity: 0;
          transform: scale(0.97);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .menu {
          transition: opacity var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
        }

        @starting-style {
          .menu {
            transform: none;
          }
        }
      }

      .menu-label {
        margin: var(--uui-size-space-2) var(--uui-size-space-4) var(--uui-size-space-1);
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .menu-label + [role='group'] {
        display: grid;
      }

      .menu [role='group'] + .menu-label {
        margin-block-start: var(--uui-size-space-3);
        padding-block-start: var(--uui-size-space-3);
        border-block-start: 1px solid var(--uui-color-divider);
      }

      .menu button {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-5);
        min-height: 36px;
        padding: 0 var(--uui-size-space-4);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      .menu button[aria-pressed='true'] {
        font-weight: 700;
      }

      .menu button[aria-pressed='false'] uui-icon {
        visibility: hidden;
      }

      .stage {
        display: flex;
        flex: 1;
        justify-content: center;
        min-block-size: 0;
      }

      .frame {
        box-sizing: border-box;
        inline-size: min(100%, var(--frame-width));
        block-size: 100%;
        /* An outline, which takes no room: the frame's own window is the width chosen. */
        border: 0;
        outline: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      iframe.frame {
        display: block;
      }

      .state {
        display: grid;
        align-content: center;
        justify-items: center;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-layout-1);
        text-align: center;
      }

      .state p {
        max-inline-size: 60ch;
        margin: 0;
      }

      .state[data-kind='loading'] p {
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _shown: Shown;
  declare private _width: CanvasWidth;
  declare private _culture: string | null;
  declare private _dark: boolean;
  declare private _narrowed: boolean;

  #asked = 0;
  #host: FrameHost | null = null;
  #resize: ResizeObserver | null = null;

  constructor() {
    super();
    this._shown = { kind: 'loading' };
    this._width = 'desktop';
    this._culture = null;
    this._dark = false;
    this._narrowed = false;
  }

  override disconnectedCallback() {
    this.#resize?.disconnect();
    this.#resize = null;
    // Closed, the frame's port closes with it: no ticket is asked for any more.
    this.#host?.close();
    this.#host = null;
    super.disconnectedCallback();
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    if (changed.has('data') && this.data) {
      this._width = this.data.width;
      this._culture = this.data.culture;
      void this.#read();
    }
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    const stage = this.renderRoot.querySelector<HTMLElement>('.stage');
    if (!stage) return;
    this.#resize = new ResizeObserver(() => this.#measure());
    this.#resize.observe(stage);
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    if (changed.has('_width')) this.#measure();
  }

  /** The draft's document in the language and scheme chosen; an answer that comes after a later ask is dropped. */
  async #read() {
    const data = this.data;
    if (!data) return;
    const asked = ++this.#asked;
    this.#host?.close();
    this.#host = null;
    this._shown = { kind: 'loading' };
    const read = await data.read(this._culture, this._dark);
    if (asked !== this.#asked) return;
    if (read.kind === 'shown') {
      this._culture = read.culture;
      const root = this.renderRoot;
      const frame = {
        get contentWindow() {
          return root.querySelector('iframe')?.contentWindow ?? null;
        },
      };
      this.#host = new FrameHost(frame, data.form, read.version, read.lifetimeSeconds, (version) =>
        data.ticket(version),
      );
    }
    this._shown = read;
  }

  // "Narrowed to fit" while the stage is narrower than the chosen width.
  #measure() {
    const stage = this.renderRoot.querySelector<HTMLElement>('.stage');
    if (stage) this._narrowed = stage.clientWidth < CANVAS_WIDTHS[this._width];
  }

  #chooseWidth(width: CanvasWidth) {
    this._width = width;
  }

  #chooseScheme(dark: boolean) {
    this.#closeMenu();
    if (dark === this._dark) return;
    this._dark = dark;
    this.#host?.scheme(dark);
  }

  #chooseLanguage(culture: string) {
    this.#closeMenu();
    if (culture.toLowerCase() === this._culture?.toLowerCase()) return;
    this._culture = culture;
    void this.#read();
  }

  #closeMenu() {
    this.renderRoot.querySelector<HTMLElement & { hidePopover?: () => void }>('#jfb-preview-more')?.hidePopover?.();
  }

  #term(key: string, ...args: unknown[]) {
    return this.localize.term(`jfbPreview_${key}`, ...args);
  }

  #bar(): TemplateResult {
    return html`<div class="bar">
      <div class="widths" role="group" aria-label=${this.#term('width')}>
        ${(Object.keys(CANVAS_WIDTHS) as CanvasWidth[]).map(
          (width) =>
            html`<button
              type="button"
              aria-pressed=${this._width === width ? 'true' : 'false'}
              @click=${() => this.#chooseWidth(width)}
            >
              ${this.localize.term(`jfbDesigner_width_${width}`)}
            </button>`,
        )}
      </div>
      ${this._narrowed ? html`<span class="narrowed">${this.localize.term('jfbDesigner_narrowed')}</span>` : nothing}
      ${this.#more()}
    </div>`;
  }

  // The "..." holds the colour scheme, for a theme that offers the dark one, and the site's languages, when it has more
  // than one; with neither it is not there.
  #more(): TemplateResult | typeof nothing {
    const shown = this._shown;
    const schemes = shown.kind === 'shown' && shown.darkScheme;
    const languages = this.data?.languages ?? [];
    if (!schemes && languages.length < 2) return nothing;
    const culture = this._culture?.toLowerCase();
    const choice = (label: string, pressed: boolean, choose: () => void) =>
      html`<button type="button" aria-pressed=${pressed ? 'true' : 'false'} @click=${choose}>
        <span>${label}</span><uui-icon name="icon-check" aria-hidden="true"></uui-icon>
      </button>`;
    return html`<uui-button class="more" compact popovertarget="jfb-preview-more" label=${this.#term('more')}>
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      <uui-popover-container
        id="jfb-preview-more"
        placement="bottom-end"
        @toggle=${(event: ToggleEvent) => {
          // Open, the menu takes the focus on its choice made, as the canvas's "..." does on its first item.
          if (event.newState === 'open')
            (event.target as HTMLElement).querySelector<HTMLElement>('button[aria-pressed="true"], button')?.focus();
        }}
      >
        <div class="menu">
          ${
            schemes
              ? html`<p class="menu-label" id="jfb-preview-scheme">${this.#term('scheme')}</p>
                  <div role="group" aria-labelledby="jfb-preview-scheme">
                    ${choice(this.#term('light'), !this._dark, () => this.#chooseScheme(false))}
                    ${choice(this.#term('dark'), this._dark, () => this.#chooseScheme(true))}
                  </div>`
              : nothing
          }
          ${
            languages.length > 1
              ? html`<p class="menu-label" id="jfb-preview-language">${this.localize.term('jfbDesigner_language')}</p>
                  <div role="group" aria-labelledby="jfb-preview-language">
                    ${languages.map((language) =>
                      choice(language.name, language.culture.toLowerCase() === culture, () =>
                        this.#chooseLanguage(language.culture),
                      ),
                    )}
                  </div>`
              : nothing
          }
        </div>
      </uui-popover-container>`;
  }

  #stage(): TemplateResult {
    const shown = this._shown;
    const width = `--frame-width: ${CANVAS_WIDTHS[this._width]}px`;
    switch (shown.kind) {
      case 'shown':
        // A new document gets a new frame: nothing of the page before stays, its history neither.
        return html`${keyed(
          `${shown.version}:${shown.culture}`,
          html`<iframe
            class="frame"
            style=${width}
            sandbox=${FRAME_SANDBOX}
            title=${this.#term('frame')}
            .srcdoc=${shown.document}
            @load=${() => {
              this.#host?.loaded();
              this.#host?.scheme(this._dark);
            }}
          ></iframe>`,
        )}`;
      case 'rulesInvalid':
        return html`<div class="frame state" style=${width} data-kind="rules">
          <p>${this.#term('rulesInvalid')}</p>
        </div>`;
      case 'failed':
        return html`<div class="frame state" style=${width} data-kind="failed">
          <p role="alert">${this.localize.term('jfbDesigner_previewFailed')}</p>
          <uui-button
            look="outline"
            label=${this.localize.term('jfbDesigner_tryAgain')}
            @click=${() => void this.#read()}
          ></uui-button>
        </div>`;
      default:
        return html`<div class="frame state" style=${width} data-kind="loading">
          <p role="status">${this.#term('loading')}</p>
        </div>`;
    }
  }

  override render() {
    return html`<umb-body-layout headline=${this.#term('headline', this.data?.name ?? '')}>
      <div class="room">
        ${this.#bar()}
        <div class="stage">${this.#stage()}</div>
      </div>
      <uui-button slot="actions" label=${this.localize.term('general_close')} @click=${this._rejectModal}></uui-button>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-preview-modal')) customElements.define('jfb-preview-modal', PreviewModalElement);

export { PreviewModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-preview-modal': PreviewModalElement;
  }
}
