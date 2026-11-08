// Library > Themes (ROADMAP 12.1, IA 7, ADR-0215): the library's list of the themes, each with its swatch, "Default" on
// the site's default, and the forms that use it; Create theme asks the theme in code it builds on, and Import a theme...
// reads an exported file into a new theme, which then opens.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { ThemeRowResponseModel } from '../api';
import { say } from '../library/items/library-items';
import { LibraryItemsScreen } from '../library/items/library-items-screen';
import { codeOf, open, sayRefusal } from '../library/library-changes';
import { newThemePath, themePath } from './constants';
import { NEW_THEME_MODAL } from './new-theme.modal.token';
import { THEME_KIND } from './theme-kind';
import { importTheme, readEditor, readRows } from './themes-api';

/** The largest file an import reads: far more than any theme's document. */
const MAX_FILE = 1024 * 1024;

export class ThemesScreenElement extends LibraryItemsScreen<ThemeRowResponseModel> {
  static override styles = [
    ...LibraryItemsScreen.styles,
    css`
      .swatch {
        display: inline-flex;
        flex: none;
        overflow: hidden;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .swatch span {
        inline-size: 10px;
        block-size: 20px;
      }

      input[type='file'] {
        display: none;
      }
    `,
  ];

  protected readonly kind = THEME_KIND;

  protected readRows(skip: number, take: number) {
    return readRows(this, skip, take);
  }

  protected override lead(row: ThemeRowResponseModel) {
    return html`<span class="swatch" aria-hidden="true"
      >${row.swatch.map((colour) => html`<span style=${`background:${colour}`}></span>`)}</span
    >`;
  }

  protected override mark(row: ThemeRowResponseModel) {
    return row.isDefault ? html`<uui-tag look="secondary">${this.term('default')}</uui-tag>` : nothing;
  }

  protected override footer() {
    return html`<uui-button
        slot="actions"
        look="secondary"
        label=${this.term('import')}
        @click=${() => this.shadowRoot?.querySelector<HTMLInputElement>('input[type=file]')?.click()}
      ></uui-button>
      <input type="file" accept=".json,application/json" tabindex="-1" @change=${this.#import} />`;
  }

  protected async create() {
    const editor = await readEditor(this);
    if (!editor) {
      await sayRefusal(this, undefined);
      return;
    }
    const chosen = await umbOpenModal(this, NEW_THEME_MODAL, { data: { bases: editor.bases } }).catch(() => undefined);
    if (chosen) open(newThemePath(chosen.baseTheme));
  }

  #import = async (event: Event) => {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (file.size > MAX_FILE) {
      await say(this, 'danger', this.term('importNotTheme'));
      return;
    }
    const { theme, error } = await importTheme(this, await file.text());
    if (theme) {
      await say(this, 'positive', this.term('imported', theme.name));
      open(themePath(theme.unique));
      return;
    }
    const code = codeOf(error);
    if (code === 'invalid_json' || code === 'unsupported_format')
      await say(this, 'danger', this.term('importNotTheme'));
    else if (code === 'validation_failed') await say(this, 'danger', this.term('importRefused'));
    else await sayRefusal(this, error);
  };
}

if (!customElements.get('jfb-themes-screen')) customElements.define('jfb-themes-screen', ThemesScreenElement);

export { ThemesScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-themes-screen': ThemesScreenElement;
  }
}
