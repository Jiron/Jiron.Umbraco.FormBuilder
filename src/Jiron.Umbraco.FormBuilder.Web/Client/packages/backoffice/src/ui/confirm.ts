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

/** One of the things a confirmation lists: its name, and a line on it in a quieter voice. */
export interface ConfirmItem {
  readonly name: string;
  readonly detail: string;
}

/**
 * Asks before a step whose effects leave the backoffice, listing what it sets off (ADR-0204): its content and each item as
 * text, as {@link confirmText} has them.
 */
export function confirmList(
  host: UmbControllerHost,
  data: Omit<UmbConfirmModalData, 'content'> & { content: string; items: readonly ConfirmItem[] },
): Promise<undefined> {
  const { items, content, ...rest } = data;
  return umbConfirmModal(host, {
    ...rest,
    content: html`<p style="margin: 0">${content}</p>
      <ul
        style="display: grid; gap: var(--uui-size-space-3); margin: var(--uui-size-space-4) 0 0; padding: 0; list-style: none"
      >
        ${items.map(
          (item) =>
            html`<li>
              <strong>${item.name}</strong>${
                item.detail ? html` <span style="color: var(--uui-color-text-alt)">${item.detail}</span>` : ''
              }
            </li>`,
        )}
      </ul>`,
  });
}
