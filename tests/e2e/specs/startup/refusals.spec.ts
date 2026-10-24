import { expect, test } from '@playwright/test';
import { runRefusedHost } from '../../support/host.ts';
import { REFUSAL_PORTS } from '../../support/paths.ts';

// A site that Jiron Form Builder cannot run does not start, and its log says what to change (ROADMAP 4.4a, ADR-0042).
// Each test starts the built demo in a process of its own, without a database, and waits until it ends.

test.describe.configure({ mode: 'parallel', timeout: 240_000 });

test('a site with an invalid host configuration does not start, and names every key to fix', async () => {
  const host = await runRefusedHost({
    name: 'refusal-configuration',
    port: REFUSAL_PORTS.configuration,
    env: {
      JironFormBuilder__Limits__Definitions__MaxFields: '0',
      JironFormBuilder__LockedSection__0: 'privacy',
    },
  });

  expect(host.exitCode).not.toBe(0);
  expect(host.log).toContain('JironFormBuilder:Limits:Definitions:MaxFields (0) must be 1 to 3000');
  expect(host.log).toContain('JironFormBuilder:LockedSection is not a setting of Jiron Form Builder');
});

test('a site with invariant globalization does not start, and says what to change', async () => {
  const host = await runRefusedHost({
    name: 'refusal-globalization',
    port: REFUSAL_PORTS.globalization,
    env: { DOTNET_SYSTEM_GLOBALIZATION_INVARIANT: '1' },
  });

  expect(host.exitCode).not.toBe(0);
  // Windows needs ICU for all three; Linux reads IANA time zones from its own files, so only the start is the same.
  const missing =
    process.platform === 'win32' ? 'culture data, Unicode normalisation and IANA time zones,' : 'culture data';
  expect(host.log).toContain(
    `Jiron Form Builder needs .NET globalization with ICU, and this site runs without ${missing}`,
  );
  expect(host.log).toContain('DOTNET_SYSTEM_GLOBALIZATION_INVARIANT to false');
  // The check runs before anything else fails for want of a culture.
  expect(host.log).not.toContain('is an invalid culture identifier');
});
