// The Undo toast (ROADMAP 4.13, copy.md Confirmations): its button runs the undo once and closes the toast; one whose
// undo the server holds open for a time loses its button when that time ends and says it is too late (ADR-0023).
import type { UmbNotificationHandler } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, shadow } from '../testing';
import { UndoNotificationElement, type UndoNotificationData } from './undo-notification.element';

const TOO_LATE = "It's too late to undo this. Change the state back by hand if needed.";

async function toastOf(data: Partial<UndoNotificationData>) {
  const undo = vi.fn(async () => undefined);
  const close = vi.fn();
  const toast = new UndoNotificationElement();
  toast.data = { message: '3 entries approved.', undoLabel: 'Undo', undo, ...data };
  toast.notificationHandler = { close } as unknown as UmbNotificationHandler;
  document.body.append(toast);
  await toast.updateComplete;
  return {
    toast,
    undo,
    close,
    message: () => shadow(toast).querySelector('#message')?.textContent,
    button: () => shadow(toast).querySelector<HTMLElement>('uui-button'),
  };
}

describe('the Undo toast', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 7, 12, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
    document.body.replaceChildren();
  });

  it('runs the undo once and closes', async () => {
    const { undo, close, button } = await toastOf({});
    const undoButton = must(button(), 'the Undo button');
    expect(undoButton.getAttribute('label')).toBe('Undo');
    undoButton.click();
    undoButton.click();
    await Promise.resolve();
    expect([undo.mock.calls.length, close.mock.calls.length]).toEqual([1, 1]);
  });

  it('offers Undo until its window ends, then says it is too late without a button', async () => {
    const { toast, message, button } = await toastOf({ until: Date.now() + 10_000, tooLate: TOO_LATE });
    vi.advanceTimersByTime(9_999);
    await toast.updateComplete;
    expect([message(), button() !== null]).toEqual(['3 entries approved.', true]);

    vi.advanceTimersByTime(1);
    await toast.updateComplete;
    expect([message(), button()]).toEqual([`3 entries approved.\n${TOO_LATE}`, null]);
  });

  it('is too late at once when its window has ended before it shows, and never without one', async () => {
    const late = await toastOf({ until: Date.now() - 1, tooLate: TOO_LATE });
    expect(late.button()).toBeNull();

    const open = await toastOf({});
    vi.advanceTimersByTime(600_000);
    await open.toast.updateComplete;
    expect([open.message(), open.button() !== null]).toEqual(['3 entries approved.', true]);
  });
});
