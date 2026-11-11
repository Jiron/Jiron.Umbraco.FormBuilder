// A site's own editor of a setting (ROADMAP 15.1, ADR-0233): the element its `jfbSettingEditor` manifest names, made
// once and kept while the manifest stays, given the setting, its value, its label, the culture and whether the value it
// gave last could not be kept. Its `change` is said on as `jfb-setting-change` with the value the editor holds, which
// the designer keeps as the setting's value and the server checks as it checks every setting.
import { LitElement, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { createExtensionElement } from '@umbraco-cms/backoffice/extension-api';
import type {
  JfbSetting,
  JfbSettingEditorElement,
  ManifestJfbSettingEditor,
} from '../../public/types/jiron-form-builder';

/** The event that carries a site's editor's new value. */
export const SETTING_CHANGE = 'jfb-setting-change';

export class SettingExtensionElement extends LitElement {
  static override properties = {
    manifest: { attribute: false },
    setting: { attribute: false },
    value: { attribute: false },
    label: {},
    culture: {},
    invalid: { type: Boolean },
  };

  declare manifest: ManifestJfbSettingEditor | undefined;
  declare setting: JfbSetting | undefined;
  declare value: unknown;
  declare label: string;
  declare culture: string;
  declare invalid: boolean;

  #editor: JfbSettingEditorElement | undefined;
  #made: ManifestJfbSettingEditor | undefined;
  #making = 0;

  constructor() {
    super();
    this.label = '';
    this.culture = '';
    this.invalid = false;
  }

  // The editor lives in this element's light DOM, so the backoffice's styles and Umbraco's contexts reach it.
  protected override createRenderRoot() {
    return this;
  }

  protected override render() {
    return this.#editor ?? null;
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    if (this.manifest !== this.#made) void this.#make(this.manifest);
    else this.#hand();
  }

  // Makes the manifest's element; one made for a manifest replaced meanwhile is dropped.
  async #make(manifest: ManifestJfbSettingEditor | undefined) {
    const making = ++this.#making;
    this.#made = manifest;
    const made = manifest ? await createExtensionElement(manifest) : undefined;
    if (making !== this.#making) return;
    this.#editor?.removeEventListener('change', this.#changed);
    this.#editor = made;
    this.#editor?.addEventListener('change', this.#changed);
    this.#hand();
    this.requestUpdate();
  }

  // What the editor gets: set again on every update, as the designer's values may change under it.
  #hand() {
    const editor = this.#editor;
    if (!editor || !this.setting) return;
    if (editor.value !== this.value) editor.value = this.value;
    editor.setting = this.setting;
    editor.label = this.label;
    editor.culture = this.culture;
    editor.invalid = this.invalid;
  }

  readonly #changed = (event: Event) => {
    if (event.target !== this.#editor) return;
    event.stopPropagation();
    this.dispatchEvent(new CustomEvent(SETTING_CHANGE, { detail: this.#editor?.value, bubbles: true, composed: true }));
  };
}

if (!customElements.get('jfb-setting-extension'))
  customElements.define('jfb-setting-extension', SettingExtensionElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-setting-extension': SettingExtensionElement;
  }
}
