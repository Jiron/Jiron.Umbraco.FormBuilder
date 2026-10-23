// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Which browser the repository scripts launch (D19, ADR-0011): Edge on the development machine, through
 * Playwright's `msedge` channel, and Playwright's own Chromium in CI, where `CI` is set and the workflow installs it.
 * The browser tests in tests/e2e choose the same way in their config.
 */

/** The options for `chromium.launch` in this environment. */
export function launchOptions(env = process.env) {
  return env.CI ? {} : { channel: 'msedge' };
}
