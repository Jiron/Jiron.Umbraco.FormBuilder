// The create dialog's "Form (a new, empty form)" (docs/ux/information-architecture.md, section 4; ADR-0166).
import { UmbEntityCreateOptionActionBase } from '@umbraco-cms/backoffice/entity-create-option-action';
import { createBlankForm } from './new-form';

export class CreateFormOption extends UmbEntityCreateOptionActionBase {
  override async execute() {
    await createBlankForm(this, this.args.unique ?? null);
  }
}

export { CreateFormOption as api };
