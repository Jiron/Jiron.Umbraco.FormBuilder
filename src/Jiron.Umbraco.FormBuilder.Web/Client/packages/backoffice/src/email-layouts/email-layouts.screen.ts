// Library > Email layouts (ROADMAP 12.1, IA 7, ADR-0215): the library's list of the layouts, each with its swatch,
// "Default" on the site's default, and the email actions that use it; Create email layout opens a new card layout.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { EmailLayoutRowResponseModel } from '../api';
import { LibraryItemsScreen } from '../library/items/library-items-screen';
import { open } from '../library/library-changes';
import { newLayoutPath } from './constants';
import { EMAIL_LAYOUT_KIND } from './email-layout-kind';
import { readRows } from './email-layouts-api';

export class EmailLayoutsScreenElement extends LibraryItemsScreen<EmailLayoutRowResponseModel> {
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
    `,
  ];

  protected readonly kind = EMAIL_LAYOUT_KIND;

  protected readRows(skip: number, take: number) {
    return readRows(this, skip, take);
  }

  protected override lead(row: EmailLayoutRowResponseModel) {
    return html`<span class="swatch" aria-hidden="true"
      >${row.swatch.map((colour) => html`<span style=${`background:${colour}`}></span>`)}</span
    >`;
  }

  protected override mark(row: EmailLayoutRowResponseModel) {
    return row.isDefault ? html`<uui-tag look="secondary">${this.term('default')}</uui-tag>` : nothing;
  }

  protected override extraHead() {
    return this.term('columnLook');
  }

  protected override extraCell(row: EmailLayoutRowResponseModel) {
    return this.term(row.look === 'letter' ? 'look_letter' : 'look_card');
  }

  protected create() {
    open(newLayoutPath());
    return Promise.resolve();
  }
}

if (!customElements.get('jfb-email-layouts-screen'))
  customElements.define('jfb-email-layouts-screen', EmailLayoutsScreenElement);

export { EmailLayoutsScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-email-layouts-screen': EmailLayoutsScreenElement;
  }
}
