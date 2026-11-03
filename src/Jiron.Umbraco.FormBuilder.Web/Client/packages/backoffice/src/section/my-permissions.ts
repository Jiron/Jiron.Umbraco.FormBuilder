// What the signed-in user holds, asked once per page load: every menu item asks, and they share the one answer. A failed
// answer is not kept, so the next asker tries again; until then the extensions that depend on it stay away.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { mePermissions } from '../api';
import type { MyPermissions } from './sidebar';

/** The condition's alias. */
export const PERMISSION_CONDITION = 'Jiron.FormBuilder.Condition.Permission';

let asked: Promise<MyPermissions | undefined> | undefined;

/** What the user holds; undefined when the request failed. */
export function myPermissions(host: UmbControllerHost): Promise<MyPermissions | undefined> {
  asked ??= tryExecute(host, mePermissions(), { disableNotifications: true }).then(({ data, error }) => {
    if (error || !data) {
      asked = undefined;
      return undefined;
    }
    return data;
  });
  return asked;
}

/** Forgets the answer, for the tests. */
export function forgetMyPermissions(): void {
  asked = undefined;
}
