// The theme of a form picker's settings (ADR-0135): a select of the site's themes, first "The form's own theme", which
// stores no value. A theme the settings name that the site no longer has stays chosen and says so, until the editor
// changes it; the server refuses it when the data type is saved. While the themes load, a box in the select's shape
// holds its place; a failure is said in place, with Try again.
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { css, html, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UmbPropertyEditorUiElement } from '@umbraco-cms/backoffice/property-editor';
import { jfbBackofficeTokens } from '../styles/tokens';
import { withThemeLabel } from '../themes/theme-label';
import { formPickerData, type PickerTheme } from './picker-data';

/**
 * The option of the form's own theme. uui-select keeps the empty value for a hidden placeholder of its own, which it
 * would show in place of this option, so it takes a value no theme's alias can have (they start with a letter).
 */
export const OWN_THEME = '(own)';

/** An option of the select, as uui-select takes it. */
interface ThemeOption {
  name: string;
  value: string;
  selected: boolean;
}

export class FormPickerThemeElement extends UmbLitElement implements UmbPropertyEditorUiElement {
  static override properties = {
    value: { type: String },
    readonly: { type: Boolean, reflect: true },
    _themes: { state: true },
    _failed: { state: true },
  };

  declare value: string | undefined;
  declare readonly: boolean;
  /** The site's themes once they are loaded. */
  declare private _themes: readonly PickerTheme[] | undefined;
  declare private _failed: boolean;

  constructor() {
    super();
    this.value = undefined;
    this.readonly = false;
    this._themes = undefined;
    this._failed = false;
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    void this.#load();
  }

  async #load() {
    this._failed = false;
    this._themes = undefined;
    const themes = await formPickerData(this, undefined).themes();
    this._failed = themes === undefined;
    this._themes = themes;
  }

  #onChange(event: Event) {
    event.stopPropagation();
    const chosen = String((event.target as HTMLElement & { value: unknown }).value ?? '');
    this.value = chosen === '' || chosen === OWN_THEME ? undefined : chosen;
    this.dispatchEvent(new UmbChangeEvent());
  }

  /** The form's own theme, the site's themes, and a chosen theme the site lacks, at the end. */
  #options(themes: readonly PickerTheme[]): ThemeOption[] {
    const current = this.value ?? '';
    const options: ThemeOption[] = [
      { name: this.localize.term('jfbFormPicker_themeOwn'), value: OWN_THEME, selected: current === '' },
      ...themes.map((theme) => ({
        name: withThemeLabel(theme, (key) => this.localize.term(key)).label,
        value: theme.alias,
        selected: theme.alias === current,
      })),
    ];
    if (current !== '' && !themes.some((theme) => theme.alias === current)) {
      options.push({ name: this.localize.term('jfbFormPicker_themeMissing', current), value: current, selected: true });
    }
    return options;
  }

  override render() {
    if (this._failed) {
      return html`<div class="failure" role="alert">
        <span>${this.localize.term('jfbFormPicker_themesFailed')}</span>
        <uui-button
          look="secondary"
          compact
          label=${this.localize.term('jfbFormPicker_tryAgain')}
          @click=${() => void this.#load()}
        ></uui-button>
      </div>`;
    }
    if (!this._themes) return html`<div class="placeholder" aria-hidden="true"><span class="bar"></span></div>`;
    return html`<uui-select
      label=${this.localize.term('jfbFormPicker_settingTheme')}
      .options=${this.#options(this._themes)}
      ?readonly=${this.readonly}
      @change=${this.#onChange}
    ></uui-select>`;
  }

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      /* The shape of uui-select: a bordered box of its height with a line of text. */
      .placeholder {
        box-sizing: border-box;
        display: flex;
        align-items: center;
        width: 16em;
        max-width: 100%;
        height: var(--uui-size-11);
        padding-inline: var(--uui-size-space-3);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .bar {
        display: block;
        width: 60%;
        height: 0.75em;
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-surface-alt);
      }

      .failure {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];
}

customElements.define('jfb-form-picker-theme', FormPickerThemeElement);

export { FormPickerThemeElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-picker-theme': FormPickerThemeElement;
  }
}
