// The allowed folders or forms of a form picker's settings (ADR-0135): the chosen ones as the property shows its forms,
// Umbraco's reference rows with Remove and a dashed Choose, which opens the picker's own sidebar, for folders in its
// folder mode. The value is the keys in the order chosen, none written as no value. No data type limits what the
// sidebar offers, and a folder or form the user cannot see any more is marked as not available. Past the most the
// picker's queries carry it says so here, as Umbraco answers the save that the server refuses with a general message.
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import {
  UmbPropertyEditorConfigCollection,
  type UmbPropertyEditorUiElement,
} from '@umbraco-cms/backoffice/property-editor';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_PICKER_MODAL } from './form-picker-modal.token';
import {
  focusAfterRemove,
  FOLDER_ROWS,
  FORM_ROWS,
  PickedLookup,
  pickedRowsStyles,
  renderChoose,
  renderPickedFailure,
  renderPickedRows,
} from './picked-rows';
import { normalKey } from './picked-keys';
import { formPickerData } from './picker-data';

/** The most folders or forms a setting holds (`SqlArgs.MaxListLength`), past which the server refuses the settings. */
export const MAX_ALLOWED = 1000;

/** The keys a setting holds, each once and in order, in the hyphenated format; anything else counts as none. */
function keysOf(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const keys = value.filter((key): key is string => typeof key === 'string' && key.trim() !== '');
  return [...new Set(keys.map(normalKey))];
}

export class FormPickerAllowedElement extends UmbLitElement implements UmbPropertyEditorUiElement {
  static override properties = {
    value: { attribute: false },
    readonly: { type: Boolean, reflect: true },
    _pick: { state: true },
  };

  declare value: string[] | undefined;
  declare readonly: boolean;
  /** What the setting holds: folders or forms. */
  declare private _pick: 'folders' | 'forms';

  #config = new UmbPropertyEditorConfigCollection([]);
  #lookup = new PickedLookup(this);

  constructor() {
    super();
    this.value = undefined;
    this.readonly = false;
    this._pick = 'forms';
  }

  get config(): UmbPropertyEditorConfigCollection {
    return this.#config;
  }

  set config(config: UmbPropertyEditorConfigCollection | undefined) {
    this.#config = config ?? new UmbPropertyEditorConfigCollection([]);
    this._pick = config?.getValueByAlias<string>('pick') === 'folders' ? 'folders' : 'forms';
  }

  get #keys(): string[] {
    return keysOf(this.value);
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const pick = this._pick;
    this.#lookup.sync(pick, this.#keys, (keys) =>
      pick === 'folders' ? formPickerData(this, undefined).folders(keys) : formPickerData(this, undefined).forms(keys),
    );
  }

  #write(keys: string[]) {
    this.value = keys.length > 0 ? keys : undefined;
    this.dispatchEvent(new UmbChangeEvent());
  }

  async #choose() {
    const keys = this.#keys;
    const answer = await umbOpenModal(this, FORM_PICKER_MODAL, {
      data: { pick: this._pick, multiple: true, limited: false },
      value: { selection: keys, items: keys.flatMap((key) => this.#lookup.found.get(key) ?? []) },
    }).catch(() => undefined);
    if (!answer) return;
    this.#lookup.remember(answer.items);
    this.#write(answer.selection);
  }

  #remove(key: string) {
    const keys = this.#keys;
    this.#write(keys.filter((other) => other !== key));
    void focusAfterRemove(this, keys.indexOf(key));
  }

  override render() {
    const folders = this._pick === 'folders';
    return html`${renderPickedRows({
      host: this,
      keys: this.#keys,
      lookup: this.#lookup,
      kind: folders ? FOLDER_ROWS : FORM_ROWS,
      standalone: false,
      readonly: this.readonly,
      remove: (key) => this.#remove(key),
    })}${renderPickedFailure(
      this,
      this.#lookup,
      folders ? 'jfbFormPicker_allowedFoldersFailed' : 'jfbFormPicker_allowedFormsFailed',
    )}${this.#renderTooMany()}${this.readonly ? nothing : renderChoose(this, () => void this.#choose())}`;
  }

  #renderTooMany() {
    if ((this.value?.length ?? 0) <= MAX_ALLOWED) return nothing;
    return html`<div class="failure" role="alert"><span>${this.localize.term('jfbPicker_tooMany')}</span></div>`;
  }

  static override styles = [jfbBackofficeTokens, pickedRowsStyles];
}

customElements.define('jfb-form-picker-allowed', FormPickerAllowedElement);

export { FormPickerAllowedElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-picker-allowed': FormPickerAllowedElement;
  }
}
