// Email me a summary... (ROADMAP 11.1, IA 6.4, ADR-0209): the sidebar offers off, daily and weekly on Mondays, says when
// it goes out in the site's time zone, shows the mail it would send (off shows the daily one), saves the choice, and
// sends a test to the user's own address, against a fake of the server.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EmailPreviewResponseModel, EntryDigestResponseModel } from '../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { loadEntryWords } from './entry-words';

const server = vi.hoisted(() => ({
  choice: undefined as EntryDigestResponseModel | undefined,
  previews: [] as string[],
  saved: [] as string[],
  tests: [] as string[],
  fails: false,
  limited: false,
}));

const mail = (frequency: string): EmailPreviewResponseModel => ({
  from: 'site@jfb.test',
  to: ['ana@jfb.test'],
  cc: [],
  bcc: [],
  replyTo: null,
  subject: frequency === 'weekly' ? 'Summary of entries, Monday to Sunday' : 'Summary of entries, Sunday',
  html: '',
  text: `${frequency} summary`,
  direction: 'ltr',
});

vi.mock('./entries-api', () => ({
  readDigest: async () => (server.fails ? undefined : server.choice),
  previewDigest: async (_host: unknown, frequency: string) => {
    server.previews.push(frequency);
    return mail(frequency);
  },
  saveDigest: async (_host: unknown, frequency: string) => {
    server.saved.push(frequency);
    return !server.fails;
  },
  testDigest: async (_host: unknown, frequency: string) => {
    server.tests.push(frequency);
    if (server.limited) return { refused: 'limited' };
    return server.fails || !server.choice?.address ? { refused: 'failed' } : { to: server.choice.address };
  },
}));

const { placeOf, mailOf } = await import('./entry-digest.modal.element');

/** A modal context as Umbraco's holds the value: the sidebar writes it, then submits. */
function modalContext() {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function open() {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const context = modalContext();
  const element = document.createElement('jfb-entry-digest-modal');
  element.modalContext = context as never;
  host.append(element);
  await vi.waitFor(() => expect(shadow(element).querySelector('uui-radio-group, [role="alert"]')).not.toBeNull());
  await settle(element);
  const root = shadow(element);
  const button = (label: string) =>
    all(element, 'uui-button').find((one) => one.getAttribute('label') === label) ?? null;
  const press = async (label: string) => {
    must(button(label), `the button ${label}`).click();
    await settle(element);
    await settle(element);
  };
  const choose = async (frequency: string) => {
    const group = must(root.querySelector<HTMLInputElement>('uui-radio-group'), 'the choice');
    group.value = frequency;
    group.dispatchEvent(new Event('change'));
    await settle(element);
  };
  return { element, root, context, peek, button, press, choose };
}

beforeAll(async () => {
  registerEnglish();
  await loadEntryWords('en');
});

beforeEach(() => {
  Object.assign(server, {
    choice: { frequency: 'off', sendsAt: '07:00', timeZone: 'Europe/Berlin', address: 'ana@jfb.test' },
    previews: [],
    saved: [],
    tests: [],
    fails: false,
  });
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the summary email', () => {
  it("offers off, daily and weekly, says when it goes out in the site's time zone, and shows the daily mail", async () => {
    const { root } = await open();

    const group = must(root.querySelector<HTMLInputElement>('uui-radio-group'), 'the choice');
    expect(group.value).toBe('off');
    expect([...group.querySelectorAll('uui-radio')].map((one) => one.getAttribute('label'))).toEqual([
      'Off',
      'Daily',
      'Weekly, on Mondays',
    ]);
    expect(text(root.querySelector('legend'))).toBe('How often');
    expect(text(root.querySelector('#when'))).toBe(
      'It goes out at 07:00 Berlin time, the time zone set for all sites, and covers the day or week before.',
    );
    expect(root.querySelector('fieldset')?.getAttribute('aria-describedby')).toBe('when');
    expect(server.previews, 'off shows what turning it on would send').toEqual(['daily']);
    const preview = must(root.querySelector('jfb-email-preview'), 'the preview');
    expect([preview.getAttribute('heading'), preview.hasAttribute('hides-to')]).toEqual(['What it looks like', true]);
    // The preview's own words load with the sidebar; the address is said once, below it.
    await vi.waitFor(() =>
      expect([...shadow(preview).querySelectorAll('.meta dt')].map((term) => text(term))).toEqual(['From', 'Subject']),
    );
    expect(text(root.querySelector('.note'))).toBe(
      'It holds counts and links, never answers, and only the forms you may view. It goes to your own address, ana@jfb.test.',
    );
  });

  it('shows the weekly mail once weekly is chosen, asks no mail twice, and saves the choice', async () => {
    const { choose, press, context } = await open();

    await choose('daily');
    await choose('weekly');
    expect(server.previews).toEqual(['daily', 'weekly']);
    await press('Save');
    expect(server.saved).toEqual(['weekly']);
    expect(context.getValue()).toEqual({ frequency: 'weekly' });
    expect(context.submit).toHaveBeenCalledOnce();
  });

  it('says so when the choice is not saved, and stays open', async () => {
    const { press, root, context } = await open();
    server.fails = true;
    await press('Save');
    expect(text(root.querySelector('[role="alert"]'))).toBe("Your choice couldn't be saved. Try again.");
    expect(context.submit).not.toHaveBeenCalled();
  });

  it('sends a test of the mail it shows to the own address, and says where it went or that it failed', async () => {
    const { choose, press, peek } = await open();
    await choose('weekly');
    await press('Send test email');
    expect(server.tests).toEqual(['weekly']);
    expect(peek).toHaveBeenLastCalledWith('positive', { data: { message: 'Test email sent to ana@jfb.test.' } });

    server.fails = true;
    await press('Send test email');
    expect(peek).toHaveBeenLastCalledWith('danger', {
      data: { message: "The test email couldn't be sent. Try again." },
    });

    // Past the tests a user runs in an hour (16.1's review).
    server.limited = true;
    await press('Send test email');
    expect(peek).toHaveBeenLastCalledWith('danger', {
      data: { message: "You've run as many tests as an hour allows. Try again later." },
    });
    server.limited = false;
  });

  it('names a site on UTC as UTC, and offers no test to a user without an address', async () => {
    server.choice = { frequency: 'daily', sendsAt: '07:00', timeZone: 'Etc/UTC', address: '' };
    const { root, button } = await open();
    expect(text(root.querySelector('#when'))).toBe(
      'It goes out at 07:00 UTC, the time zone set for all sites, and covers the day or week before.',
    );
    expect(text(root.querySelector('.note'))).toBe('Your user has no email address yet, so no summary can go out.');
    expect(button('Send test email')).toBeNull();
    expect(button('Save')).not.toBeNull();
  });

  it('says so when the choice cannot be read, and reads it again on Try again', async () => {
    server.fails = true;
    const { root, press, element } = await open();
    expect(text(root.querySelector('[role="alert"] span'))).toBe("Your choice couldn't be read. Try again.");
    expect(
      must(
        all(element, 'uui-button').find((one) => one.getAttribute('label') === 'Save'),
        'Save',
      ).hasAttribute('disabled'),
    ).toBe(true);

    server.fails = false;
    await press('Try again');
    await vi.waitFor(() => expect(root.querySelector('uui-radio-group')).not.toBeNull());
  });
});

describe('the place a time zone names', () => {
  it('is the last part of its name, with spaces, and nothing for UTC', () => {
    expect(placeOf('Europe/Berlin')).toBe('Berlin');
    expect(placeOf('America/New_York')).toBe('New York');
    expect(placeOf('America/Argentina/Buenos_Aires')).toBe('Buenos Aires');
    expect(['UTC', 'Etc/UTC', 'GMT', 'Etc/GMT'].map(placeOf)).toEqual(['', '', '', '']);
    expect([mailOf('off'), mailOf('daily'), mailOf('weekly')]).toEqual(['daily', 'daily', 'weekly']);
  });
});
