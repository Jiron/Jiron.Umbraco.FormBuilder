// An email action's live preview in its sidebar (ROADMAP 10.2b, ADR-0189): who the mail comes from and goes to, its
// subject, and the mail itself as the team gets it for the sample entry, in a frame that runs nothing and is as tall as
// the mail, with a Light/Dark switch for the colour scheme it shows in; a plain-text mail shows its text. The last
// answer stays while the next is awaited, so a change never flashes.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { NO_PREVIEW, type PreviewState } from '../preview/draft-previewer';
import type { EmailPreviewResponseModel } from '../../api';
import { mailDocument, type EmailPreview, type Scheme } from './email-preview';

// A press on the mail, with any button of the pointer: a link of the preview leads nowhere.
const goesNowhere = (event: Event) => event.preventDefault();

// The least height of the frame while its mail is drawn.
const LEAST = 120;

export class EmailPreviewElement extends UmbLitElement {
  static override properties = {
    state: { attribute: false },
    culture: { type: String },
    view: { type: String },
    retry: { attribute: false },
    _scheme: { state: true },
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

      .head {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      .help,
      .said {
        margin: 0;
      }

      .help {
        color: var(--uui-color-text-alt);
      }

      .schemes {
        display: inline-flex;
        border: 1px solid var(--uui-color-border-emphasis);
        border-radius: var(--uui-border-radius);
      }

      .schemes button {
        min-block-size: 32px;
        padding: 0 var(--uui-size-space-4);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .schemes button + button {
        border-inline-start: 1px solid var(--uui-color-border-emphasis);
      }

      .schemes button[aria-pressed='true'] {
        background: var(--uui-color-selected);
        color: var(--uui-color-selected-contrast);
        font-weight: 700;
      }

      .schemes button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .meta {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: var(--uui-size-space-1) var(--uui-size-space-4);
        margin: 0;
        font-size: var(--uui-type-small-size);
      }

      .meta dt {
        color: var(--uui-color-text-alt);
      }

      .meta dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      .meta .subject {
        font-weight: 700;
      }

      .mail {
        display: block;
        box-sizing: border-box;
        inline-size: 100%;
        block-size: ${LEAST}px;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: transparent;
      }

      /* A plain-text mail on a white sheet, as a mail client shows it. */
      .text {
        box-sizing: border-box;
        min-block-size: ${LEAST}px;
        margin: 0;
        padding: var(--uui-size-space-5);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: #fff;
        color: #1d2433;
        font: inherit;
        white-space: pre-wrap;
        overflow-wrap: anywhere;
      }

      .failed {
        display: grid;
        gap: var(--uui-size-space-3);
        justify-items: start;
      }
    `,
  ];

  declare state: PreviewState<EmailPreview>;
  declare culture: string;
  /** The view the action names, which a missing view's line says. */
  declare view: string;
  /** Asks again after an ask that got no answer. */
  declare retry: () => void;
  declare private _scheme: Scheme;

  #observer: ResizeObserver | undefined;
  /** The frame's document for the mail and scheme last drawn, so a render that keeps them parses nothing. */
  #document: { readonly html: string; readonly scheme: Scheme; readonly text: string } | null = null;

  constructor() {
    super();
    this.state = NO_PREVIEW;
    this.culture = '';
    this.view = '';
    this.retry = () => undefined;
    this._scheme = 'light';
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#observer?.disconnect();
    this.#observer = undefined;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActions_${key}`, ...args);

  #documentOf(html: string): string {
    const kept = this.#document;
    if (kept?.html === html && kept.scheme === this._scheme) return kept.text;
    const text = mailDocument(html, this._scheme);
    this.#document = { html, scheme: this._scheme, text };
    return text;
  }

  /** The frame as tall as its mail, at its width now: measured from the least height, so it grows and shrinks. */
  #fit(frame: HTMLIFrameElement) {
    const root = frame.contentDocument?.documentElement;
    if (!root) return;
    frame.style.blockSize = `${LEAST}px`;
    frame.style.blockSize = `${Math.max(root.scrollHeight, LEAST)}px`;
  }

  #loaded(frame: HTMLIFrameElement) {
    const mail = frame.contentDocument;
    mail?.addEventListener('click', goesNowhere);
    mail?.addEventListener('auxclick', goesNowhere);
    this.#fit(frame);
    // A narrower or wider sidebar wraps the mail anew.
    this.#observer?.disconnect();
    this.#observer = new ResizeObserver(() => this.#fit(frame));
    this.#observer.observe(frame);
  }

  #meta(mail: EmailPreviewResponseModel) {
    const line = (label: string, text: string, kind?: string) =>
      html`<dt>${label}</dt>
        <dd class=${kind ?? nothing}>${text}</dd>`;
    return html`<dl class="meta">
      ${line(this.#term('previewFrom'), mail.from || this.#term('previewNoSender'))}
      ${line(
        this.#term('previewTo'),
        mail.to.length > 0
          ? mail.to.join(', ')
          : mail.visitor
            ? this.#term('previewOwnMail', mail.visitor)
            : this.#term('previewNoRecipient'),
      )}
      ${mail.to.length > 0 && mail.visitor ? line(this.#term('previewVisitor'), this.#term('previewOwnMail', mail.visitor)) : nothing}
      ${mail.cc.length > 0 ? line(this.#term('previewCc'), mail.cc.join(', ')) : nothing}
      ${mail.bcc.length > 0 ? line(this.#term('previewBcc'), mail.bcc.join(', ')) : nothing}
      ${mail.replyTo ? line(this.#term('previewReplyTo'), mail.replyTo) : nothing}
      <dt>${this.#term('previewSubject')}</dt>
      <dd class="subject" lang=${this.culture || nothing} dir=${mail.direction}>${mail.subject}</dd>
    </dl>`;
  }

  #schemes() {
    const choose = (scheme: Scheme) =>
      html`<button
        type="button"
        aria-pressed=${this._scheme === scheme ? 'true' : 'false'}
        @click=${() => (this._scheme = scheme)}
      >
        ${this.#term(`scheme_${scheme}`)}
      </button>`;
    return html`<div class="schemes" role="group" aria-label=${this.#term('scheme')}>
      ${choose('light')}${choose('dark')}
    </div>`;
  }

  #shown() {
    const { status, preview } = this.state;
    // Said as it comes, as the designer's other failures are.
    if (status === 'failed')
      return html`<div class="failed">
        <p class="said" role="alert">${this.#term('previewFailed')}</p>
        <uui-button look="outline" label=${this.#term('previewRetry')} @click=${() => this.retry()}></uui-button>
      </div>`;
    if (!preview) return html`<div class="text" aria-hidden="true"></div>`;
    if ('unshown' in preview)
      return html`<p class="said">
        ${preview.unshown === 'viewMissing' ? this.#term('previewViewMissing', this.view) : this.#term('previewSettingsInvalid')}
      </p>`;
    return html`${this.#meta(preview)}
    ${
      preview.html
        ? html`<iframe
            class="mail"
            sandbox="allow-same-origin"
            title=${this.#term('previewFrame')}
            style=${`color-scheme: ${this._scheme}`}
            .srcdoc=${this.#documentOf(preview.html)}
            @load=${(event: Event) => this.#loaded(event.target as HTMLIFrameElement)}
          ></iframe>`
        : html`<p class="text" lang=${this.culture || nothing} dir=${preview.direction}>${preview.text}</p>`
    }`;
  }

  override render() {
    const { status, preview } = this.state;
    const busy = status === 'idle' || status === 'asking';
    const mail = preview && !('unshown' in preview) ? preview : null;
    const mailHtml = mail?.html;
    return html`<section class="preview" aria-labelledby="preview-heading">
      <div class="head">
        <h4 id="preview-heading">${this.#term('preview')}</h4>
        ${mailHtml ? this.#schemes() : nothing}
      </div>
      <p class="help">
        ${this.#term(mail && mail.to.length === 0 && mail.visitor ? 'previewHelpVisitor' : 'previewHelp')}
      </p>
      <div class="shown" aria-busy=${busy ? 'true' : 'false'}>${this.#shown()}</div>
    </section>`;
  }
}

if (!customElements.get('jfb-email-preview')) customElements.define('jfb-email-preview', EmailPreviewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-email-preview': EmailPreviewElement;
  }
}
