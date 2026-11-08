// "Export" in a theme's "..." (ROADMAP 12.1, IA 7, ADR-0215): the theme's document as a file to keep or to import on
// another site.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { say, term, wordsFor } from '../library/items/library-items';
import { THEME_KIND } from './theme-kind';
import { exportTheme } from './themes-api';

export class ExportThemeAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const localize = await wordsFor(this, THEME_KIND);
    const file = await exportTheme(this, key);
    if (!file) {
      await say(this, 'danger', term(localize, THEME_KIND, 'exportFailed'));
      return;
    }
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([file.document], { type: 'application/json' }));
    link.download = file.fileName;
    link.click();
    URL.revokeObjectURL(link.href);
  }
}

export { ExportThemeAction as api };
