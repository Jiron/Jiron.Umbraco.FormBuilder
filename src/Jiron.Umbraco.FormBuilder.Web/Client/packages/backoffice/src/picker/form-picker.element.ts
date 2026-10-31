// The form picker's property editor (ADR-0134): the picked forms as Umbraco's reference rows, each with Remove, and a
// dashed Choose that opens the sidebar. A single picker hides Choose once it holds a form, and a read-only property
// shows the rows only. The names come from the picker's endpoints within the data type's limits, so a form the
// picker cannot show is marked as not available, in the look Umbraco gives an item it cannot read.
import { UMB_PROPERTY_TYPE_BASED_PROPERTY_CONTEXT } from '@umbraco-cms/backoffice/content';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { css, html, nothing, repeat, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import {
  UmbPropertyEditorConfigCollection,
  type UmbPropertyEditorUiElement,
} from '@umbraco-cms/backoffice/property-editor';
import { UMB_VALIDATION_EMPTY_LOCALIZATION_KEY, UmbFormControlMixin } from '@umbraco-cms/backoffice/validation';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_PICKER_MODAL } from './form-picker-modal.token';
import { readKeys, writeKeys } from './picked-keys';
import { formPickerData, type PickerItem } from './picker-data';

/** Whether a setting names at least one folder or form. */
const names = (setting: unknown): boolean => Array.isArray(setting) && setting.length > 0;

export class FormPickerElement
  extends UmbFormControlMixin<string | undefined, typeof UmbLitElement, undefined>(UmbLitElement, undefined)
  implements UmbPropertyEditorUiElement
{
  static override properties = {
    readonly: { type: Boolean, reflect: true },
    mandatory: { type: Boolean },
    mandatoryMessage: { type: String },
    _found: { state: true },
    _failed: { state: true },
    _multiple: { state: true },
  };

  declare readonly: boolean;
  declare mandatory: boolean;
  declare mandatoryMessage: string;
  /** The picked forms the endpoint answered with, by key; null for one it left out. */
  declare private _found: ReadonlyMap<string, PickerItem | null>;
  declare private _failed: boolean;
  declare private _multiple: boolean;

  #keys: string[] = [];
  #config = new UmbPropertyEditorConfigCollection([]);
  #limited = false;
  #dataTypeKey: string | undefined = undefined;
  /** The keys and data type the picked forms were last looked up for. */
  #lookedUp: string | undefined = undefined;
  /** Counts the lookups, so that an answer to an older one is dropped. */
  #lookupId = 0;

  constructor() {
    super();
    this.readonly = false;
    this.mandatory = false;
    this.mandatoryMessage = UMB_VALIDATION_EMPTY_LOCALIZATION_KEY;
    this._found = new Map();
    this._failed = false;
    this._multiple = false;
    this.addValidator(
      'valueMissing',
      () => this.mandatoryMessage,
      () => !this.readonly && this.mandatory && this.#keys.length === 0,
    );
    this.consumeContext(UMB_PROPERTY_TYPE_BASED_PROPERTY_CONTEXT, (context) => {
      this.observe(
        context?.dataType,
        (dataType) => {
          this.#dataTypeKey = dataType?.unique ?? undefined;
          this.requestUpdate();
        },
        'jfbDataType',
      );
    });
  }

  override set value(value: string | undefined) {
    const old = super.value;
    super.value = value;
    this.#keys = readKeys(value);
    this.requestUpdate('value', old);
  }

  override get value(): string | undefined {
    return super.value;
  }

  get config(): UmbPropertyEditorConfigCollection {
    return this.#config;
  }

  set config(config: UmbPropertyEditorConfigCollection | undefined) {
    this.#config = config ?? new UmbPropertyEditorConfigCollection([]);
    this._multiple = config?.getValueByAlias<boolean>('multiple') === true;
    this.#limited = names(config?.getValueByAlias('allowedFolders')) || names(config?.getValueByAlias('allowedForms'));
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const lookup = `${this.#dataTypeKey ?? ''}|${this.#keys.join(',')}`;
    if (lookup !== this.#lookedUp) {
      if (this.#lookedUp?.split('|')[0] !== (this.#dataTypeKey ?? '')) this._found = new Map();
      this.#lookedUp = lookup;
      void this.#lookUp();
    }
  }

  /** Looks up the picked forms not known yet; with a new data type, every one again. */
  async #lookUp() {
    const id = ++this.#lookupId;
    const missing = this.#keys.filter((key) => !this._found.has(key));
    if (missing.length === 0) {
      this._failed = false;
      return;
    }
    const items = await formPickerData(this, this.#dataTypeKey).forms(missing);
    if (id !== this.#lookupId) return;
    this._failed = items === undefined;
    if (!items) return;
    const found = new Map(this._found);
    for (const key of missing) found.set(key, items.find((item) => item.key === key) ?? null);
    this._found = found;
  }

  #retry() {
    this.#lookedUp = undefined;
    this.requestUpdate();
  }

  #write(keys: string[]) {
    this.value = writeKeys(keys);
    this.dispatchEvent(new UmbChangeEvent());
  }

  async #choose() {
    const answer = await umbOpenModal(this, FORM_PICKER_MODAL, {
      data: {
        multiple: this._multiple,
        limited: this.#limited,
        ...(this.#dataTypeKey ? { dataTypeKey: this.#dataTypeKey } : {}),
      },
      value: { selection: [...this.#keys], items: this.#keys.flatMap((key) => this._found.get(key) ?? []) },
    }).catch(() => undefined);
    if (!answer) return;
    const found = new Map(this._found);
    for (const item of answer.items) found.set(item.key, item);
    this._found = found;
    this.#write(answer.selection);
  }

  async #remove(key: string) {
    const index = this.#keys.indexOf(key);
    this.#write(this.#keys.filter((other) => other !== key));
    await this.updateComplete;
    // The focus goes to the next row's Remove, or the one before, or Choose when no row is left.
    const removes = [...(this.shadowRoot?.querySelectorAll<HTMLElement>('.remove') ?? [])];
    const next = removes[Math.min(index, removes.length - 1)] ?? this.shadowRoot?.querySelector<HTMLElement>('#choose');
    next?.focus();
  }

  override render() {
    return html`${this.#renderRows()}${this.#renderFailure()}${this.#renderChoose()}`;
  }

  #renderRows() {
    if (this.#keys.length === 0) return nothing;
    return html`<uui-ref-list>
      ${repeat(
        this.#keys,
        (key) => key,
        (key) => this.#renderRow(key),
      )}
    </uui-ref-list>`;
  }

  #renderRow(key: string) {
    const item = this._found.get(key);
    if (item === undefined) {
      return this._failed
        ? nothing
        : html`<div class="placeholder" aria-hidden="true">
            <span class="block"></span>
            <span class="lines">
              <span class="line"><span class="bar"></span></span>
              <span class="line detail"><span class="bar short"></span></span>
            </span>
          </div>`;
    }
    const standalone = !this._multiple && this.#keys.length === 1;
    if (item === null) {
      return html`<uui-ref-node
        readonly
        ?standalone=${standalone}
        class="not-available"
        detail=${this.localize.term('jfbFormPicker_notAvailableDetail')}
      >
        <umb-icon slot="icon" name="icon-alert"></umb-icon>
        <span slot="name">${this.localize.term('jfbFormPicker_notAvailable')}</span>
        ${this.#renderRemove(key, this.localize.term('jfbFormPicker_removeNotAvailable'))}
      </uui-ref-node>`;
    }
    return html`<uui-ref-node readonly ?standalone=${standalone} name=${item.name} detail=${item.alias ?? ''}>
      <umb-icon slot="icon" name="icon-document"></umb-icon>
      ${this.#renderRemove(key, this.localize.term('jfbFormPicker_remove', item.name))}
    </uui-ref-node>`;
  }

  #renderRemove(key: string, label: string) {
    if (this.readonly) return nothing;
    return html`<uui-action-bar slot="actions">
      <uui-button class="remove" label=${label} @click=${() => this.#remove(key)}
        >${this.localize.term('general_remove')}</uui-button
      >
    </uui-action-bar>`;
  }

  #renderFailure() {
    if (!this._failed) return nothing;
    return html`<div class="failure" role="alert">
      <span>${this.localize.term('jfbFormPicker_pickedFailed')}</span>
      <uui-button
        look="secondary"
        compact
        label=${this.localize.term('jfbFormPicker_tryAgain')}
        @click=${this.#retry}
      ></uui-button>
    </div>`;
  }

  #renderChoose() {
    if (this.readonly) return nothing;
    if (!this._multiple && this.#keys.length > 0) return nothing;
    return html`<uui-button
      id="choose"
      look="placeholder"
      label=${this.localize.term('general_choose')}
      @click=${this.#choose}
    ></uui-button>`;
  }

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      #choose {
        display: block;
      }

      uui-ref-list:not(:empty) + #choose,
      .failure + #choose {
        margin-top: var(--uui-size-space-2);
      }

      /* The name and the icon carry the state; the reason stays in the detail's own colour, which keeps its contrast. */
      .not-available umb-icon,
      .not-available [slot='name'] {
        color: var(--jfb-bo-color-danger-text);
      }

      /* The shape of uui-ref-node: a pixel around a padded box with a line of name and a line of detail. */
      .placeholder {
        position: relative;
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: calc(var(--uui-size-space-3) + 1px);
      }

      .block,
      .bar {
        display: block;
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-surface-alt);
      }

      .block {
        flex: none;
        width: 1.25em;
        height: 1.25em;
      }

      .lines {
        display: flex;
        flex: 1;
        flex-direction: column;
      }

      .line {
        display: flex;
        align-items: center;
        height: 1.2em;
      }

      .line.detail {
        height: calc(1.2 * var(--uui-type-small-size));
      }

      .bar {
        width: 40%;
        height: 0.75em;
      }

      .bar.short {
        width: 25%;
      }

      .failure {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding-block: var(--uui-size-space-2);
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];
}

customElements.define('jfb-form-picker', FormPickerElement);

export { FormPickerElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-picker': FormPickerElement;
  }
}
