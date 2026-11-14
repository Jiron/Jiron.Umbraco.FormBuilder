// The toast that says the section's words and may offer one button (ROADMAP 11.4, ADR-0210): it says its sentence by its
// key, takes each new sentence it is told without reading every step out, and its button runs once and closes it.
import type { UmbNotificationHandler } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { loadEntryWords } from '../entries/entry-words';
import { must, registerEnglish, shadow, text } from '../testing';
import { ActionNotificationElement, type ActionNotificationData, type Said } from './action-notification.element';

async function toastOf(data: Partial<ActionNotificationData>) {
  const close = vi.fn();
  const toast = new ActionNotificationElement();
  toast.data = { message: '', said: { key: 'jfbEntries_export_readyToast', args: [12400] }, ...data };
  toast.notificationHandler = { close } as unknown as UmbNotificationHandler;
  document.body.append(toast);
  await toast.updateComplete;
  return {
    toast,
    close,
    message: () => shadow(toast).querySelector('#message'),
    button: () => shadow(toast).querySelector<HTMLElement>('uui-button'),
  };
}

beforeAll(async () => {
  registerEnglish();
  await loadEntryWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the action toast', () => {
  it('says its sentence by its key, and its button runs once and closes the toast', async () => {
    const run = vi.fn(async () => undefined);
    const { message, button, close } = await toastOf({
      positive: true,
      action: { label: { key: 'jfbEntries_export_download' }, run },
    });

    expect(text(message())).toBe('Your export of 12,400 entries is ready.');
    expect(message()?.classList.contains('positive')).toBe(true);
    const download = must(button(), 'the Download button');
    expect(download.getAttribute('label')).toBe('Download');
    download.click();
    download.click();
    await Promise.resolve();
    expect(close).toHaveBeenCalledTimes(1);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('takes each new sentence it is told, without reading every step out, and stops listening once closed', async () => {
    let show: ((said: Said) => void) | undefined;
    const stop = vi.fn();
    const { toast, message, button } = await toastOf({
      said: { key: 'jfbEntries_export_preparingToast', args: [6, false] },
      watch: (listener) => {
        show = listener;
        return stop;
      },
    });

    expect(text(message())).toBe('Preparing the export of 6 entries. A notification will link to the file.');
    expect(message()?.hasAttribute('aria-live'), 'its first sentence is announced').toBe(false);
    expect(button(), 'no button without an action').toBeNull();
    must(show, 'the listener')({ key: 'jfbEntries_export_progressToast', args: [2, 6, false] });
    await toast.updateComplete;
    expect(text(message())).toBe('Preparing the export: 2 of 6 entries. A notification will link to the file.');
    expect(message()?.getAttribute('aria-live')).toBe('off');

    toast.remove();
    expect(stop).toHaveBeenCalled();
  });
});
