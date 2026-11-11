// "Draft a form with AI" (docs/ux/information-architecture.md, section 4; ADR-0018): a description of the form, or the
// pasted text of a questionnaire, and **Draft form**. The AI's draft becomes a new form, never published; when the AI
// is not set up, the sidebar says so instead of offering a box that cannot work. A refusal is said above the buttons.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { aiDraftCreate, aiStatus } from '../api';
import { problemKey, refusalOf, refusalText, type Refusal } from '../problems';
import type { DraftModalData, DraftModalValue } from './draft.modal.token';

/** The longest text the server takes. */
export const MAX_TEXT = 20_000;

export class DraftModalElement extends UmbModalBaseElement<DraftModalData, DraftModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _text: { state: true },
    _busy: { state: true },
    _problem: { state: true },
    _setup: { state: true },
  };

  declare _text: string;
  declare _busy: boolean;
  declare _problem: Refusal | null;
  /** Why the AI cannot draft, from its status; null while it can, undefined before it is read. */
  declare _setup: string | null | undefined;

  constructor() {
    super();
    this._text = '';
    this._busy = false;
    this._problem = null;
    this._setup = undefined;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#readStatus();
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // The sidebar takes the focus itself as it opens, so the text box takes it in the frame after.
    requestAnimationFrame(() => this.shadowRoot?.querySelector<HTMLTextAreaElement>('textarea')?.focus());
  }

  async #readStatus() {
    const { data } = await tryExecute(this, aiStatus(), { disableNotifications: true });
    this._setup = data ? (data.ready ? null : (data.problem ?? 'failed')) : 'failed';
  }

  async #draft() {
    const text = this._text.trim();
    if (text.length === 0 || this._busy) return;
    this._busy = true;
    this._problem = null;
    const { data, error } = await tryExecute(
      this,
      aiDraftCreate({ body: { text, folderKey: this.data?.folderKey ?? null } }),
      { disableNotifications: true },
    );
    this._busy = false;
    if (!data) {
      this._problem = refusalOf(error);
      return;
    }

    if (data.left > 0) {
      const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
      notifications?.peek('warning', { data: { message: this.localize.term('jfbAI_draftLeft', data.left) } });
    }

    this.value = { formKey: data.key };
    this._submitModal();
  }

  override render() {
    const ready = this._setup === null;
    return html`<umb-body-layout headline=${this.localize.term('jfbAI_draftHeadline')}>
      <div class="body">
        ${ready || this._setup === undefined ? this.#form() : html`<p class="setup">${this.localize.term(problemKey(this._setup ?? 'failed'))}</p>`}
        <p class="problem" role="alert">
          ${this._problem ? refusalText((key) => this.localize.term(key), this._problem) : nothing}
        </p>
        <p class="busy" role="status">${this._busy ? this.localize.term('jfbAI_drafting') : nothing}</p>
      </div>
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('jfbAI_draftSubmit')}
          ?disabled=${!ready || this._text.trim().length === 0}
          .state=${this._busy ? 'waiting' : undefined}
          @click=${() => void this.#draft()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }

  #form() {
    return html`<label for="text">${this.localize.term('jfbAI_draftLabel')}</label>
      <p class="help" id="text-help">${this.localize.term('jfbAI_draftHelp')}</p>
      <textarea
        id="text"
        rows="14"
        maxlength=${MAX_TEXT}
        aria-describedby="text-help"
        ?readonly=${this._busy}
        .value=${this._text}
        @input=${(event: Event) => (this._text = (event.target as HTMLTextAreaElement).value)}
      ></textarea>`;
  }

  static override styles = css`
    .body {
      display: grid;
      gap: var(--uui-size-space-3);
    }

    label {
      font-weight: 700;
    }

    p {
      margin: 0;
    }

    .help {
      color: var(--uui-color-text-alt);
    }

    textarea {
      box-sizing: border-box;
      inline-size: 100%;
      min-block-size: 16rem;
      padding: var(--uui-size-space-3);
      font: inherit;
      color: inherit;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border-standalone);
      border-radius: var(--uui-border-radius);
      resize: vertical;
    }

    textarea:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }

    .problem:empty,
    .busy:empty {
      display: none;
    }

    .problem {
      color: var(--uui-color-danger-standalone);
    }
  `;
}

if (!customElements.get('jfb-ai-draft-modal')) customElements.define('jfb-ai-draft-modal', DraftModalElement);

export { DraftModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-ai-draft-modal': DraftModalElement;
  }
}
