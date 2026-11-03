// A folder or form in the forms tree (docs/ux/information-architecture.md, section 3; ADR-0166): Umbraco's own tree item,
// with an unpublished form's name muted as Umbraco mutes a draft page, and said to screen readers once. The lock and the
// unseen dot are Umbraco's entity signs, from the item's flags.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbTreeItemElementBase } from '@umbraco-cms/backoffice/tree';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import type { LibraryTreeItem } from './library-tree.repository';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';

export class LibraryTreeItemElement extends UmbTreeItemElementBase<LibraryTreeItem> {
  static override styles = [
    ...UmbTreeItemElementBase.styles,
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      .unpublished {
        color: var(--jfb-bo-color-text-muted);
        font-style: italic;
      }
    `,
  ];

  override renderLabel() {
    const item = this._item;
    // Only a live form has a published version or not; the Recycle Bin and what it holds are never muted.
    const unpublished = item?.entityType === FORM_ENTITY_TYPE && item.isPublished === false;
    return html`<span slot="label" class=${unpublished ? 'unpublished' : ''}
      >${this.localize.string(item?.name ?? '')}${
        unpublished
          ? html`<span class="visually-hidden">, ${this.localize.term('jfbLibrary_notPublished')}</span>`
          : nothing
      }</span
    >`;
  }
}

if (!customElements.get('jfb-library-tree-item'))
  customElements.define('jfb-library-tree-item', LibraryTreeItemElement);

export { LibraryTreeItemElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-library-tree-item': LibraryTreeItemElement;
  }
}
