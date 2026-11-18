// Privacy > Retention (ROADMAP 13.1, IA 9, ADR-0222): the site's days per state, the forms with days of their own, and
// what will be deleted per form with its files within the span chosen, counted again with the days as they are typed;
// Save sends the days through the privacy section and a number out of range is said under its box; a privacy officer
// reads it all and changes nothing; Keep for good only where the host allows it, and the key that would otherwise.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { RetentionDryRunResponseModel, RetentionResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({
  read: undefined as unknown,
  counts: [] as unknown[],
  asked: [] as { days: unknown; span: number }[],
  saves: [] as unknown[],
  sent: [] as unknown[],
  global: ['manageSettings', 'privacyTools'] as string[],
  anywhere: ['design', 'viewEntries'] as string[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  retentionGet: async () => ({ data: api.read }),
  retentionDryRun: async (options: { body: { days: unknown; span: number } }) => {
    api.asked.push(options.body);
    return { data: api.counts.length > 1 ? api.counts.shift() : api.counts[0] };
  },
  settingsSave: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
}));

vi.mock('../section/my-permissions', () => ({
  myPermissions: async () => ({ global: api.global, anywhere: api.anywhere }),
  myPermissionsNow: () => undefined,
}));

const { RetentionScreenElement, COUNT_DELAY_MS } = await import('./retention-screen.element');

const said = (root: ParentNode | null | undefined): string => (root?.textContent ?? '').replace(/\s+/g, ' ').trim();

function model(more: Partial<RetentionResponseModel> = {}): RetentionResponseModel {
  return {
    days: { submitted: 365, approved: 365, rejected: 90, spam: 30 },
    version: 4,
    keepForGood: false,
    keepForGoodKey: 'JironFormBuilder:Privacy:AllowRetentionOff',
    lockedBy: null,
    forms: [
      { key: '11111111-1111-4111-8111-111111111111', name: 'Survey', trashed: false, spam: 7 },
      { key: '22222222-2222-4222-8222-222222222222', name: 'Old contest', trashed: true, storeDays: 60 },
    ],
    ...more,
  };
}

const COUNTED: RetentionDryRunResponseModel = {
  span: 30,
  entries: 3,
  held: 1,
  files: 3,
  forms: [
    { key: '33333333-3333-4333-8333-333333333333', name: 'Contact', trashed: false, entries: 2, held: 1, files: 3 },
    { key: '11111111-1111-4111-8111-111111111111', name: 'Survey', trashed: false, entries: 1, held: 0, files: 0 },
  ],
};

async function screenOf(read: RetentionResponseModel = model(), ...counts: RetentionDryRunResponseModel[]) {
  api.read = read;
  api.counts = counts.length > 0 ? counts : [COUNTED];
  api.asked = [];
  api.saves = [];
  api.sent = [];
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const screen = new RetentionScreenElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('.total')).not.toBeNull());
  await settle(screen);
  const root = shadow(screen);
  const box = (id: string) => must(root.getElementById(id) as HTMLInputElement | HTMLSelectElement | null, id);
  const type = async (id: string, value: string) => {
    const input = box(id);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    await settle(screen);
  };
  const save = () =>
    [...root.querySelectorAll('uui-button')].find((one) => one.getAttribute('label') === 'Save retention settings');
  return { screen, root, box, type, save, peek };
}

beforeAll(registerEnglish);

// The screen's own address, which a leaving is told from.
beforeAll(() => history.replaceState({}, '', '/umbraco/section/jfb-forms/privacy/retention'));

afterEach(() => {
  api.global = ['manageSettings', 'privacyTools'];
  api.anywhere = ['design', 'viewEntries'];
  document.body.replaceChildren();
  localStorage.clear();
});

describe('the retention screen', () => {
  it('draws the days per state, the forms with their own, and what will be deleted with its files', async () => {
    const { root, box } = await screenOf();

    expect(['submitted', 'approved', 'rejected', 'spam'].map((state) => box(`days-${state}`).value)).toEqual([
      '365',
      '365',
      '90',
      '30',
    ]);
    expect(text(root.getElementById('days-spam-unit'))).toBe('days');
    // A help line only where the state's name needs one, each tied to its box; none says a key.
    expect([...root.querySelectorAll('.row .help')].map((help) => help.id)).toEqual([
      'days-submitted-help',
      'days-approved-help',
    ]);
    expect(box('days-rejected').getAttribute('aria-describedby')).toBe('days-rejected-unit');
    expect(root.textContent).not.toMatch(/privacy_retention_/);
    expect([...root.querySelectorAll('jfb-learn-more')].map((link) => link.getAttribute('topic'))).toEqual([
      'retention',
    ]);
    expect([...root.querySelectorAll('uui-box')].map((one) => one.getAttribute('headline'))).toEqual([
      'How long entries are kept',
      'Forms with their own retention',
      'What will be deleted',
    ]);
    expect([...root.querySelectorAll('.own div')].map((row) => said(row))).toEqual([
      'Survey Spam: 7 days',
      'Old contest In the Recycle Bin Every entry: 60 days after it was sent',
    ]);
    expect(root.querySelector('.own a')?.getAttribute('href')).toContain('/view/settings');
    expect(text(root.querySelector('.total'))).toBe('3 entries in 2 forms');
    expect([...root.querySelectorAll('thead th')].map((cell) => text(cell))).toEqual(['Form', 'Entries', 'Files']);
    expect([...root.querySelectorAll('tbody tr')].map((row) => said(row))).toEqual(['Contact 2 3', 'Survey 1 none']);
    expect(root.querySelector('tbody a')?.getAttribute('href')).toContain('/view/entries');
    expect(said(root.querySelector('.footnote'))).toBe(
      '1 more is on legal hold and stays until the hold ends. Retention deletes by itself, every hour.',
    );
    expect(api.asked).toEqual([{ days: { submitted: 365, approved: 365, rejected: 90, spam: 30 }, span: 30 }]);
  });

  it('names the key that would allow Keep for good where the host locks it', async () => {
    const { root } = await screenOf();

    expect(root.querySelector('.for-good')).toBeNull();
    expect(said(root.querySelector('uui-box [role="note"]'))).toBe(
      'Keeping entries for good is locked by configuration: JironFormBuilder:Privacy:AllowRetentionOff',
    );
  });

  it('counts again with the days as typed once typing rests, and says they are not saved yet', async () => {
    const { root, type } = await screenOf(model(), COUNTED, { ...COUNTED, entries: 9, held: 0, files: 0, forms: [] });

    await type('days-spam', '7');
    await vi.waitFor(() => expect(api.asked).toHaveLength(2), { timeout: COUNT_DELAY_MS * 5 });
    await vi.waitFor(() => expect(text(root.querySelector('.total'))).toBe('9 entries in 0 forms'));

    expect(api.asked[1]).toEqual({ days: { submitted: 365, approved: 365, rejected: 90, spam: 7 }, span: 30 });
    expect(said(root.querySelector('.footnote'))).toBe(
      'Counted with the days above, before you save. Entries on legal hold stay. Retention deletes by itself, every hour.',
    );
  });

  it("shows Privacy's tabs with Retention open, and names its Save for what it saves", async () => {
    const { root, save } = await screenOf();
    expect(root.querySelector('umb-body-layout > jfb-place-tabs')?.getAttribute('tab')).toBe('retention');
    expect(save()).toBeDefined();
  });

  it('asks before the backoffice leaves it with days not saved', async () => {
    const leaving = (url = '/umbraco/section/content'): boolean => {
      const event = new CustomEvent('willchangestate', {
        detail: { url },
        cancelable: true,
      });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    };
    const { type } = await screenOf(model(), COUNTED, COUNTED);
    expect(leaving(), 'nothing changed').toBe(false);
    await type('days-spam', '7');
    expect(leaving(), 'days wait to be saved').toBe(true);
    // A tab is a link to its sibling's own address, so leaving by one asks just the same (ADR-0257).
    expect(leaving('/umbraco/section/jfb-forms/workspace/jfb-privacy-people'), 'by a tab').toBe(true);
  });

  it('counts at once for another span, which this browser keeps', async () => {
    const { root, screen } = await screenOf();
    const span = (label: string) =>
      must(
        [...root.querySelectorAll('.spans button')].find((one) => text(one) === label),
        label,
      );
    expect(span('30 days').getAttribute('aria-pressed')).toBe('true');
    expect(must(root.querySelector('.spans'), 'span').getAttribute('aria-label')).toBe('Deleted within');

    (span('90 days') as HTMLElement).click();
    await settle(screen);

    expect(api.asked.at(-1)).toMatchObject({ span: 90 });
    expect([span('30 days'), span('90 days')].map((one) => one.getAttribute('aria-pressed'))).toEqual([
      'false',
      'true',
    ]);
    expect(localStorage.getItem('jfb.privacy.retention.span')).toBe('90');
  });

  it('says a number out of range under its box and sends nothing, then saves the days through the privacy section', async () => {
    const { root, type, save, peek, screen } = await screenOf();
    api.saves = [
      {
        data: {
          section: 'privacy',
          version: 5,
          values: { retention: { submitted: 365, approved: 365, rejected: 90, spam: 14 }, insights: true },
        },
      },
    ];

    await type('days-rejected', '0');
    expect(text(root.getElementById('days-rejected-why'))).toBe('Use a whole number from 1 to 3,650.');
    must(save(), 'Save').click();
    await settle(screen);
    expect(api.sent).toEqual([]);
    expect(text(root.querySelector('.alert'))).toBe('Some settings need another value. Each one says why.');

    await type('days-rejected', '90');
    await type('days-spam', '14');
    must(save(), 'Save').click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalled());

    expect(api.sent).toEqual([
      { version: 4, site: null, values: { retention: { submitted: 365, approved: 365, rejected: 90, spam: 14 } } },
    ]);
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'The settings are saved.' } });
    await settle(screen);
    expect(said(root.querySelector('.footnote'))).not.toContain('before you save');
  });

  it('reads only for a privacy officer, without Save, and links only where the officer may go', async () => {
    api.global = ['privacyTools'];
    api.anywhere = ['viewEntries'];
    const { root, save } = await screenOf();

    expect(root.querySelector('input#days-spam')).toBeNull();
    expect([...root.querySelectorAll('.shown')].map((one) => text(one))).toEqual([
      '365 days',
      '365 days',
      '90 days',
      '30 days',
    ]);
    expect([...root.querySelectorAll('[role="note"]')].map((one) => said(one))).toEqual([
      'Changing retention needs the "Manage settings" permission. An administrator can grant it in Settings \u203a Access.',
    ]);
    expect(root.querySelector('.own a')).toBeNull();
    expect(root.querySelector('tbody a')?.getAttribute('href')).toContain('/view/entries');
    expect(save()).toBeUndefined();
  });

  it('says once that the host locks the section, and has no Save', async () => {
    const { root, save } = await screenOf(model({ lockedBy: 'JironFormBuilder:LockedSections:0' }));

    const notes = [...root.querySelectorAll('[role="note"]')].map((one) => said(one));
    expect(notes).toHaveLength(1);
    expect(notes[0]).toContain('JironFormBuilder:LockedSections:0');
    expect(root.querySelector('input#days-spam')).toBeNull();
    expect(save()).toBeUndefined();
  });

  it('offers Keep for good only where the host allows it, and counts with it', async () => {
    const { root, box, screen } = await screenOf(model({ keepForGood: true }));
    expect(root.querySelector('uui-box [role="note"]')).toBeNull();
    const forGood = must(root.querySelector<HTMLInputElement>('.for-good input'), 'Keep for good');
    forGood.checked = true;
    forGood.dispatchEvent(new Event('change'));
    await settle(screen);

    expect((box('days-submitted') as HTMLInputElement).disabled).toBe(true);
    expect(api.asked.at(-1)).toMatchObject({ days: { submitted: 0 } });
  });
});
