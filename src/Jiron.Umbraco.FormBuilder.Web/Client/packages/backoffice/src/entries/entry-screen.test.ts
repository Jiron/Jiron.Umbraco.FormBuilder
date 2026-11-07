// An entry's own screen and the Forward... sidebar (ROADMAP 11.2, IA 6.3, ADR-0208): the screen asks which form holds
// the entry and shows the panel alone, or one sentence for an entry out of reach; the sidebar offers only the users and
// team addresses the server names, sends the one chosen, and sensitive values only where the user may send them.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryForwardRecipientsResponseModel, EntryForwardRequestModel } from '../api';
import { all, must, registerEnglish, settle, shadow, text } from '../testing';
import type { EntryForwardModalElement } from './entry-forward.modal.element';
import { loadEntryWords } from './entry-words';

const server = vi.hoisted(() => ({
  forms: {} as Record<string, string | null>,
  recipients: undefined as EntryForwardRecipientsResponseModel | undefined,
  forwards: [] as EntryForwardRequestModel[],
  forwardFails: false,
  opens: [] as string[],
}));

vi.mock('./entry-panel-api', () => ({
  locateEntry: async (_host: unknown, entry: string) => server.forms[entry] ?? null,
  openEntry: async (_host: unknown, _form: string, entry: string) => {
    server.opens.push(entry);
    return undefined;
  },
  readRecipients: async () => server.recipients,
  forwardEntry: async (_host: unknown, _form: string, _entry: string, request: EntryForwardRequestModel) => {
    server.forwards.push(request);
    return !server.forwardFails;
  },
}));

await import('./entry-screen.element');
await import('./entry-forward.modal.element');

beforeAll(async () => {
  registerEnglish();
  await loadEntryWords('en');
});

beforeEach(() => {
  Object.assign(server, {
    forms: { e7: 'form1' },
    recipients: {
      users: [
        { key: 'u1', name: 'Ana Silva' },
        { key: 'u2', name: 'Ben Okafor' },
      ],
      addresses: ['hr@example.org'],
    },
    forwards: [],
    forwardFails: false,
    opens: [],
  });
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("an entry's own screen", () => {
  it('shows the panel alone for the form the server names', async () => {
    const screen = document.createElement('jfb-entry-screen');
    screen.unique = 'e7';
    document.body.append(screen);
    await vi.waitFor(() => expect(shadow(screen).querySelector('jfb-entry-panel')).not.toBeNull());
    const panel = must(shadow(screen).querySelector('jfb-entry-panel'), 'the panel');
    expect([panel.form, panel.entry, panel.place]).toEqual(['form1', 'e7', 'alone']);
  });

  it("gives Umbraco's not found, and nothing of the entry, where it is gone or out of reach", async () => {
    const screen = document.createElement('jfb-entry-screen');
    screen.unique = 'e9';
    document.body.append(screen);
    await vi.waitFor(() => expect(shadow(screen).querySelector('umb-entity-detail-not-found')).not.toBeNull());
    expect(shadow(screen).querySelector('umb-entity-detail-not-found')?.getAttribute('entity-type')).toBe('entry');
    expect(shadow(screen).querySelector('jfb-entry-panel')).toBeNull();
    expect(server.opens).toEqual([]);
  });
});

describe('the Forward... sidebar', () => {
  async function sidebar(maySendSensitive: boolean) {
    const context = { setValue: vi.fn(), submit: vi.fn(), reject: vi.fn(), getValue: () => undefined };
    const element = document.createElement('jfb-entry-forward-modal') as EntryForwardModalElement;
    element.modalContext = context as never;
    element.data = { form: 'form1', entry: 'e7', number: 7, maySendSensitive };
    document.body.append(element);
    await vi.waitFor(() => expect(shadow(element).querySelector('select, p')).not.toBeNull());
    await settle(element);
    const button = (label: string) =>
      must(
        all(element, 'uui-button').find((one) => one.getAttribute('label') === label),
        `the button ${label}`,
      );
    return { element, context, button };
  }

  it('offers the users and team addresses, and forwards to the one chosen with the message', async () => {
    const { element, context, button } = await sidebar(false);
    const select = must(shadow(element).querySelector('select'), 'the recipients');
    expect([...select.options].map((option) => option.text)).toEqual(['Ana Silva', 'Ben Okafor', 'hr@example.org']);
    // Without viewSensitive, the sidebar has no box for sensitive values.
    expect(shadow(element).querySelector('uui-checkbox')).toBeNull();
    select.value = 'address:hr@example.org';
    select.dispatchEvent(new Event('change'));
    const message = must(shadow(element).querySelector<HTMLTextAreaElement>('uui-textarea'), 'the message');
    message.value = '  Please call her back.  ';
    message.dispatchEvent(new Event('input'));
    await settle(element);
    button('Forward').click();
    await settle(element);
    expect(server.forwards).toEqual([
      { address: 'hr@example.org', message: 'Please call her back.', includeSensitive: false },
    ]);
    expect(context.setValue).toHaveBeenCalledWith({ to: 'hr@example.org' });
    expect(context.submit).toHaveBeenCalled();
  });

  it('sends sensitive values only when the user who may see them ticks the box', async () => {
    const { element, button } = await sidebar(true);
    const box = must(shadow(element).querySelector<HTMLInputElement>('uui-checkbox'), 'the box');
    expect(box.getAttribute('label')).toBe('Include sensitive values');
    button('Forward').click();
    await settle(element);
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await settle(element);
    button('Forward').click();
    await settle(element);
    expect(server.forwards).toEqual([
      { user: 'u1', message: null, includeSensitive: false },
      { user: 'u1', message: null, includeSensitive: true },
    ]);
  });

  it('says when nobody may receive the entry, and when the forward fails', async () => {
    server.recipients = { users: [], addresses: [] };
    const nobody = await sidebar(false);
    expect(text(shadow(nobody.element).querySelector('uui-box p'))).toBe(
      "Nobody else may view this form's entries yet, and Settings \u203a Email names no team address.",
    );
    document.body.replaceChildren();

    server.recipients = { users: [{ key: 'u1', name: 'Ana Silva' }], addresses: [] };
    server.forwardFails = true;
    const failing = await sidebar(false);
    failing.button('Forward').click();
    await settle(failing.element);
    expect(text(shadow(failing.element).querySelector('[role="alert"]'))).toBe(
      "The entry couldn't be forwarded. Try again.",
    );
    expect(failing.context.submit).not.toHaveBeenCalled();
  });
});
