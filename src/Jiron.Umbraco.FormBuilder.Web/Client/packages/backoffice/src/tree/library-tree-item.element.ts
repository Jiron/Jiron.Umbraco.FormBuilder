// A folder or form in the forms tree (docs/ux/information-architecture.md, section 3; ADR-0166): Umbraco's own tree item,
// with an unpublished form's name muted as Umbraco mutes a draft page, and said to screen readers once. The lock and the
// unseen dot are Umbraco's entity signs, from the item's flags.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbTreeItemElementBase } from '@umbraco-cms/backoffice/tree';
import { FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, RECYCLE_BIN_ENTITY_TYPE } from '../section/entity-types';
import type { LibraryTreeItem } from './library-tree.repository';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';

const ROOTS = new Set([LIBRARY_ROOT_ENTITY_TYPE, RECYCLE_BIN_ENTITY_TYPE]);

/**
 * A tree item's name as it shows: a root's (Forms, the Recycle Bin) is a dictionary key in the user's language; a folder's
 * or form's is the name an editor gave it, shown as it stands even when it looks like a key.
 */
export function nameOf(
  item: Pick<LibraryTreeItem, 'entityType' | 'name'> | undefined,
  localize: (text: string) => string,
): string {
  if (!item) return '';
  return ROOTS.has(item.entityType) ? localize(item.name) : item.name;
}

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

      /* On the open or chosen row the label takes the row's own colour: the muted grey is 2.3:1 on its fill. */
      uui-menu-item:is([active], [selected]) > .unpublished {
        color: inherit;
      }
    `,
  ];

  override renderLabel() {
    const item = this._item;
    // Only a live form has a published version or not; the Recycle Bin and what it holds are never muted.
    const unpublished = item?.entityType === FORM_ENTITY_TYPE && item.isPublished === false;
    // The id is the one Umbraco's own items give their label, which its styles end with an ellipsis: a long name then
    // keeps the row as wide as the sidebar, and the row's "..." in reach (the Phase 12 review).
    return html`<span id="label" slot="label" class=${unpublished ? 'unpublished' : ''}
      >${nameOf(item, (text) => this.localize.string(text))}${
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
