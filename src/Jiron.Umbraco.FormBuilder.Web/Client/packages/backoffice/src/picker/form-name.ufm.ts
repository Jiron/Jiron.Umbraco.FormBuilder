// The block label's form name (ADR-0134): `{jfbFormName: <property alias>}` in a block's label shows the names of
// the forms its property picks, joined as the backoffice's language joins a list, "(not available)" for one the
// picker cannot show, and nothing for no value or when the names cannot be read. The names are looked up without a
// data type's limits, since a label names what the block holds.
import {
  UMB_UFM_RENDER_CONTEXT,
  UmbUfmComponentBase,
  UmbUfmElementBase,
  type UfmToken,
} from '@umbraco-cms/backoffice/ufm';
import { readKeys } from './picked-keys';
import { formPickerData } from './picker-data';

/** Renders `{jfbFormName: alias}` as the element below. */
export class FormNameUfmComponent extends UmbUfmComponentBase {
  // Umbraco hands render to its markdown parser without the instance (17.7.1, research note section 21), so this
  // method uses super, which is bound when the class is defined, never this.
  render(token: UfmToken) {
    return token.text ? `<ufm-jfb-form-name ${super.getAttributes(token.text) ?? ''}></ufm-jfb-form-name>` : undefined;
  }
}

/** The names of the picked forms, as text. Its tag starts with ufm-, as the label's sanitising keeps no other. */
export class FormNameUfmElement extends UmbUfmElementBase {
  static override properties = { alias: { type: String } };

  declare alias: string | undefined;

  /** Counts the lookups, so that an answer to an older one is dropped. */
  #lookupId = 0;

  constructor() {
    super();
    this.consumeContext(UMB_UFM_RENDER_CONTEXT, (context) => {
      this.observe(context?.value, (value) => void this.#name(value), 'jfbFormName');
    });
  }

  async #name(content: unknown) {
    const id = ++this.#lookupId;
    const picked =
      this.alias && content !== null && typeof content === 'object'
        ? (content as Record<string, unknown>)[this.alias]
        : content;
    const keys = readKeys(picked);
    if (keys.length === 0) {
      this.value = '';
      return;
    }
    const items = await formPickerData(this, undefined).forms(keys);
    if (id !== this.#lookupId) return;
    if (!items) {
      this.value = '';
      return;
    }
    const gone = this.localize.term('jfbFormPicker_labelNotAvailable');
    const names = keys.map((key) => items.find((item) => item.key === key)?.name ?? gone);
    this.value = this.localize.list(names, { type: 'conjunction' });
  }
}

customElements.define('ufm-jfb-form-name', FormNameUfmElement);

export { FormNameUfmComponent as api };

declare global {
  interface HTMLElementTagNameMap {
    'ufm-jfb-form-name': FormNameUfmElement;
  }
}
