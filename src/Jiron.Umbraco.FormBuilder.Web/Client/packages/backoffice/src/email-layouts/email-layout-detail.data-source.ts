// How the email layout workspace reads and saves a layout (ROADMAP 12.1, ADR-0215): the library's detail data source
// over the layout endpoints. A new layout is the card look with mail's own colours, its key its own from the start.
import { LibraryItemDataSource } from '../library/items/library-item-data-source';
import { EMAIL_LAYOUT_ENTITY_TYPE } from './constants';
import { NEW_DESIGN, type EmailLayoutDetailModel } from './email-layout-model';
import { createLayout, deleteLayout, readLayout, updateLayout } from './email-layouts-api';

export const scaffoldOf = (key: string): EmailLayoutDetailModel => ({
  entityType: EMAIL_LAYOUT_ENTITY_TYPE,
  unique: key,
  name: '',
  alias: '',
  design: NEW_DESIGN,
  concurrencyVersion: 0,
});

export class EmailLayoutDetailDataSource extends LibraryItemDataSource<EmailLayoutDetailModel> {
  protected scaffold(key: string) {
    return scaffoldOf(key);
  }

  protected readItem(key: string) {
    return readLayout(this, key);
  }

  protected createItem(model: EmailLayoutDetailModel) {
    return createLayout(this, model);
  }

  protected updateItem(model: EmailLayoutDetailModel) {
    return updateLayout(this, model);
  }

  protected deleteItem(key: string, concurrencyVersion: number) {
    return deleteLayout(this, key, concurrencyVersion);
  }
}

export { EmailLayoutDetailDataSource as api };
