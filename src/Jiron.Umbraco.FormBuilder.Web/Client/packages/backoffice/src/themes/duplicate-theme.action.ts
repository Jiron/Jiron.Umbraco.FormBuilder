// "Duplicate" in a theme's "..." (ROADMAP 12.1, IA 7, ADR-0215): a copy under the first free alias, which then opens.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { duplicateItem } from '../library/items/library-items';
import { THEME_KIND } from './theme-kind';

export class DuplicateThemeAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await duplicateItem(this, THEME_KIND, this.args.unique);
  }
}

export { DuplicateThemeAction as api };
