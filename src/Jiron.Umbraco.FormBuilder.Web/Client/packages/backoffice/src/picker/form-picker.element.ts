// The form picker's property editor (ADR-0134): the picked forms as Umbraco's reference rows, each with Remove, and a
// dashed Choose that opens the sidebar. A single picker hides Choose once it holds a form, and a read-only property
// shows the rows only. The names come from the picker's endpoints within the data type's limits, so a form the
// picker cannot show is marked as not available, in the look Umbraco gives an item it cannot read.
import { UMB_PROPERTY_TYPE_BASED_PROPERTY_CONTEXT } from '@umbraco-cms/backoffice/content';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import {
  UmbPropertyEditorConfigCollection,
  type UmbPropertyEditorUiElement,
} from '@umbraco-cms/backoffice/property-editor';
import { UMB_VALIDATION_EMPTY_LOCALIZATION_KEY, UmbFormControlMixin } from '@umbraco-cms/backoffice/validation';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_PICKER_MODAL } from './form-picker-modal.token';
import {
  focusAfterRemove,
  FORM_ROWS,
  PickedLookup,
  pickedRowsStyles,
  renderChoose,
  renderPickedFailure,
  renderPickedRows,
} from './picked-rows';
import { readKeys, writeKeys } from './picked-keys';
import { formPickerData } from './picker-data';

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
    _multiple: { state: true },
  };

  declare readonly: boolean;
  declare mandatory: boolean;
  declare mandatoryMessage: string;
  declare private _multiple: boolean;

  #keys: string[] = [];
  #config = new UmbPropertyEditorConfigCollection([]);
  #limited = false;
  #dataTypeKey: string | undefined = undefined;
  #lookup = new PickedLookup(this);

  constructor() {
    super();
    this.readonly = false;
    this.mandatory = false;
    this.mandatoryMessage = UMB_VALIDATION_EMPTY_LOCALIZATION_KEY;
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
    // A new data type may limit the forms differently, so every name is looked up again within it.
    const dataTypeKey = this.#dataTypeKey;
    this.#lookup.sync(dataTypeKey ?? '', this.#keys, (keys) => formPickerData(this, dataTypeKey).forms(keys));
  }

  #write(keys: string[]) {
    this.value = writeKeys(keys);
    this.dispatchEvent(new UmbChangeEvent());
  }

  async #choose() {
    const answer = await umbOpenModal(this, FORM_PICKER_MODAL, {
      data: {
        pick: 'forms',
        multiple: this._multiple,
        limited: this.#limited,
        ...(this.#dataTypeKey ? { dataTypeKey: this.#dataTypeKey } : {}),
      },
      value: { selection: [...this.#keys], items: this.#keys.flatMap((key) => this.#lookup.found.get(key) ?? []) },
    }).catch(() => undefined);
    if (!answer) return;
    this.#lookup.remember(answer.items);
    this.#write(answer.selection);
  }

  #remove(key: string) {
    const index = this.#keys.indexOf(key);
    this.#write(this.#keys.filter((other) => other !== key));
    void focusAfterRemove(this, index);
  }

  override render() {
    return html`${renderPickedRows({
      host: this,
      keys: this.#keys,
      lookup: this.#lookup,
      kind: FORM_ROWS,
      standalone: !this._multiple && this.#keys.length === 1,
      readonly: this.readonly,
      remove: (key) => this.#remove(key),
    })}${renderPickedFailure(this, this.#lookup, 'jfbFormPicker_pickedFailed')}${this.#renderChoose()}`;
  }

  #renderChoose() {
    if (this.readonly) return nothing;
    if (!this._multiple && this.#keys.length > 0) return nothing;
    return renderChoose(this, () => void this.#choose());
  }

  static override styles = [jfbBackofficeTokens, pickedRowsStyles];
}

customElements.define('jfb-form-picker', FormPickerElement);

export { FormPickerElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-picker': FormPickerElement;
  }
}
