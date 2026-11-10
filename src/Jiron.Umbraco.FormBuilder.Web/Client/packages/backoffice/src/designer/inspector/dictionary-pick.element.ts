// "Use a dictionary item" (ROADMAP 14.1, ADR-0228): a button at the end of a plain text box's row, which opens Umbraco's
// own dictionary picker; the item chosen becomes the box's text in the language shown, `#` and its key, as typing it
// would. Only a user whose groups open the Translation section, where the dictionary lives, gets the button, as no
// one else may read the dictionary's tree. The row shows it while it is hovered or holds the focus (the styles below).
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { UMB_DICTIONARY_PICKER_MODAL, UmbDictionaryItemRepository } from '@umbraco-cms/backoffice/dictionary';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { dictionaryKeyOf } from '../dictionary-texts';

/** The section whose users Umbraco lets read the dictionary. */
const TRANSLATION_SECTION = 'Umb.Section.Translation';

export class DictionaryPickElement extends UmbLitElement {
  static override properties = {
    label: {},
    pick: { attribute: false },
    _allowed: { state: true },
  };

  static override styles = css`
    /*
     * At the end of its row, seen while the row is hovered or holds the focus: the row's own styles hide it otherwise
     * (.item:not(:hover, :focus-within) jfb-dictionary-pick), and Tab still reaches it, which shows it.
     */
    :host {
      display: inline-flex;
      margin-inline-start: auto;
      transition: opacity var(--jfb-bo-motion-fast, 120ms) var(--jfb-bo-motion-ease, ease-out);
    }

    @media (prefers-reduced-motion: reduce) {
      :host {
        transition: none;
      }
    }

    button {
      display: inline-grid;
      place-items: center;
      inline-size: 28px;
      block-size: 28px;
      margin: 0;
      padding: 0;
      border: 0;
      border-radius: var(--uui-border-radius);
      background: none;
      color: var(--uui-color-interactive);
      font: inherit;
      cursor: pointer;
    }

    button:hover {
      background: var(--uui-color-surface-emphasis);
      color: var(--uui-color-interactive-emphasis);
    }

    button:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }
  `;

  /** The box's own label, which the button's name says. */
  declare label: string;
  /** Puts the text of the item chosen into the box, as typing it would. */
  declare pick: (text: string) => void;
  declare private _allowed: boolean;

  constructor() {
    super();
    this.label = '';
    this.pick = () => undefined;
    this._allowed = false;
    this.consumeContext(UMB_CURRENT_USER_CONTEXT, (user) => {
      this.observe(user?.allowedSections, (sections) => {
        this._allowed = sections?.includes(TRANSLATION_SECTION) ?? false;
      });
    });
  }

  async #open() {
    const chosen = await umbOpenModal(this, UMB_DICTIONARY_PICKER_MODAL, { data: { multiple: false } }).catch(
      () => undefined,
    );
    const unique = chosen?.selection?.[0];
    if (!unique) return;
    const { data } = await new UmbDictionaryItemRepository(this).requestItems([unique]);
    const key = data?.[0]?.name;
    // Only a key a text can name: the server reads nothing else as one.
    if (key && dictionaryKeyOf(`#${key}`) === key) this.pick(`#${key}`);
  }

  override render() {
    if (!this._allowed) return nothing;
    const name = this.localize.term('jfbDesigner_useDictionary');
    return html`<button
      type="button"
      class="dictionary-pick"
      aria-label=${this.label ? this.localize.term('jfbDesigner_useDictionaryFor', this.label) : name}
      title=${name}
      @click=${() => void this.#open()}
    >
      <uui-icon name="icon-book-alt" aria-hidden="true"></uui-icon>
    </button>`;
  }
}

if (!customElements.get('jfb-dictionary-pick')) customElements.define('jfb-dictionary-pick', DictionaryPickElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-dictionary-pick': DictionaryPickElement;
  }
}
