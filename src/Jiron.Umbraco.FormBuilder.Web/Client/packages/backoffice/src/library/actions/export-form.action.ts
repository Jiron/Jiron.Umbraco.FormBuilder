// "Export as JSON" on a form (docs/ux/information-architecture.md, section 3; ADR-0046): the file of its draft, named by its alias,
// saved by the browser. The request carries the backoffice's token, which a plain link could not.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formExchangeExport, formGet } from '../../api';
import { sayRefusal } from '../library-changes';

/**
 * Hands the browser a file to save, through a link that never joins the page: Umbraco's router takes every click on a link in
 * the page for a navigation, and would try to open the file's address as a screen.
 */
export function saveFile(name: string, text: string, type = 'application/json'): void {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export class ExportFormAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;
    const { data, error } = await tryExecute(this, formExchangeExport({ path: { key }, parseAs: 'text' }), {
      disableNotifications: true,
    });
    if (typeof data !== 'string') {
      await sayRefusal(this, error);
      return;
    }

    saveFile(`${form.alias}.json`, data);
  }
}

export { ExportFormAction as api };
