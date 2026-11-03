import { E2E_WEBHOOK_SECRET, STATIC_ORIGIN, STUB_PORT } from './paths.ts';

/** What one address may ask about one form in a minute on the test host. */
export const E2E_FORM_LIMIT = 200;
import { PEOPLE, RESTRICTED_FOLDERS, ROLES, USER_GROUPS, type E2ESecrets } from './users.ts';

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
export function hostEnvironment(secrets: E2ESecrets, options: { deliveryApi: boolean }): Record<string, string> {
  const admin = secrets.users.admin;
  const env: Record<string, string> = {
    Umbraco__CMS__Unattended__UnattendedUserName: admin.name,
    Umbraco__CMS__Unattended__UnattendedUserEmail: admin.email,
    Umbraco__CMS__Unattended__UnattendedUserPassword: admin.password,
    Umbraco__CMS__Imaging__HMACSecretKey: secrets.imagingHmacSecretKey,
    // Several tests sign in as the same user at the same time.
    Umbraco__CMS__Security__AllowConcurrentLogins: 'true',
    Umbraco__CMS__DeliveryApi__Enabled: String(options.deliveryApi),
    JironFormBuilderTesting__Endpoints: 'true',
    JironFormBuilderTesting__Clock: 'true',
    JironFormBuilderTesting__StubPort: String(STUB_PORT),
    JironFormBuilderTesting__StartupMarker: 'true',
    // The webhook action calls the stub (ROADMAP 8.4).
    JironFormBuilder__Webhooks__AllowedHosts__0: `127.0.0.1:${STUB_PORT}`,
    JironFormBuilder__Webhooks__AllowHttp: 'true',
    JironFormBuilder__Secrets__E2EWebhook: E2E_WEBHOOK_SECRET,
    // The static page's origin may call the public API (ROADMAP 6.6f), as a headless site's would.
    JironFormBuilder__PublicApi__AllowedOrigins__0: STATIC_ORIGIN,
  };

  // The suite asks far more of one address than any visitor does; the limits project uses up one form's.
  for (const policy of ['Read', 'Challenge', 'Submit', 'Upload', 'Events', 'Verify']) {
    env[`JironFormBuilder__Limits__${policy}__PerAddressPerMinute`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerAddressPerHour`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerFormPerMinute`] = '0';
    env[`JironFormBuilder__Limits__${policy}__SitePerSecond`] = '0';
    env[`JironFormBuilder__Limits__${policy}__PerAddressAndFormPerMinute`] = String(E2E_FORM_LIMIT);
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
  });

  // The demo's own seeder creates the members, in its member group, with access to the members area.
  secrets.members.forEach((member, m) => {
    env[`Demo__Members__${m}__Name`] = member.name;
    env[`Demo__Members__${m}__Email`] = member.email;
    env[`Demo__Members__${m}__Password`] = member.password;
  });
  return env;
}
