// What the signed-in user holds, asked once per page load: the sidebar's extensions ask as the section opens, and every
// asker shares the one answer, which a screen's tabs then draw from at once (ADR-0257). A failed answer is not kept, so
// the next asker tries again; until then the extensions that depend on it stay away.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { mePermissions } from '../api';
import type { MyPermissions } from './sidebar';

/** The condition's alias. */
export const PERMISSION_CONDITION = 'Jiron.FormBuilder.Condition.Permission';

let asked: Promise<MyPermissions | undefined> | undefined;
let answered: MyPermissions | undefined;

/** What the user holds; undefined when the request failed. */
export function myPermissions(host: UmbControllerHost): Promise<MyPermissions | undefined> {
  asked ??= tryExecute(host, mePermissions(), { disableNotifications: true }).then(({ data, error }) => {
    if (error || !data) {
      asked = undefined;
      return undefined;
    }
    answered = data;
    return data;
  });
  return asked;
}

/** What the user holds when it has been answered already, so what draws from it needs no wait. */
export function myPermissionsNow(): MyPermissions | undefined {
  return answered;
}

/** Forgets the answer, for the tests. */
export function forgetMyPermissions(): void {
  asked = undefined;
  answered = undefined;
}
