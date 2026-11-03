// The condition Jiron.FormBuilder.Condition.FormPermission (docs/ux/information-architecture.md, section 11): a view or
// a footer action of the form workspace shows only to users who hold one of the permissions it names on the open form,
// as its grants and restrictions give them. Until the form is read it stays away, so nothing shows and then disappears.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type {
  UmbConditionConfigBase,
  UmbConditionControllerArguments,
  UmbExtensionCondition,
} from '@umbraco-cms/backoffice/extension-api';
import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import { FORM_PERMISSION_CONDITION, FORM_WORKSPACE_CONTEXT } from './constants';

export type FormPermissionConditionConfig = UmbConditionConfigBase<typeof FORM_PERMISSION_CONDITION> & {
  /** One of these on the open form is enough. */
  anyOf: readonly string[];
};

/** Whether the user holds one of these permissions on the form; nothing on a form not read yet. */
export const holdsAny = (held: readonly string[] | undefined, anyOf: readonly string[]): boolean =>
  anyOf.some((permission) => held?.includes(permission) === true);

export class FormPermissionCondition
  extends UmbConditionBase<FormPermissionConditionConfig>
  implements UmbExtensionCondition
{
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<FormPermissionConditionConfig>) {
    super(host, args);
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.observe(context?.form, (form) => (this.permitted = holdsAny(form?.permissions, this.config.anyOf)), 'form');
    });
  }
}

export { FormPermissionCondition as api };

declare global {
  interface UmbExtensionConditionConfigMap {
    FormPermissionConditionConfig: FormPermissionConditionConfig;
  }
}
