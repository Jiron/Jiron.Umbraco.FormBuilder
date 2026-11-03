// The create dialog's "Import a form..." (docs/ux/information-architecture.md, section 4; ADR-0166).
import { UmbEntityCreateOptionActionBase } from '@umbraco-cms/backoffice/entity-create-option-action';
import { importForm } from './new-form';

export class ImportFormOption extends UmbEntityCreateOptionActionBase {
  override async execute() {
    await importForm(this, this.args.unique ?? null);
  }
}

export { ImportFormOption as api };
