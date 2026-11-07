// Privacy > Data collection (ROADMAP 11.5, IA 9, ADR-0211): so far its Insights box, the site's switch for counting what
// visitors do with the forms, which every site shares; the rest of the screen's settings come with their features. One
// box with a sentence on what is counted and what never is, the switch with its help below it, and Save in the footer.
// The sidebar names the screen, so it has no title of its own (ADR-0021). A section the host locks shows the key and no
// Save; a save made meanwhile is said, with the value now saved. A user who may not manage settings is told so.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { PrivacySettingsResponseModel } from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { readPrivacy, savePrivacy } from '../insights/insight-api';
import { loadInsightWords } from '../insights/insight-words';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';

export class PrivacySettingsElement extends UmbLitElement {
  static override properties = {
    _settings: { state: true },
    _failed: { state: true },
    _insights: { state: true },
    _saving: { state: true },
    _conflict: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    controlStyles,
    css`
      :host {
        display: block;
        block-size: 100%;
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-5);
        max-inline-size: 760px;
        margin-inline: auto;
      }

      .lead,
      .locked,
      .alert {
        margin: 0;
      }

      .locked {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .fields {
        margin-block-start: var(--uui-size-space-5);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
  ];

  declare private _settings: PrivacySettingsResponseModel | undefined;
  /** Why the settings couldn't be read: no answer, which Try again may fix, or a missing permission, which it won't. */
  declare private _failed: false | 'failed' | 'forbidden';
  /** What the switch holds, not yet saved. */
  declare private _insights: boolean;
  declare private _saving: boolean;
  /** Whether a save found the settings saved by someone else meanwhile. */
  declare private _conflict: boolean;

  constructor() {
    super();
    this._settings = undefined;
    this._failed = false;
    this._insights = true;
    this._saving = false;
    this._conflict = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInsights_${key}`, ...args);
  #general = (key: string, ...args: unknown[]) => this.localize.term(`jfbGeneral_${key}`, ...args);

  async #read() {
    this._failed = false;
    const [words, settings] = await Promise.all([
      loadInsightWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readPrivacy(this),
    ]);
    if (!words || settings === undefined || settings === 'forbidden') {
      this._failed = settings === 'forbidden' ? 'forbidden' : 'failed';
      return;
    }
    this._settings = settings;
    this._insights = settings.insights;
  }

  async #save() {
    const settings = this._settings;
    if (!settings || this._saving) return;
    this._conflict = false;
    this._saving = true;
    const answer = await savePrivacy(this, settings.version, this._insights);
    this._saving = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (answer.kind) {
      case 'saved':
        this._settings = answer.settings;
        this._insights = answer.settings.insights;
        notifications?.peek('positive', { data: { message: this.#general('saved') } });
        return;
      case 'conflict':
        this._conflict = true;
        await this.#read();
        return;
      case 'locked':
        await this.#read();
        return;
      default:
        notifications?.peek('danger', { data: { message: this.#general('saveFailed') } });
    }
  }

  #lockOf(field: string) {
    return this._settings?.locks.find((lock) => lock.field === field);
  }

  #body(): TemplateResult {
    if (this._failed === 'forbidden')
      return html`<uui-box>
        <p class="lead" role="alert">
          ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_manageSettings'))}
        </p>
      </uui-box>`;
    if (this._failed)
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#general('failed')}</span>
          <uui-button look="outline" label=${this.#general('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (!this._settings) return html`<uui-box>${skeletonLines(this.#general('loading'), [64, 48])}</uui-box>`;
    const locked = this.#lockOf('*') ?? this.#lockOf('insights');
    return html`<uui-box headline-variant="h2" headline=${this.#term('dataCollection')}>
      <p class="lead">${this.#term('dataCollectionLead')}</p>
      ${locked ? html`<p class="locked" role="note">${this.#general('locked', locked.configurationKey)}</p>` : nothing}
      <div class="fields">
        <div class="item">
          <div class="item-switch">
            <input
              type="checkbox"
              role="switch"
              id="insights"
              .checked=${this._insights}
              ?disabled=${locked !== undefined}
              aria-describedby="insights-help"
              @change=${(event: Event) => (this._insights = (event.target as HTMLInputElement).checked)}
            />
            <label class="label" for="insights">${this.#term('dataCollectionSwitch')}</label>
          </div>
          <p class="help" id="insights-help">${this.#term('dataCollectionHelp')}</p>
        </div>
      </div>
    </uui-box>`;
  }

  override render() {
    const savable =
      this._settings !== undefined &&
      !this._failed &&
      this.#lockOf('*') === undefined &&
      this.#lockOf('insights') === undefined;
    return html`<umb-body-layout>
      <div class="page">
        ${this._conflict ? html`<p class="alert" role="alert">${this.#general('conflict')}</p>` : nothing}
        ${this.#body()}
      </div>
      ${
        savable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#general('save')}
              .state=${this._saving ? 'waiting' : undefined}
              @click=${() => void this.#save()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-privacy-settings')) customElements.define('jfb-privacy-settings', PrivacySettingsElement);

export { PrivacySettingsElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-privacy-settings': PrivacySettingsElement;
  }
}
