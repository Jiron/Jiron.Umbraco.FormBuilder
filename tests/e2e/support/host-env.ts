import { E2E_WEBHOOK_SECRET, STATIC_ORIGIN, STUB_PORT, STUB_URL } from './paths.ts';

/** What one address may send to one form in a minute on the test host; its other requests have no limit there. */
export const E2E_FORM_LIMIT = 200;
import { PEOPLE, RESTRICTED_FOLDERS, ROLES, USER_GROUPS, type E2ESecrets } from './users.ts';

/** Umbraco's setting for the time between two reads of the servers' cache instructions (five seconds unless set). */
export const INSTRUCTION_SYNC = 'Umbraco__CMS__Global__DatabaseServerMessenger__TimeBetweenSyncOperations';

/** Umbraco's setting for the time between two prunings of the old cache instructions (a minute unless set). */
export const INSTRUCTION_PRUNING = 'Umbraco__CMS__Global__DatabaseServerMessenger__TimeBetweenPruneOperations';

/** What a host of one server waits between two reads, and between two prunings: a day, longer than any run. */
export const ONE_SERVER_WAIT = '1.00:00:00';

/** Whether the run's host has the Content Delivery API on: `JFB_E2E_DELIVERY_API=on` or `off` (the default). */
export function deliveryApiOn(env: NodeJS.ProcessEnv = process.env): boolean {
  const value = (env.JFB_E2E_DELIVERY_API ?? 'off').trim().toLowerCase();
  if (value !== 'on' && value !== 'off') throw new Error(`JFB_E2E_DELIVERY_API is "${value}"; use "on" or "off".`);
  return value === 'on';
}

/**
 * The test host's settings and the people it creates, as environment variables: never printed or written to a file.
 * The Testing project turns on what the browser tests need (ADR-0029): the test-only endpoints, the test clock, the
 * stub and the start-up marker.
 */
export function hostEnvironment(
  secrets: E2ESecrets,
  options: { deliveryApi: boolean; severalServers?: boolean },
): Record<string, string> {
  const admin = secrets.users.admin;
  const env: Record<string, string> = {
    Umbraco__CMS__Unattended__UnattendedUserName: admin.name,
    Umbraco__CMS__Unattended__UnattendedUserEmail: admin.email,
    Umbraco__CMS__Unattended__UnattendedUserPassword: admin.password,
    Umbraco__CMS__Imaging__HMACSecretKey: secrets.imagingHmacSecretKey,
    // Several tests sign in as the same user at the same time.
    Umbraco__CMS__Security__AllowConcurrentLogins: 'true',
    Umbraco__CMS__DeliveryApi__Enabled: String(options.deliveryApi),
    // A site of one server has no other server's cache instructions to read, so its host reads them, and prunes
    // the old ones, once a day: not every five seconds and every minute. Both of Umbraco 17's jobs read the
    // instruction table and then write in the same transaction; on SQLite's shared cache that deadlocks with a
    // request that adds an instruction as it ends, Umbraco tries both statements again inside their transactions,
    // and every database call of the host waits (ADR-0253). Several servers keep Umbraco's own intervals, as they
    // tell each other what changed this way.
    ...(options.severalServers ? {} : { [INSTRUCTION_SYNC]: ONE_SERVER_WAIT, [INSTRUCTION_PRUNING]: ONE_SERVER_WAIT }),
    JironFormBuilderTesting__Endpoints: 'true',
    JironFormBuilderTesting__Clock: 'true',
    JironFormBuilderTesting__StubPort: String(STUB_PORT),
    JironFormBuilderTesting__StartupMarker: 'true',
    // The AI add-on's fake provider, its connection and chat profile chosen in Settings > AI (ROADMAP 15.3, ADR-0018).
    JironFormBuilderTesting__FakeAI: 'Chosen',
    // The extension samples the backoffice specs show (ROADMAP 15.1, ADR-0234): a site's own setting editor, field
    // preview and workspace view.
    Demo__Samples__0: 'colour',
    Demo__Samples__1: 'embedView',
    // The webhook action calls the stub (ROADMAP 8.4).
    JironFormBuilder__Webhooks__AllowedHosts__0: `127.0.0.1:${STUB_PORT}`,
    JironFormBuilder__Webhooks__AllowHttp: 'true',
    JironFormBuilder__Secrets__E2EWebhook: E2E_WEBHOOK_SECRET,
    // A Slack and a Teams address of the host's own, which the stub answers (ROADMAP 10.2d).
    JironFormBuilder__Secrets__E2ESlack: `${STUB_URL}/webhooks/slack`,
    JironFormBuilder__Secrets__E2ETeams: `${STUB_URL}/webhooks/teams`,
    // The static page's origin may call the public API (ROADMAP 6.6f), as a headless site's would.
    JironFormBuilder__PublicApi__AllowedOrigins__0: STATIC_ORIGIN,
  };

  // The suite asks far more of one address than any visitor does; the limits project uses up one form's sends. Only
  // that limit stays: each visit of a form page sends the insights beacon, and when a site's share holds many of the
  // visitor's specs on the form specimen at once (keyboard-pass, focus-and-landing and others), the one address went
  // over 200 beacons a minute for that form, the browser logged the 429 and runtime-frame's console check failed (the
  // same with eight workers on one site).
  for (const policy of ['Read', 'Challenge', 'Submit', 'Upload', 'Events', 'Verify']) {
    env[`JironFormBuilder__Limits__${policy}__PerAddressPerMinute`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerAddressPerHour`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerFormPerMinute`] = '0';
    env[`JironFormBuilder__Limits__${policy}__SitePerSecond`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerAddressAndFormPerMinute`] =
      policy === 'Submit' ? String(E2E_FORM_LIMIT) : '0';
  }

  USER_GROUPS.forEach((group, g) => {
    env[`JironFormBuilderTesting__UserGroups__${g}__Alias`] = group.alias;
    env[`JironFormBuilderTesting__UserGroups__${g}__Name`] = group.name;
    group.sections.forEach(
      (section, s) => (env[`JironFormBuilderTesting__UserGroups__${g}__Sections__${s}`] = section),
    );
    group.permissions.forEach(
      (permission, p) => (env[`JironFormBuilderTesting__UserGroups__${g}__Permissions__${p}`] = permission),
    );
    if ('mediaRootAccess' in group && group.mediaRootAccess)
      env[`JironFormBuilderTesting__UserGroups__${g}__MediaRootAccess`] = 'true';
    if ('contentRootAccess' in group && group.contentRootAccess)
      env[`JironFormBuilderTesting__UserGroups__${g}__ContentRootAccess`] = 'true';
    if ('contentEditing' in group && group.contentEditing)
      env[`JironFormBuilderTesting__UserGroups__${g}__ContentEditing`] = 'true';
  });

  RESTRICTED_FOLDERS.forEach((folder, f) => {
    env[`JironFormBuilderTesting__RestrictedFolders__${f}__Name`] = folder.name;
    env[`JironFormBuilderTesting__RestrictedFolders__${f}__Group`] = folder.group;
    folder.permissions.forEach(
      (permission, p) => (env[`JironFormBuilderTesting__RestrictedFolders__${f}__Permissions__${p}`] = permission),
    );
  });

  // The admin is the unattended install's user; the Testing project's seeder creates the others.
  ROLES.filter((role) => role !== 'admin').forEach((role, u) => {
    const user = secrets.users[role];
    env[`JironFormBuilderTesting__Users__${u}__Email`] = user.email;
    env[`JironFormBuilderTesting__Users__${u}__Name`] = user.name;
    env[`JironFormBuilderTesting__Users__${u}__Password`] = user.password;
    PEOPLE[role].groups.forEach((group, i) => (env[`JironFormBuilderTesting__Users__${u}__Groups__${i}`] = group));
    const language = PEOPLE[role].language;
    if (language) env[`JironFormBuilderTesting__Users__${u}__Language`] = language;
  });

  // The demo's own seeder creates the members, in its member group, with access to the members area.
  secrets.members.forEach((member, m) => {
    env[`Demo__Members__${m}__Name`] = member.name;
    env[`Demo__Members__${m}__Email`] = member.email;
    env[`Demo__Members__${m}__Password`] = member.password;
  });
  return env;
}
