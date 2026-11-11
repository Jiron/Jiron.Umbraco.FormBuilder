// Settings > AI (ADR-0018), for who may manage settings: the Umbraco.AI profile the AI features use, chosen with
// Umbraco.AI's own profile picker, chat profiles only, or none for Umbraco.AI's default chat profile; and, once, what
// the features use now or why they cannot work. Save keeps the choice, which the audit log records.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { aiGetSettings, aiSaveSettings, aiStatus, type AiStatusResponseModel } from '../api';
import { problemKey, refusalOf, refusalText, type Refusal } from '../problems';

export class AISettingsScreenElement extends UmbLitElement {
  static override properties = {
    _profile: { state: true },
    _status: { state: true },
    _saving: { state: true },
    _problem: { state: true },
    _read: { state: true },
  };

  /** The chosen profile's id, or '' for the default. */
  declare _profile: string;
  declare _status: AiStatusResponseModel | undefined;
  declare _saving: boolean;
  declare _problem: Refusal | null;
  declare _read: boolean;

  constructor() {
    super();
    this._profile = '';
    this._status = undefined;
    this._saving = false;
    this._problem = null;
    this._read = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  async #read() {
    const [{ data: settings, error }, { data: status }] = await Promise.all([
      tryExecute(this, aiGetSettings(), { disableNotifications: true }),
      tryExecute(this, aiStatus(), { disableNotifications: true }),
    ]);
    if (!settings) this._problem = refusalOf(error);
    this._profile = settings?.profileId ?? '';
    this.#saved = this._profile;
    this._status = status;
    this._read = true;
  }

  async #save() {
    this._saving = true;
    this._problem = null;
    const { data, error } = await tryExecute(
      this,
      aiSaveSettings({ body: { profileId: this._profile === '' ? null : this._profile } }),
      { disableNotifications: true },
    );
    this._saving = false;
    if (!data) {
      this._problem = refusalOf(error);
      return;
    }

    this._profile = data.profileId ?? '';
    this.#saved = this._profile;
    this._status = (await tryExecute(this, aiStatus(), { disableNotifications: true })).data;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek('positive', { data: { message: this.localize.term('jfbAI_settingsSaved') } });
  }

  override render() {
    return html`<umb-body-layout>
      <div class="page">
        <uui-box headline-variant="h2" headline=${this.localize.term('jfbAI_settingsBox')}>
          <p class="lead" id="lead">${this.localize.term('jfbAI_settingsLead')}</p>
          ${
            this._read
              ? html`<uai-profile-picker
                  capability="Chat"
                  aria-describedby="lead"
                  label=${this.localize.term('jfbAI_settingsProfile')}
                  .value=${this._profile}
                  @change=${(event: Event) => {
                    const value = (event.target as unknown as { value?: unknown }).value;
                    this._profile = typeof value === 'string' ? value : '';
                  }}
                ></uai-profile-picker>`
              : html`<uui-loader-bar></uui-loader-bar>`
          }
          <p class="status" role="status">${this.#statusText()}</p>
          <p class="problem" role="alert">
            ${this._problem ? refusalText((key) => this.localize.term(key), this._problem) : nothing}
          </p>
        </uui-box>
      </div>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term('jfbAI_settingsSave')}
        ?disabled=${!this._read}
        .state=${this._saving ? 'waiting' : undefined}
        @click=${() => void this.#save()}
      ></uui-button>
    </umb-body-layout>`;
  }

  /** The profile the settings hold as saved, which the status speaks of; '' for Umbraco.AI's default. */
  #saved = '';

  // Said once: a chosen profile shows in the picker, so the line speaks only of the default in use and of a problem.
  #statusText() {
    const status = this._status;
    if (!status || (status.ready && this.#saved !== '')) return nothing;
    if (status.ready && status.profileName) return this.localize.term('jfbAI_settingsReady', status.profileName);
    return this.localize.term(problemKey(status.problem ?? 'failed'));
  }

  static override styles = css`
    .page {
      display: grid;
      gap: var(--uui-size-space-5);
      max-inline-size: 760px;
    }

    uui-box > * + * {
      margin-block-start: var(--uui-size-space-4);
    }

    p {
      margin: 0;
    }

    .lead,
    .status {
      color: var(--uui-color-text-alt);
    }

    .problem {
      color: var(--uui-color-danger-standalone);
    }

    .status:empty,
    .problem:empty {
      display: none;
    }
  `;
}

if (!customElements.get('jfb-ai-settings-screen'))
  customElements.define('jfb-ai-settings-screen', AISettingsScreenElement);

export { AISettingsScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-ai-settings-screen': AISettingsScreenElement;
  }
}
