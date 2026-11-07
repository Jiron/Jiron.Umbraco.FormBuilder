// Privacy > Data collection (ROADMAP 11.5, IA 9, ADR-0211): it shows the site's insights switch with what is counted and
// what never is; Save sends the switch with the version read and says so; a save made meanwhile is said, with the value
// now saved; a section the host locks names the key, keeps the switch as it is, and has no Save; a read that fails offers
// Try again; and a user who may not manage settings is told so.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { PrivacySettingsResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({ read: [] as unknown[], saves: [] as unknown[], sent: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  privacySettingsGet: async () => api.read.shift(),
  privacySettingsSave: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
}));

const { PrivacySettingsElement } = await import('./privacy-settings.element');

const settings = (more: Partial<PrivacySettingsResponseModel> = {}): PrivacySettingsResponseModel => ({
  version: 2,
  insights: true,
  locks: [],
  ...more,
});

const refusal = (code: string, status: number) => ({
  error: new UmbApiError('refused', status, null, { code } as never),
});

async function screenOf(...reads: unknown[]) {
  api.read = reads;
  api.saves = [];
  api.sent = [];
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const screen = new PrivacySettingsElement();
  host.append(screen);
  await vi.waitFor(() =>
    expect(shadow(screen).querySelector('uui-box #insights, uui-box [role="alert"], uui-box .lead')).not.toBeNull(),
  );
  await settle(screen);
  const root = shadow(screen);
  const toggle = () => must(root.querySelector<HTMLInputElement>('#insights'), 'the switch');
  const save = () => root.querySelector<HTMLElement>('uui-button[slot="actions"]');
  return { screen, root, toggle, save, peek };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('Privacy \u203a Data collection', () => {
  it('shows the switch with what is counted, and Save sends it with the version read', async () => {
    const { screen, root, toggle, save, peek } = await screenOf({ data: settings() });
    expect(root.querySelector('uui-box')?.getAttribute('headline')).toBe('Insights');
    expect(text(root.querySelector('.lead'))).toMatch(/No answer and no visitor is counted/);
    expect(toggle().checked).toBe(true);
    expect(toggle().getAttribute('role')).toBe('switch');
    expect(text(root.querySelector('label[for="insights"]'))).toBe('Count what visitors do with the forms');
    expect(text(root.querySelector('#insights-help'))).toBe(
      'Each form can turn it off in its Settings view, under Privacy.',
    );

    api.saves = [{ data: settings({ version: 3, insights: false }) }];
    toggle().checked = false;
    toggle().dispatchEvent(new Event('change'));
    must(save(), 'Save').click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalled());
    await settle(screen);
    expect(api.sent).toEqual([{ version: 2, insights: false }]);
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'The settings are saved.' } });
    expect(toggle().checked).toBe(false);
  });

  it('says a save made meanwhile, with the value now saved', async () => {
    const { screen, root, toggle, save } = await screenOf(
      { data: settings() },
      { data: settings({ version: 5, insights: false }) },
    );
    api.saves = [refusal('concurrency_conflict', 409)];
    toggle().checked = false;
    toggle().dispatchEvent(new Event('change'));
    must(save(), 'Save').click();
    await vi.waitFor(() => expect(root.querySelector('.alert')).not.toBeNull());
    await settle(screen);
    expect(text(root.querySelector('.alert'))).toMatch(/^Someone else saved these settings meanwhile\./);
    expect(toggle().checked).toBe(false);
  });

  it('names the key of a section the host locks, keeps the switch, and has no Save', async () => {
    const { root, toggle, save } = await screenOf({
      data: settings({
        locks: [{ field: '*', kind: 'locked', configurationKey: 'JironFormBuilder:LockedSections:0' }],
      }),
    });
    expect(text(root.querySelector('.locked'))).toContain('JironFormBuilder:LockedSections:0');
    expect(toggle().disabled).toBe(true);
    expect(save()).toBeNull();
  });

  it('offers Try again after a read that failed, and tells a user who may not manage settings so', async () => {
    const failed = await screenOf({ data: undefined }, { data: settings() });
    expect(text(failed.root.querySelector('[role="alert"]'))).toMatch(/^The settings couldn't be read\./);
    must(failed.root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(failed.root.querySelector('#insights')).not.toBeNull());

    const forbidden = await screenOf(refusal('forbidden', 403));
    expect(text(forbidden.root.querySelector('[role="alert"]'))).toMatch(/manage settings/i);
    expect(forbidden.save()).toBeNull();
  });
});
