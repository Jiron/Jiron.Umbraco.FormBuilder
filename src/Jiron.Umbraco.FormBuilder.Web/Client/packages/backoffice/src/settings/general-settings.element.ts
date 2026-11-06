// Settings > General (ROADMAP 10.3b, IA 8, ADR-0198): so far its History group, how long a form's History keeps draft
// saves, which every site shares; 12.4a adds the rest of the section and the site switch. One box with a sentence on what
// is kept for good, two numbers each with its help below it, and Save in the footer. The sidebar names the screen, so it
// has no title of its own (ADR-0021). A section the host locks shows the key and no Save; a value out of range is said
// under its box before anything is sent, and the server's refusal there too; a save made meanwhile is said, with the
// values now saved. A user who may not manage settings, coming by a link, is told so, without Try again.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { GeneralSettingsResponseModel } from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readGeneral, saveGeneral } from './general-settings';

type Field = 'draftDays' | 'draftCount';

const FIELDS: readonly Field[] = ['draftDays', 'draftCount'];

export class GeneralSettingsElement extends UmbLitElement {
  static override properties = {
    _settings: { state: true },
    _failed: { state: true },
    _values: { state: true },
    _invalid: { state: true },
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
        display: grid;
        gap: var(--uui-size-space-5);
        margin-block-start: var(--uui-size-space-5);
      }

      .with-unit {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .with-unit input {
        inline-size: 9rem;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
  ];

  declare private _settings: GeneralSettingsResponseModel | undefined;
  /** Why the settings couldn't be read: no answer, which Try again may fix, or a missing permission, which it won't. */
  declare private _failed: false | 'failed' | 'forbidden';
  /** What the boxes hold, as written. */
  declare private _values: Readonly<Record<Field, string>>;
  /** The boxes whose value can't be saved. */
  declare private _invalid: ReadonlySet<Field>;
  declare private _saving: boolean;
  /** Whether a save found the settings saved by someone else meanwhile. */
  declare private _conflict: boolean;

  constructor() {
    super();
    this._settings = undefined;
    this._failed = false;
    this._values = { draftDays: '', draftCount: '' };
    this._invalid = new Set();
    this._saving = false;
    this._conflict = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbGeneral_${key}`, ...args);

  // ---- Reading and saving -------------------------------------------------------------------------------------------

  async #read() {
    this._failed = false;
    const settings = await readGeneral(this);
    if (settings === undefined || settings === 'forbidden') {
      this._failed = settings ?? 'failed';
      return;
    }
    this.#show(settings);
  }

  #show(settings: GeneralSettingsResponseModel) {
    this._settings = settings;
    this._values = { draftDays: String(settings.history.draftDays), draftCount: String(settings.history.draftCount) };
    this._invalid = new Set();
  }

  /** The lowest and highest value a box takes, from the server's limits. */
  #limits(field: Field): readonly [number, number] {
    const limits = this._settings?.historyLimits;
    if (!limits) return [0, 0];
    return field === 'draftDays' ? [limits.minDraftDays, limits.maxDraftDays] : [0, limits.maxDraftCount];
  }

  #valid(field: Field): boolean {
    const text = this._values[field].trim();
    const [min, max] = this.#limits(field);
    return /^\d+$/.test(text) && Number(text) >= min && Number(text) <= max;
  }

  async #save() {
    const settings = this._settings;
    if (!settings || this._saving) return;
    this._conflict = false;
    const invalid = new Set(FIELDS.filter((field) => !this.#valid(field)));
    this._invalid = invalid;
    if (invalid.size > 0) {
      await this.updateComplete;
      this.shadowRoot?.querySelector<HTMLElement>(`#${[...invalid][0] ?? ''}`)?.focus();
      return;
    }
    this._saving = true;
    const answer = await saveGeneral(this, settings.version, {
      draftDays: Number(this._values.draftDays),
      draftCount: Number(this._values.draftCount),
    });
    this._saving = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (answer.kind) {
      case 'saved':
        this.#show(answer.settings);
        notifications?.peek('positive', { data: { message: this.#term('saved') } });
        return;
      case 'invalid': {
        const invalid = new Set(FIELDS.filter((field) => answer.fields.has(`history.${field}`)));
        this._invalid = invalid;
        // A field this screen doesn't show can't be marked: the save is said to have failed instead.
        if (invalid.size === 0) notifications?.peek('danger', { data: { message: this.#term('saveFailed') } });
        else {
          await this.updateComplete;
          this.shadowRoot?.querySelector<HTMLElement>(`#${[...invalid][0] ?? ''}`)?.focus();
        }
        return;
      }
      case 'conflict':
        // The values saved meanwhile replace the ones written: two numbers are quickly written again.
        this._conflict = true;
        await this.#read();
        return;
      case 'locked':
        await this.#read();
        return;
      default:
        notifications?.peek('danger', { data: { message: this.#term('saveFailed') } });
    }
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  /** The host's lock of a field, or of the whole section. */
  #lockOf(field: Field | '*') {
    return this._settings?.locks.find((lock) => lock.field === (field === '*' ? '*' : `history.${field}`));
  }

  #number(field: Field, label: string, unit: string, help: string): TemplateResult {
    const lock = this.#lockOf(field);
    const readOnly = this.#lockOf('*') !== undefined || lock !== undefined;
    const invalid = this._invalid.has(field);
    const [min, max] = this.#limits(field);
    // The unit first, so a screen reader says what the number counts, then the help.
    const described = [`${field}-unit`, `${field}-help`, lock ? `${field}-lock` : '', invalid ? `${field}-why` : '']
      .filter(Boolean)
      .join(' ');
    return html`<div class="item">
      <label class="label" for=${field}>${label}</label>
      <p class="help" id=${`${field}-help`}>${help}</p>
      <div class="with-unit">
        <input
          id=${field}
          type="number"
          inputmode="numeric"
          min=${min}
          max=${max}
          step="1"
          .value=${this._values[field]}
          ?readonly=${readOnly}
          aria-describedby=${described}
          aria-invalid=${invalid ? 'true' : 'false'}
          @input=${(event: Event) => {
            this._values = { ...this._values, [field]: (event.target as HTMLInputElement).value };
          }}
        />
        <span id=${`${field}-unit`}>${unit}</span>
      </div>
      ${lock ? html`<p class="help" id=${`${field}-lock`}>${this.#term('fieldLocked', lock.configurationKey)}</p>` : nothing}
      ${invalid ? html`<p class="why" id=${`${field}-why`}>${this.#term('range', min, max)}</p>` : nothing}
    </div>`;
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
          <span>${this.#term('failed')}</span>
          <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    if (!this._settings) return html`<uui-box>${skeletonLines(this.#term('loading'), [64, 48, 56])}</uui-box>`;
    const locked = this.#lockOf('*');
    // The screen's first heading: the sidebar names the screen, and a heading below its groups' is in order (WCAG 1.3.1).
    return html`<uui-box headline-variant="h2" headline=${this.#term('history')}>
      <p class="lead">${this.#term('historyLead')}</p>
      ${locked ? html`<p class="locked" role="note">${this.#term('locked', locked.configurationKey)}</p>` : nothing}
      <div class="fields">
        ${this.#number('draftDays', this.#term('draftDays'), this.#term('draftDaysUnit'), this.#term('draftDaysHelp'))}
        ${this.#number('draftCount', this.#term('draftCount'), this.#term('draftCountUnit'), this.#term('draftCountHelp'))}
      </div>
    </uui-box>`;
  }

  override render() {
    const savable = this._settings !== undefined && !this._failed && this.#lockOf('*') === undefined;
    return html`<umb-body-layout>
      <div class="page">
        ${this._conflict ? html`<p class="alert" role="alert">${this.#term('conflict')}</p>` : nothing} ${this.#body()}
      </div>
      ${
        savable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#term('save')}
              .state=${this._saving ? 'waiting' : undefined}
              @click=${() => void this.#save()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-general-settings')) customElements.define('jfb-general-settings', GeneralSettingsElement);

export { GeneralSettingsElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-general-settings': GeneralSettingsElement;
  }
}
