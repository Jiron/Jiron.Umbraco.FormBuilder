// Library > Option lists (ROADMAP 12.1, IA 7, ADR-0214): the library's list of the lists, each with its kind and the
// forms that use it; Create option list asks the kind first. With no list, one sentence says what lists are for.
import { html } from '@umbraco-cms/backoffice/external/lit';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { OptionListRowResponseModel } from '../api';
import { LibraryItemsScreen } from '../library/items/library-items-screen';
import { open } from '../library/library-changes';
import { newListPath } from './constants';
import { NEW_OPTION_LIST_MODAL } from './new-option-list.modal.token';
import { OPTION_LIST_KIND } from './option-list-kind';
import { isKind } from './option-list-model';
import { readRows } from './option-lists-api';

export class OptionListsScreenElement extends LibraryItemsScreen<OptionListRowResponseModel> {
  protected readonly kind = OPTION_LIST_KIND;
  protected override icon = 'icon-bulleted-list';

  protected readRows(skip: number, take: number) {
    return readRows(this, skip, take);
  }

  protected override extraHead() {
    return this.term('columnKind');
  }

  protected override extraCell(row: OptionListRowResponseModel) {
    return html`${this.term(`kind_${isKind(row.kind) ? row.kind : 'unknown'}`)}`;
  }

  protected async create() {
    const chosen = await umbOpenModal(this, NEW_OPTION_LIST_MODAL, { data: {} }).catch(() => undefined);
    if (chosen) open(newListPath(chosen.kind));
  }
}

if (!customElements.get('jfb-option-lists-screen'))
  customElements.define('jfb-option-lists-screen', OptionListsScreenElement);

export { OptionListsScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-option-lists-screen': OptionListsScreenElement;
  }
}
