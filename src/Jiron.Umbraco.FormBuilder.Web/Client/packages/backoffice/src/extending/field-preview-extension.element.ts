// A site's own field type in the designer (ROADMAP 15.1, ADR-0233): what its `jfbFieldPreview` manifest draws in the
// place of the type's control, on the canvas and in the preview, given the field with its effective settings and the
// culture shown. It follows the registry, so a preview a site's bundle registers later still shows; without one the
// place stays empty and the field shows its label alone, as before.
import { LitElement, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { createExtensionElement } from '@umbraco-cms/backoffice/extension-api';
import type {
  JfbFieldPreviewElement,
  JfbPreviewField,
  ManifestJfbFieldPreview,
} from '../../public/types/jiron-form-builder';
import { EXTENSIONS_CHANGED, previewFor } from './extension-registry';

export class FieldPreviewExtensionElement extends LitElement {
  static override properties = {
    field: { attribute: false },
    culture: {},
  };

  declare field: JfbPreviewField | undefined;
  declare culture: string;

  #preview: JfbFieldPreviewElement | undefined;
  #made: ManifestJfbFieldPreview | undefined;
  #making = 0;

  constructor() {
    super();
    this.culture = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener(EXTENSIONS_CHANGED, this.#registered);
  }

  override disconnectedCallback() {
    window.removeEventListener(EXTENSIONS_CHANGED, this.#registered);
    super.disconnectedCallback();
  }

  // The preview lives in this element's light DOM, inside the field's wrapper, so the theme's rules reach it.
  protected override createRenderRoot() {
    return this;
  }

  protected override render() {
    return this.#preview ?? null;
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    const manifest = this.field ? previewFor(this.field.type) : undefined;
    if (manifest !== this.#made) void this.#make(manifest);
    else this.#hand();
  }

  readonly #registered = () => this.requestUpdate();

  async #make(manifest: ManifestJfbFieldPreview | undefined) {
    const making = ++this.#making;
    this.#made = manifest;
    const made = manifest ? await createExtensionElement(manifest) : undefined;
    if (making !== this.#making) return;
    this.#preview = made;
    this.#hand();
    this.requestUpdate();
  }

  #hand() {
    if (!this.#preview || !this.field) return;
    this.#preview.field = this.field;
    // The canvas and the preview name the language they show on the form's root.
    this.#preview.culture = this.culture || (this.closest('[lang]')?.getAttribute('lang') ?? '');
  }
}

if (!customElements.get('jfb-field-preview-extension'))
  customElements.define('jfb-field-preview-extension', FieldPreviewExtensionElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-field-preview-extension': FieldPreviewExtensionElement;
  }
}
