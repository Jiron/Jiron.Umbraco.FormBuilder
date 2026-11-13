// The Overview itself, with the API replaced: what it shows for a fresh install, a complete setup, a user who may not view entries,
// a block that fails, and the migrations pending. Playwright covers it on a real backoffice.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  ChecklistResponseModel,
  HealthResponseModel,
  MyPermissionsResponseModel,
  RecentEntriesResponseModel,
} from '../api';
import { forgetMyPermissions } from '../section/my-permissions';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

type Answer<T> = { data: T } | { error: unknown };

const api = vi.hoisted(() => ({
  checklist: vi.fn(),
  health: vi.fn(),
  recent: vi.fn(),
  me: vi.fn(),
  error503: {} as unknown,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  setupChecklist: () => api.checklist(),
  setupHealth: () => api.health(),
  recentEntryList: () => api.recent(),
  mePermissions: () => api.me(),
  // Forms at a glance has its own tests (insight-glance.test.ts); here it finds no form.
  insightGlance: () => Promise.resolve({ data: { items: [] } }),
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, promise: Promise<unknown>) => promise,
}));

const fresh: ChecklistResponseModel = {
  complete: false,
  items: [
    { step: 'email', done: true, canDo: true },
    { step: 'form', done: false, canDo: true },
    { step: 'testEntry', done: false, canDo: false },
    { step: 'formBlock', done: false, canDo: true },
    { step: 'access', done: false, canDo: true },
  ],
};

const complete: ChecklistResponseModel = {
  complete: true,
  items: fresh.items.map((item) => ({ ...item, done: true })),
};

const answer = <T>(data: T): Promise<Answer<T>> => Promise.resolve({ data });

const everything: MyPermissionsResponseModel = {
  global: ['manageSettings', 'viewEntries'],
  anywhere: ['manageSettings', 'viewEntries', 'design'],
};

async function open() {
  forgetMyPermissions();
  const { OverviewElement } = await import('./overview.element');
  const element = new OverviewElement();
  testHost().append(element);
  await settle(element);
  await settle(element);
  return element;
}

describe('the Overview', () => {
  beforeAll(registerEnglish);

  beforeEach(() => {
    api.checklist.mockImplementation(() => answer(fresh));
    api.health.mockImplementation(() => answer<HealthResponseModel>({ findings: [] }));
    api.recent.mockImplementation(() => answer<RecentEntriesResponseModel>({ items: [] }));
    api.me.mockImplementation(() => answer(everything));
  });

  afterEach(() => {
    document.body.replaceChildren();
    vi.clearAllMocks();
  });

  it('on a fresh install shows the checklist with its count, the action on the next step, and the health line', async () => {
    const element = await open();
    const root = shadow(element);

    expect(text(must(root.querySelector('.count'), 'the count'))).toBe('1 of 5 done');
    const buttons = [...root.querySelectorAll('ol uui-button')].map((button) => button.getAttribute('label'));
    expect(buttons).toEqual(['Choose a template\u2026']);
    expect(text(must(root.querySelector('.passing'), 'the health line'))).toBe('All checks pass');
  });

  it('says "done" once, for a screen reader, and shows a done step without its help', async () => {
    const root = shadow(await open());
    const first = must(root.querySelector('ol li'), 'the first step');

    expect(text(first)).toContain('Check email sending');
    expect(text(must(first.querySelector('.visually-hidden'), 'the hidden word'))).toBe(', done');
    expect(first.querySelector('.step__help')).toBeNull();
  });

  it('makes a later step a link, and the form block a button that opens its dialog', async () => {
    const root = shadow(await open());
    const steps = [...root.querySelectorAll('ol li')];

    expect(steps[2]?.querySelector('a, button.link'), 'the preview waits for a form').toBeNull();
    expect(text(must(steps[2]?.querySelector('.step__help'), 'its help'))).toBe(
      'Once there is a form, see it as visitors will and check what arrives.',
    );
    expect(steps[3]?.querySelector('button.link')?.textContent).toBe('Put the form on a page');
    expect(steps[4]?.querySelector('a')?.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-settings-access');
  });

  it('links the test entry to the form there is, with help that no longer waits for one', async () => {
    const key = '3f2a1c5e-0000-4000-8000-000000000001';
    // A form made: its step is done, and the test entry is the next, its button opening that form.
    const items = fresh.items.map((item) =>
      item.step === 'form' ? { ...item, done: true } : item.step === 'testEntry' ? { ...item, canDo: true } : item,
    );
    api.checklist.mockImplementation(() => answer({ ...fresh, items, previewForm: key }));
    const step = [...shadow(await open()).querySelectorAll('ol li')][2];
    expect(step?.querySelector('uui-button')?.getAttribute('href')).toBe(
      `section/jfb-forms/workspace/jfb-form/edit/${key}`,
    );
    expect(text(must(step?.querySelector('.step__help'), 'its help'))).toBe(
      'See the form as visitors will, send an entry, and check what arrives.',
    );
  });

  it('leaves the checklist away once setup is complete, and shows it on request', async () => {
    api.checklist.mockImplementation(() => answer(complete));
    const element = await open();
    const root = shadow(element);
    expect(root.querySelector('ol')).toBeNull();

    must(root.querySelector('.health uui-menu-item'), 'the request').dispatchEvent(new CustomEvent('click-label'));
    await settle(element);

    expect(root.querySelector('ol')).not.toBeNull();
    expect(root.querySelector('.health umb-dropdown'), 'nothing to ask for any more').toBeNull();
  });

  it('lists findings with the link a user may follow, and who can fix what they may not', async () => {
    api.health.mockImplementation(() =>
      answer<HealthResponseModel>({
        findings: [
          { code: 'emailsFailing', message: "2 emails couldn't be sent.", link: 'deliveries', coversEmail: false },
          { code: 'jobsStopped', message: 'The jobs have stopped.', coversEmail: false },
        ],
      }),
    );
    let root = shadow(await open());
    let findings = [...root.querySelectorAll('.finding')];
    expect(findings.map(text)).toEqual(["2 emails couldn't be sent. Open Deliveries", 'The jobs have stopped.']);
    expect(findings[0]?.querySelector('a')?.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-deliveries');

    document.body.replaceChildren();
    api.me.mockImplementation(() => answer<MyPermissionsResponseModel>({ global: [], anywhere: [] }));
    root = shadow(await open());
    findings = [...root.querySelectorAll('.finding')];
    expect(findings.map(text)[0]).toBe("2 emails couldn't be sent. needs an administrator");
    expect(findings[0]?.querySelector('a')).toBeNull();
  });

  it('links the privacy findings to their screens, Retention for the privacy tools too, as the sidebar opens it', async () => {
    api.health.mockImplementation(() =>
      answer<HealthResponseModel>({
        findings: [
          { code: 'fullIp', message: 'Full IP addresses are kept.', link: 'privacyDataCollection', coversEmail: false },
          {
            code: 'retentionOff',
            message: 'Some entries are kept for good.',
            link: 'privacyRetention',
            coversEmail: false,
          },
          {
            code: 'captchaUnasked',
            message: 'A captcha loads unasked.',
            link: 'settingsProtection',
            coversEmail: false,
          },
        ],
      }),
    );
    let root = shadow(await open());
    const hrefs = () =>
      [...root.querySelectorAll('.finding')].map((one) => one.querySelector('a')?.getAttribute('href'));
    expect(hrefs()).toEqual([
      'section/jfb-forms/workspace/jfb-privacy-collection',
      'section/jfb-forms/workspace/jfb-privacy-retention',
      'section/jfb-forms/workspace/jfb-settings-spam',
    ]);
    expect([...root.querySelectorAll('.finding a')].map(text)).toEqual([
      'Open Data collection',
      'Open Retention',
      'Open spam protection settings',
    ]);

    document.body.replaceChildren();
    api.me.mockImplementation(() => answer<MyPermissionsResponseModel>({ global: ['privacyTools'], anywhere: [] }));
    root = shadow(await open());
    expect(hrefs()).toEqual([undefined, 'section/jfb-forms/workspace/jfb-privacy-retention', undefined]);
    expect([...root.querySelectorAll('.finding .who')]).toHaveLength(2);
  });

  it('asks for recent entries only for someone who may view them', async () => {
    api.me.mockImplementation(() => answer<MyPermissionsResponseModel>({ global: [], anywhere: ['design'] }));
    const root = shadow(await open());

    expect(api.recent).not.toHaveBeenCalled();
    expect(root.querySelector('table')).toBeNull();
  });

  it('names an entry by its title or number, marks it unseen, and links to its panel', async () => {
    api.recent.mockImplementation(() =>
      answer<RecentEntriesResponseModel>({
        items: [
          {
            key: 'e1',
            formKey: 'f',
            formName: 'Contact us',
            title: 'Anna Svensson',
            number: 7,
            submitted: new Date().toISOString(),
            unseen: true,
          },
          {
            key: 'e2',
            formKey: 'f',
            formName: 'Contact us',
            number: 6,
            submitted: new Date().toISOString(),
            unseen: false,
          },
        ],
      }),
    );
    const root = shadow(await open());
    const rows = [...root.querySelectorAll('tbody tr')];

    expect(rows[0]?.querySelector('a')?.textContent).toBe('Anna Svensson');
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-entry/edit/e1');
    expect(rows[0]?.querySelector('.dot')).not.toBeNull();
    expect(rows[1]?.querySelector('a')?.textContent).toBe('Entry #6');
    expect(rows[1]?.querySelector('.dot')).toBeNull();
    // The Inbox from here, as its item may lie below a long forms tree (16.6's review).
    const inbox = must(root.querySelector('uui-button[slot="header-actions"]'), 'Open the Inbox');
    expect([inbox.getAttribute('label'), inbox.getAttribute('href')]).toEqual([
      'Open the Inbox',
      'section/jfb-forms/workspace/jfb-inbox',
    ]);
  });

  it('says so when there are no entries yet', async () => {
    const root = shadow(await open());
    expect(text(must(root.querySelector('uui-box p.muted'), 'the empty state'))).toBe(
      'No entries yet. They arrive here from every form you may view.',
    );
  });

  it('says in place what failed, and tries again from there', async () => {
    api.recent.mockImplementationOnce(() => Promise.resolve({ error: new Error('down') }));
    const element = await open();
    const root = shadow(element);
    const failed = must(root.querySelector('.failed'), 'the failure');
    expect(text(failed)).toContain("Recent entries couldn't be loaded.");
    expect(root.querySelector('.passing'), 'the rest stands').not.toBeNull();

    must(failed.querySelector<HTMLElement>('uui-button'), 'Try again').click();
    await settle(element);
    await settle(element);

    expect(root.querySelector('.failed')).toBeNull();
  });

  it("takes a gateway's 503 for a failure like any other", async () => {
    const { UmbApiError } = await import('@umbraco-cms/backoffice/resources');
    const gateway = new UmbApiError('Service Unavailable', 503, undefined, {} as never);
    api.checklist.mockImplementation(() => Promise.resolve({ error: gateway }));
    const root = shadow(await open());

    expect(root.querySelector('.failed')).not.toBeNull();
    expect(root.querySelector('.passing'), 'the rest stands').not.toBeNull();
  });

  it('says in the email step what is wrong, where Health leaves it out', async () => {
    const open_ = {
      ...fresh,
      items: fresh.items.map((item) => (item.step === 'email' ? { ...item, done: false } : item)),
    };
    api.checklist.mockImplementation(() => answer(open_));
    api.health.mockImplementation(() =>
      answer<HealthResponseModel>({
        findings: [{ code: 'emailNoServer', message: "Emails can't be sent.", coversEmail: true }],
      }),
    );
    const root = shadow(await open());

    expect(text(must(root.querySelector('ol li .step__help'), 'the step'))).toBe("Emails can't be sent.");
    expect(root.querySelector('.finding'), 'said once').toBeNull();
    expect(text(must(root.querySelector('.passing'), 'the line'))).toBe('All checks pass');
  });

  it('says who can fix a finding without a link, and nothing about it until it knows what the user holds', async () => {
    api.health.mockImplementation(() =>
      answer<HealthResponseModel>({
        findings: [{ code: 'jobsStopped', message: 'The jobs have stopped.', coversEmail: false }],
      }),
    );
    api.me.mockImplementation(() => answer<MyPermissionsResponseModel>({ global: [], anywhere: [] }));
    let root = shadow(await open());
    expect(text(must(root.querySelector('.finding'), 'the finding'))).toBe(
      'The jobs have stopped. needs an administrator',
    );

    document.body.replaceChildren();
    api.me.mockImplementation(() => Promise.resolve({ error: new Error('down') }));
    root = shadow(await open());
    expect(text(must(root.querySelector('.finding'), 'the finding'))).toBe('The jobs have stopped.');
  });

  it('says why nothing shows while the migrations are pending, and nothing else', async () => {
    const { UmbApiError } = await import('@umbraco-cms/backoffice/resources');
    const pending = new UmbApiError('Service Unavailable', 503, undefined, { code: 'migrations_pending' } as never);
    api.checklist.mockImplementation(() => Promise.resolve({ error: pending }));
    api.health.mockImplementation(() => Promise.resolve({ error: pending }));
    const root = shadow(await open());

    expect(text(must(root.querySelector('[role="alert"]'), 'the sentence'))).toContain(
      'database migrations are pending',
    );
    expect([...root.querySelectorAll('jfb-learn-more')].map((link) => link.getAttribute('topic'))).toEqual([
      'migrations',
    ]);
    expect(root.querySelector('ol')).toBeNull();
    expect(root.querySelector('.failed')).toBeNull();
  });
});
