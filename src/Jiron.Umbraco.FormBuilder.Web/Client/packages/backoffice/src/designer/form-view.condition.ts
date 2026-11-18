// The condition Jiron.FormBuilder.Condition.FormChangingView (ADR-0258, section 4): the form's Preview, Save draft and
// Publish... show on the views that change the form (Design, Settings, Actions) and not on Entries, Insights and
// History, which show what the form brought in. It follows the address, as the views' tabs do.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type {
  UmbConditionConfigBase,
  UmbConditionControllerArguments,
  UmbExtensionCondition,
} from '@umbraco-cms/backoffice/extension-api';
import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import type { FORM_VIEW_CONDITION } from './constants';
import { changesForm, shownView } from './form-views';

export type FormViewConditionConfig = UmbConditionConfigBase<typeof FORM_VIEW_CONDITION>;

export class FormViewCondition extends UmbConditionBase<FormViewConditionConfig> implements UmbExtensionCondition {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<FormViewConditionConfig>) {
    super(host, args);
    window.addEventListener('navigationend', this.#check);
    this.#check();
  }

  readonly #check = () => {
    this.permitted = changesForm(shownView(window.location.pathname));
  };

  override destroy() {
    window.removeEventListener('navigationend', this.#check);
    super.destroy();
  }
}

export { FormViewCondition as api };

declare global {
  interface UmbExtensionConditionConfigMap {
    FormViewConditionConfig: FormViewConditionConfig;
  }
}
