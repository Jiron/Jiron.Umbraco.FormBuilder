// What a folder's screen tells the actions in its header (ADR-0166): whether the folder holds anything the user sees, which the
// tree tells the same actions through its item.
import { UmbContextBase } from '@umbraco-cms/backoffice/class-api';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbBooleanState } from '@umbraco-cms/backoffice/observable-api';

export class FolderScreenContext extends UmbContextBase {
  #empty = new UmbBooleanState(false);
  /** Whether the folder holds nothing the user sees. */
  readonly isEmpty = this.#empty.asObservable();

  constructor(host: UmbControllerHost) {
    super(host, FOLDER_SCREEN_CONTEXT);
  }

  setEmpty(empty: boolean): void {
    this.#empty.setValue(empty);
  }
}

export const FOLDER_SCREEN_CONTEXT = new UmbContextToken<FolderScreenContext>('Jiron.FormBuilder.Context.FolderScreen');
