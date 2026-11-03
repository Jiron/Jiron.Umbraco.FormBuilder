// The condition that keeps a menu from a user it would refuse (ADR-0165), and the one answer every item shares.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MyPermissionsResponseModel } from '../api';
import { settle, testHost } from '../testing';
import { forgetMyPermissions, myPermissions, PERMISSION_CONDITION } from './my-permissions';
import { PermissionCondition, type PermissionConditionConfig } from './permission.condition';
import type { Needs } from './sidebar';

const api = vi.hoisted(() => ({ me: vi.fn() }));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  mePermissions: () => api.me(),
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, promise: Promise<unknown>) => promise,
}));

const holds = (permissions: MyPermissionsResponseModel) =>
  api.me.mockImplementation(() => Promise.resolve({ data: permissions }));

/** Whether a condition with this config permits, once the answer has come, and what it said on the way. */
async function permits(needs: readonly Needs[], none = false): Promise<boolean[]> {
  const heard: boolean[] = [];
  const config: PermissionConditionConfig = { alias: PERMISSION_CONDITION, needs, ...(none ? { none } : {}) };
  new PermissionCondition(testHost(), {
    manifest: {} as never,
    config,
    onChange: (permitted: boolean) => heard.push(permitted),
  } as never);
  await settle();
  return heard;
}

describe('the permission condition', () => {
  beforeEach(forgetMyPermissions);

  afterEach(() => {
    document.body.replaceChildren();
    vi.clearAllMocks();
  });

  it('permits a user who holds it globally', async () => {
    holds({ global: ['manageSettings'], anywhere: ['manageSettings'] });
    expect(await permits([{ anyOf: ['manageSettings'] }])).toEqual([true]);
  });

  it('never permits what a user holds on one folder where it takes a global grant', async () => {
    holds({ global: [], anywhere: ['manageSettings'] });
    expect(await permits([{ anyOf: ['manageSettings'] }])).toEqual([]);
  });

  it('permits on one folder what is enough there', async () => {
    holds({ global: [], anywhere: ['viewEntries'] });
    expect(await permits([{ anyOf: ['viewEntries'], anywhere: true }])).toEqual([true]);
  });

  it('turned round, it is for those who hold nothing of it', async () => {
    holds({ global: [], anywhere: [] });
    expect(await permits([{ anyOf: ['design'], anywhere: true }], true)).toEqual([true]);
    forgetMyPermissions();
    holds({ global: [], anywhere: ['design'] });
    expect(await permits([{ anyOf: ['design'], anywhere: true }], true)).toEqual([]);
  });

  it('shows nothing when the answer does not come, either way', async () => {
    api.me.mockImplementation(() => Promise.resolve({ error: new Error('down') }));
    expect(await permits([{ anyOf: ['design'] }])).toEqual([]);
    expect(await permits([{ anyOf: ['design'] }], true)).toEqual([]);
  });
});

describe('what a user holds', () => {
  beforeEach(forgetMyPermissions);

  afterEach(() => {
    document.body.replaceChildren();
    vi.clearAllMocks();
  });

  it('is asked once, however many items ask', async () => {
    holds({ global: ['design'], anywhere: ['design'] });
    const host = testHost();
    const [first, second] = await Promise.all([myPermissions(host), myPermissions(host)]);
    await myPermissions(host);

    expect(api.me).toHaveBeenCalledTimes(1);
    expect(first).toBe(second);
  });

  it('is asked again after a failure', async () => {
    api.me.mockImplementationOnce(() => Promise.resolve({ error: new Error('down') }));
    holds({ global: ['design'], anywhere: ['design'] });
    const host = testHost();

    expect(await myPermissions(host)).toBeUndefined();
    expect((await myPermissions(host))?.global).toEqual(['design']);
  });
});
