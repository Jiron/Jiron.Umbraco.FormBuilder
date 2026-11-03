import { html } from '@umbraco-cms/backoffice/external/lit';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { umbConfirmModal, type UmbConfirmModalData } from '@umbraco-cms/backoffice/modal';

/**
 * Asks before a step that cannot be undone, with its content as text (section 4.10). Umbraco's confirm modal renders a string as
 * HTML, and Jiron Form Builder's texts carry what people wrote: a form's name, an option list's, an entry's title. The content
 * goes in as a Lit template, which escapes it. The screens use this, never `umbConfirmModal` itself.
 */
export function confirmText(
  host: UmbControllerHost,
  data: Omit<UmbConfirmModalData, 'content'> & { content: string },
): Promise<undefined> {
  return umbConfirmModal(host, { ...data, content: html`${data.content}` });
}
