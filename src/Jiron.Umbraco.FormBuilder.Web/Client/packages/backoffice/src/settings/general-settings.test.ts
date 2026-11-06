// Settings > General (ROADMAP 10.3b, IA 8, ADR-0198): it shows how long a form's History keeps draft saves, each number
// with its help and unit; Save sends both with the version read and says so; a value out of range is said under its box
// and nothing is sent; the server's refusal of a field is said there too; a save made meanwhile is said, with the values
// now saved; a section the host locks names the key, reads only, and has no Save; a read that fails offers Try again; and
// a user who may not manage settings is told so.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { GeneralSettingsResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

/** What the API answers: the settings read, and each save's answer, in turn. */
const api = vi.hoisted(() => ({
  read: [] as unknown[],
  saves: [] as unknown[],
  sent: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  generalSettingsGet: async () => api.read.shift(),
  generalSettingsSave: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
}));

const { GeneralSettingsElement } = await import('./general-settings.element');
const { saveGeneral } = await import('./general-settings');

const settings = (more: Partial<GeneralSettingsResponseModel> = {}): GeneralSettingsResponseModel => ({
  version: 3,
  history: { draftDays: 90, draftCount: 50 },
  historyLimits: { minDraftDays: 1, maxDraftDays: 3650, maxDraftCount: 1000 },
  locks: [],
  ...more,
});

const refusal = (code: string, extra: Record<string, unknown> = {}) => ({
  error: new UmbApiError('refused', code === 'validation_failed' ? 400 : 409, null, { code, ...extra } as never),
});

async function screenOf(...reads: unknown[]) {
  api.read = reads;
  api.saves = [];
  api.sent = [];
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const screen = new GeneralSettingsElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('uui-box')).not.toBeNull());
  await settle(screen);
  const root = shadow(screen);
  const input = (id: string) => must(root.querySelector<HTMLInputElement>(`#${id}`), id);
  const type = (id: string, value: string) => {
    input(id).value = value;
    input(id).dispatchEvent(new Event('input'));
  };
  const save = () => root.querySelector<HTMLElement>('uui-button[slot="actions"]');
  return { screen, root, input, type, save, peek };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('Settings \u203a General', () => {
  it('shows how long draft saves stay, each number with its help and unit, and no title of its own', async () => {
    const { root, input, save } = await screenOf({ data: settings() });
    expect(root.querySelector('uui-box')?.getAttribute('headline')).toBe('History');
    // The screen's first heading, in order after the sidebar's.
    expect(root.querySelector('uui-box')?.getAttribute('headline-variant')).toBe('h2');
    expect(text(root.querySelector('.lead'))).toMatch(/^How long each form's History keeps its draft saves\./);
    expect([input('draftDays').value, input('draftCount').value]).toEqual(['90', '50']);
    expect([input('draftDays').min, input('draftDays').max, input('draftCount').min, input('draftCount').max]).toEqual([
      '1',
      '3650',
      '0',
      '1000',
    ]);
    expect(text(root.querySelector('label[for="draftDays"]'))).toBe('Keep draft saves for');
    expect(input('draftDays').getAttribute('aria-describedby')).toBe('draftDays-unit draftDays-help');
    expect(text(root.querySelector('#draftDays-help'))).toBe('Every draft save stays at least this long.');
    expect(text(input('draftCount').nextElementSibling)).toBe('draft saves');
    expect(root.querySelector('h1, h2')).toBeNull();
    expect(save()?.getAttribute('label')).toBe('Save');
  });

  it('saves both numbers with the version read, and says so', async () => {
    const { type, save, peek, input } = await screenOf({ data: settings() });
    type('draftDays', '30');
    type('draftCount', '5');
    const after = settings({ version: 4, history: { draftDays: 30, draftCount: 5 } });
    api.saves = [{ data: after }];
    save()?.click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
    expect(api.sent).toEqual([{ version: 3, history: { draftDays: 30, draftCount: 5 } }]);
    expect(peek.mock.calls[0]).toEqual(['positive', { data: { message: 'The settings are saved.' } }]);
    expect(input('draftDays').value).toBe('30');
  });

  it('says a value out of range under its box, sends nothing, and puts the focus there', async () => {
    const { type, save, root, input } = await screenOf({ data: settings() });
    type('draftDays', '0');
    type('draftCount', '2.5');
    save()?.click();
    await vi.waitFor(() => expect(root.querySelector('#draftDays-why')).not.toBeNull());
    expect(api.sent).toEqual([]);
    expect(text(root.querySelector('#draftDays-why'))).toBe('Use a whole number from 1 to 3650.');
    expect(text(root.querySelector('#draftCount-why'))).toBe('Use a whole number from 0 to 1000.');
    expect(input('draftDays').getAttribute('aria-invalid')).toBe('true');
    expect(input('draftDays').getAttribute('aria-describedby')).toBe('draftDays-unit draftDays-help draftDays-why');
    expect(root.activeElement).toBe(input('draftDays'));
  });

  it("says the server's refusal of a field under its box, with the focus there", async () => {
    const { save, root, input } = await screenOf({ data: settings() });
    api.saves = [refusal('validation_failed', { errors: [{ code: 'range', path: 'general.history.draftCount' }] })];
    save()?.click();
    await vi.waitFor(() => expect(root.querySelector('#draftCount-why')).not.toBeNull());
    expect(root.querySelector('#draftDays-why')).toBeNull();
    await vi.waitFor(() => expect(root.activeElement).toBe(input('draftCount')));
  });

  it("says a refusal of a field the screen doesn't show as a save that failed", async () => {
    const { save, root, peek } = await screenOf({ data: settings() });
    api.saves = [refusal('validation_failed', { errors: [{ code: 'unknown', path: 'general.timeZone' }] })];
    save()?.click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
    expect(peek.mock.calls[0]).toEqual([
      'danger',
      { data: { message: "The settings couldn't be saved. Try again in a moment." } },
    ]);
    expect(root.querySelector('.why')).toBeNull();
  });

  it('says a save made meanwhile, and shows the values now saved', async () => {
    const { type, save, root, input } = await screenOf(
      { data: settings() },
      { data: settings({ version: 5, history: { draftDays: 14, draftCount: 9 } }) },
    );
    type('draftDays', '30');
    api.saves = [refusal('concurrency_conflict')];
    save()?.click();
    await vi.waitFor(() => expect(input('draftDays').value).toBe('14'));
    expect(text(root.querySelector('[role="alert"]'))).toMatch(/^Someone else saved these settings meanwhile\./);
  });

  it('names the key of a section the host locks, reads only, and has no Save', async () => {
    const lock = { field: '*', kind: 'locked', configurationKey: 'JironFormBuilder:LockedSections:0' };
    const { root, input, save } = await screenOf({ data: settings({ locks: [lock] }) });
    expect(text(root.querySelector('.locked'))).toBe(
      "The site's configuration sets these settings (JironFormBuilder:LockedSections:0), so they can't be changed here.",
    );
    expect([input('draftDays').readOnly, input('draftCount').readOnly]).toEqual([true, true]);
    expect(save()).toBeNull();
  });

  it('tells a user who may not manage settings so, without Try again, which would not help', async () => {
    const forbidden = new UmbApiError('refused', 403, null, {
      code: 'forbidden',
      permission: 'manageSettings',
    } as never);
    const { root, save } = await screenOf({ error: forbidden });
    expect(text(root.querySelector('[role="alert"]'))).toBe(
      'This needs the "Manage settings" permission. An administrator can grant it in Settings \u203a Access.',
    );
    expect(root.querySelector('[role="alert"] uui-button')).toBeNull();
    expect(save()).toBeNull();
  });

  it('offers Try again for a read that fails', async () => {
    const { root, input } = await screenOf({ data: undefined }, { data: settings() });
    expect(text(root.querySelector('[role="alert"] span'))).toBe("The settings couldn't be read.");
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(input('draftDays').value).toBe('90'));
  });
});

describe('a save of the general settings', () => {
  it('answers each refusal as what the screen says', async () => {
    const answer = async (response: unknown) => {
      api.saves = [response];
      return saveGeneral(testHost() as never, 1, { draftDays: 1, draftCount: 1 });
    };
    expect(await answer(refusal('concurrency_conflict'))).toEqual({ kind: 'conflict' });
    expect(await answer(refusal('setting_locked'))).toEqual({ kind: 'locked' });
    expect(await answer({ error: new Error('offline') })).toEqual({ kind: 'failed' });
    const invalid = await answer(
      refusal('validation_failed', { errors: [{ code: 'range', path: 'general.history.draftDays' }] }),
    );
    expect(invalid.kind === 'invalid' ? [...invalid.fields] : invalid).toEqual(['history.draftDays']);
  });
});
