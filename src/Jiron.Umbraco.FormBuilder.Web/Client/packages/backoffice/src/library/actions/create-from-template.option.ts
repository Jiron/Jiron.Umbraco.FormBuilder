// The create dialog's "Form from a template..." (docs/ux/information-architecture.md, section 4; ADR-0166).
import { UmbEntityCreateOptionActionBase } from '@umbraco-cms/backoffice/entity-create-option-action';
import { createFromTemplate } from './new-form';

export class CreateFromTemplateOption extends UmbEntityCreateOptionActionBase {
  override async execute() {
    await createFromTemplate(this, this.args.unique ?? null);
  }
}

export { CreateFromTemplateOption as api };
