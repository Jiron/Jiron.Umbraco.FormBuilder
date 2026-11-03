// The condition Jiron.FormBuilder.Condition.Permission (ADR-0013, ADR-0165): an extension shows only to users who hold what it
// names, or with `none` only to those who don't. It spares a user the menus that would refuse them; every endpoint still
// checks for itself. While the answer is on its way the extension stays away, so nothing flashes and then disappears.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type {
  UmbConditionConfigBase,
  UmbConditionControllerArguments,
  UmbExtensionCondition,
} from '@umbraco-cms/backoffice/extension-api';
import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import { myPermissions, PERMISSION_CONDITION } from './my-permissions';
import { meets, type Needs } from './sidebar';

export type PermissionConditionConfig = UmbConditionConfigBase<typeof PERMISSION_CONDITION> & {
  /** What the user must hold: one of these is enough. */
  needs: readonly Needs[];
  /** Turns it round: the extension is for users who hold none of it. */
  none?: boolean;
};

/** How many times a failed answer is asked for again, and the first wait. */
const RETRIES = 3;
const RETRY_AFTER_MS = 3000;

export class PermissionCondition extends UmbConditionBase<PermissionConditionConfig> implements UmbExtensionCondition {
  #retry: ReturnType<typeof setTimeout> | undefined;

  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<PermissionConditionConfig>) {
    super(host, args);
    void this.#ask(host, 0);
  }

  // A failed answer is tried again a few times, so that one bad moment doesn't leave the sidebar empty until a reload.
  async #ask(host: UmbControllerHost, tried: number): Promise<void> {
    const permissions = await myPermissions(host);
    if (permissions) {
      this.permitted = meets(permissions, this.config.needs) !== (this.config.none ?? false);
    } else if (tried < RETRIES) {
      this.#retry = setTimeout(() => void this.#ask(host, tried + 1), RETRY_AFTER_MS * (tried + 1));
    }
  }

  override destroy() {
    clearTimeout(this.#retry);
    super.destroy();
  }
}

export { PermissionCondition as api };

declare global {
  interface UmbExtensionConditionConfigMap {
    PermissionConditionConfig: PermissionConditionConfig;
  }
}
