// What an option list's "..." shares (ROADMAP 12.1, IA 7, ADR-0214): its words, a toast, and the dialog that names the
// forms using a list, which a delete shows instead of deleting it.
import { html } from '@umbraco-cms/backoffice/external/lit';
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbInfoModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { loadOptionListWords } from './option-list-words';
import { readUsers } from './option-lists-api';

export const term = (localize: UmbLocalizationController, key: string, ...args: unknown[]) =>
  localize.term(`jfbOptionLists_${key}`, ...args);

export async function say(host: UmbClassInterface, color: 'positive' | 'danger', message: string): Promise<void> {
  (await host.getContext(UMB_NOTIFICATION_CONTEXT))?.peek(color, { data: { message } });
}

/** The words, which a "..." opened from elsewhere than the list's workspace may not have loaded yet. */
export async function wordsFor(host: UmbClassInterface): Promise<UmbLocalizationController> {
  const localize = new UmbLocalizationController(host);
  await loadOptionListWords(localize.lang()).catch(() => undefined);
  return localize;
}

/**
 * Says that forms use a list, naming those the user may open and counting the rest; false when none does, so the delete
 * may go on.
 */
export async function sayInUse(host: UmbClassInterface, key: string, name: string): Promise<boolean> {
  const users = await readUsers(host, key);
  if (!users || users.forms.length + users.others === 0) return false;
  const localize = new UmbLocalizationController(host);
  await umbInfoModal(host, {
    headline: term(localize, 'inUseHeadline', name),
    content: html`<p style="margin: 0">${term(localize, 'inUseText')}</p>
      <ul
        style="display: grid; gap: var(--uui-size-space-2); margin: var(--uui-size-space-4) 0 0; padding-inline-start: 1.25em"
      >
        ${users.forms.map((form) => html`<li>${form.name}</li>`)}
        ${users.others > 0 ? html`<li>${term(localize, 'usersOthers', users.others)}</li>` : ''}
      </ul>`,
  }).catch(() => undefined);
  return true;
}
