// "Delete..." in a theme's "..." (ROADMAP 12.1, IA 7, ADR-0215): asked first. A theme that forms use is not deleted, and
// neither is the site's default.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { deleteItem } from '../library/items/library-items';
import { THEME_KIND } from './theme-kind';

export class DeleteThemeAction extends UmbEntityActionBase<never> {
  override async execute() {
    if (this.args.unique) await deleteItem(this, THEME_KIND, this.args.unique);
  }
}

export { DeleteThemeAction as api };
