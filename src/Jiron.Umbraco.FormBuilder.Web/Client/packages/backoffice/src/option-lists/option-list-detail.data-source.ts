// How the option list workspace reads and saves a list (ROADMAP 12.1, ADR-0214): the library's detail data source over
// the list endpoints. A new list's scaffold holds the kind its create route names, and its key from the start, so a
// preview and a save name the same list.
import { LibraryItemDataSource } from '../library/items/library-item-data-source';
import { OPTION_LIST_ENTITY_TYPE } from './constants';
import { isKind, keepOfNew, type OptionListDetailModel } from './option-list-model';
import { createList, deleteList, readList, updateList } from './option-lists-api';

export { SAVE_PROBLEMS } from '../library/items/library-item-data-source';

/** The new list a create route's scaffold holds: its kind, a key of its own, and the kind's keep time. */
export function scaffoldOf(kind: string, key: string): OptionListDetailModel {
  const known = isKind(kind) ? kind : 'manual';
  return {
    entityType: OPTION_LIST_ENTITY_TYPE,
    unique: key,
    name: '',
    alias: '',
    kind: known,
    config: {},
    items: [],
    cacheSeconds: keepOfNew(known),
    concurrencyVersion: 0,
  };
}

export class OptionListDetailDataSource extends LibraryItemDataSource<OptionListDetailModel> {
  protected scaffold(key: string, preset?: Partial<OptionListDetailModel>) {
    return scaffoldOf(preset?.kind ?? 'manual', key);
  }

  protected readItem(key: string) {
    return readList(this, key);
  }

  protected createItem(model: OptionListDetailModel) {
    return createList(this, model);
  }

  protected updateItem(model: OptionListDetailModel) {
    return updateList(this, model);
  }

  protected deleteItem(key: string, concurrencyVersion: number) {
    return deleteList(this, key, concurrencyVersion);
  }
}

export { OptionListDetailDataSource as api };
