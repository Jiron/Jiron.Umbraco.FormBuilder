import { describeStall, recordStall, waitForStartupWork } from './support/host-health.ts';
import { buildDemo, ensureClientAssets, logTail, startHost, type Host } from './support/host.ts';
import { deliveryApiOn, hostEnvironment } from './support/host-env.ts';
import { E2E_BASE_URL, E2E_PORT, E2E_SITE, STUB_URL } from './support/paths.ts';
import { isShared, siteLabel } from './support/sites.ts';
import { clearUserLock } from './support/user-lock.ts';
import { createSecrets } from './support/users.ts';

const seconds = (since: number): string => ((Date.now() - since) / 1000).toFixed(0);

/**
 * Starts a fresh demo host on 5520 (or the port of the site this process runs, support/sites.ts) with its own database, mail pickup folder and log, the personas and the two members
 * (ADR-0011, ADR-0031), and waits for Umbraco's start-up work. Returns the teardown, which stops the host and writes
 * the hang marker when its database hung. `JFB_E2E_SKIP_BUILD=1` reuses the current build; `JFB_E2E_DELIVERY_API=on`
 * runs the host with the Content Delivery API.
 */
export default async function globalSetup(): Promise<() => Promise<void>> {
  const secrets = createSecrets();
  clearUserLock();
  if (process.env.JFB_E2E_SKIP_BUILD === '1') {
    console.warn('e2e: JFB_E2E_SKIP_BUILD=1, so the demo and the client assets are used as they were last built.');
  } else {
    ensureClientAssets();
    buildDemo();
  }

  const deliveryApi = deliveryApiOn();
  const started = Date.now();
  const host = await startHost({
    name: `host-${E2E_PORT}`,
    port: E2E_PORT,
    env: hostEnvironment(secrets, { deliveryApi }),
  });
  console.log(
    `e2e: ${E2E_SITE.budgets || isShared(E2E_SITE) ? `${siteLabel(E2E_SITE)}: ` : ''}host ready at ${E2E_BASE_URL} in ${seconds(started)} s, Delivery API ${deliveryApi ? 'on' : 'off'} (log ${host.logFile})`,
  );

  try {
    // Umbraco 17 registers its jobs and rebuilds its indexes about a minute after it starts; the tests start after
    // that (ADR-0011).
    const waited = Date.now();
    const wait = await waitForStartupWork(host.logFile);
    if (wait.state === 'stalled') throw new Error(describeStall(wait.stall, host.logFile));
    if (wait.state === 'misordered')
      throw new Error("The host's start-up marker ran before another index populator (StartupMarkerComposer).");
    if (wait.state === 'timeout')
      throw new Error(
        `The host's start-up work did not finish: the log lacks ${wait.missing.map((line) => `"${line}"`).join(' and ')}.`,
      );
    console.log(`e2e: the host's start-up work is done (waited ${seconds(waited)} s)`);
    await checkTestHost();
  } catch (error) {
    await finish(host);
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${message}\nLast log lines:\n${logTail(host.logFile)}`, { cause: error });
  }

  // Workers start after the global set-up and inherit this.
  process.env.JFB_E2E_MAIL_DIR = host.mailDir;
  return () => finish(host);
}

/** The host is the test host these tests started: the test-only endpoints answer, on the test clock, with the stub. */
async function checkTestHost(): Promise<void> {
  const response = await fetch(`${E2E_BASE_URL}/jfb-testing/status`, { signal: AbortSignal.timeout(10_000) });
  const status = response.ok
    ? ((await response.json()) as { testHost?: boolean; clock?: boolean; stub?: string | null })
    : {};
  if (status.testHost !== true || status.clock !== true || status.stub !== `${STUB_URL}/`) {
    throw new Error(
      `${E2E_BASE_URL} does not answer as the browser tests' host (/jfb-testing/status: HTTP ${response.status}).`,
    );
  }
}

/** Stops the host, and leaves the hang marker when its database hung, so the gate may run the suites once more. */
async function finish(host: Host): Promise<void> {
  await host.stop();
  const stall = recordStall(host.logFile);
  if (stall) console.log(describeStall(stall, host.logFile));
}
