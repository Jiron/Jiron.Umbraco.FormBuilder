// How the theme workspace reads and saves a theme (ROADMAP 12.1, ADR-0215): the library's detail data source over the
// theme endpoints. A new theme's scaffold builds on the theme its create route names and changes nothing of it yet.
import { LibraryItemDataSource } from '../library/items/library-item-data-source';
import { THEME_ENTITY_TYPE } from './constants';
import type { ThemeDetailModel } from './theme-model';
import { createTheme, deleteTheme, readTheme, updateTheme } from './themes-api';

/** A new theme on a base theme, with a key of its own until the save gives it the key of its alias. */
export const scaffoldOf = (baseTheme: string, key: string): ThemeDetailModel => ({
  entityType: THEME_ENTITY_TYPE,
  unique: key,
  name: '',
  alias: '',
  baseTheme,
  tokens: { light: {}, dark: {} },
  customCss: '',
  concurrencyVersion: 0,
});

export class ThemeDetailDataSource extends LibraryItemDataSource<ThemeDetailModel> {
  protected scaffold(key: string, preset?: Partial<ThemeDetailModel>) {
    return scaffoldOf(preset?.baseTheme ? decodeURIComponent(preset.baseTheme) : 'default', key);
  }

  protected readItem(key: string) {
    return readTheme(this, key);
  }

  protected createItem(model: ThemeDetailModel) {
    return createTheme(this, model);
  }

  protected updateItem(model: ThemeDetailModel) {
    return updateTheme(this, model);
  }

  protected deleteItem(key: string, concurrencyVersion: number) {
    return deleteTheme(this, key, concurrencyVersion);
  }
}

export { ThemeDetailDataSource as api };
