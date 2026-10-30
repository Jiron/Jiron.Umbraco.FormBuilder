import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { credentialsFile } from './paths.ts';

/**
 * The backoffice personas the tests sign in as (ADR-0013), each with its Jiron Form Builder permissions (ADR-0049); `hr`
 * is the separate group that alone may open the restricted folder.
 */
export type Role = 'admin' | 'designer' | 'entryHandler' | 'viewer' | 'privacyOfficer' | 'noAccess' | 'hr' | 'outsider';

export const ROLES: readonly Role[] = [
  'admin',
  'designer',
  'entryHandler',
  'viewer',
  'privacyOfficer',
  'noAccess',
  'hr',
  'outsider',
];

/** The Forms section's alias (D12). */
export const FORMS_SECTION = 'Jiron.FormBuilder.Section';

/** A backoffice user or a member; the password comes from .local/credentials.json and is never printed. */
export interface Account {
  name: string;
  email: string;
  password: string;
}

/**
 * The user groups the test host creates before the users: one per persona, each with the Forms section and its global
 * permissions (ADR-0013). Designers also get Content, where they place forms on pages (6.7); the others work in the
 * Forms section only.
 */
export const USER_GROUPS = [
  {
    alias: 'jfbDesigners',
    name: 'Form designers',
    sections: ['content', FORMS_SECTION],
    permissions: ['design', 'manageActions', 'manageLibrary'],
  },
  {
    alias: 'jfbEntryHandlers',
    name: 'Entry handlers',
    sections: [FORMS_SECTION],
    permissions: ['viewEntries', 'editEntries', 'export'],
  },
  { alias: 'jfbViewers', name: 'Entry viewers', sections: [FORMS_SECTION], permissions: ['viewEntries'] },
  {
    alias: 'jfbPrivacyOfficers',
    name: 'Privacy officers',
    sections: [FORMS_SECTION],
    permissions: ['privacyTools', 'viewEntries', 'viewSensitive', 'deleteEntries'],
  },
  { alias: 'jfbNoAccess', name: 'Forms without grants', sections: [FORMS_SECTION], permissions: [] },
  { alias: 'jfbHrTeam', name: 'HR team', sections: [FORMS_SECTION], permissions: [] },
] as const;

/** The restricted folder at the root that the test host seeds, granted to the HR team only (ADR-0013). */
export const RESTRICTED_FOLDERS = [
  { name: 'HR', group: 'jfbHrTeam', permissions: ['viewEntries', 'editEntries'] },
] as const;

/** Who each persona is and which Umbraco user groups it belongs to. */
export const PEOPLE: Record<Role, { name: string; email: string; groups: readonly string[] }> = {
  // The unattended install's super user, in the groups Umbraco gives it.
  admin: { name: 'Ada Admin', email: 'e2e-admin@jfb.test', groups: ['admin', 'sensitiveData'] },
  designer: { name: 'Dana Designer', email: 'e2e-designer@jfb.test', groups: ['jfbDesigners'] },
  entryHandler: { name: 'Eli Entries', email: 'e2e-entries@jfb.test', groups: ['jfbEntryHandlers'] },
  viewer: { name: 'Vera Viewer', email: 'e2e-viewer@jfb.test', groups: ['jfbViewers'] },
  privacyOfficer: { name: 'Pia Privacy', email: 'e2e-privacy@jfb.test', groups: ['jfbPrivacyOfficers'] },
  // The Forms section and no grants.
  noAccess: { name: 'Nina Nogrant', email: 'e2e-no-access@jfb.test', groups: ['jfbNoAccess'] },
  // The only one besides the admin who may open the restricted folder.
  hr: { name: 'Hana Hiring', email: 'e2e-hr@jfb.test', groups: ['jfbHrTeam'] },
  // A backoffice user without the Forms section: Umbraco's writers have Content only.
  outsider: { name: 'Otto Outsider', email: 'e2e-outsider@jfb.test', groups: ['writer'] },
};

/** The two members, in the demo's member group, who may open the members area. */
export const MEMBERS = [
  { name: 'Mila Member', email: 'e2e-member-1@jfb.test' },
  { name: 'Moritz Member', email: 'e2e-member-2@jfb.test' },
] as const;

/** The test host's generated secrets, kept under "e2e" in .local/credentials.json. */
export interface E2ESecrets {
  imagingHmacSecretKey: string;
  users: Record<Role, Account>;
  members: Account[];
}

/** About 32 random characters plus one of each class, so any password policy accepts it. */
const newPassword = (): string => `${randomBytes(24).toString('base64url')}Aa1!`;

type CredentialsFile = Record<string, unknown> & { e2e?: Partial<E2ESecrets> };

function readCredentials(): CredentialsFile {
  return existsSync(credentialsFile) ? (JSON.parse(readFileSync(credentialsFile, 'utf8')) as CredentialsFile) : {};
}

/**
 * New secrets for this run, written to .local/credentials.json (the demo's entry and every other one are kept as they
 * are). Every run installs a fresh database, so nothing needs the old ones, and a password that reaches a failure
 * artifact (Playwright's traces record typed values) stops working when the run ends. The global set-up calls it.
 */
export function createSecrets(): E2ESecrets {
  const users = {} as Record<Role, Account>;
  for (const role of ROLES)
    users[role] = { name: PEOPLE[role].name, email: PEOPLE[role].email, password: newPassword() };
  const members = MEMBERS.map((member) => ({ ...member, password: newPassword() }));
  const secrets: E2ESecrets = { imagingHmacSecretKey: randomBytes(64).toString('base64'), users, members };

  mkdirSync(dirname(credentialsFile), { recursive: true });
  writeFileSync(credentialsFile, JSON.stringify({ ...readCredentials(), e2e: secrets }, null, 2) + '\n', {
    encoding: 'utf8',
    mode: 0o600,
  });
  return secrets;
}

/** The run's secrets, for a spec that starts a host of its own beside the run's; the global set-up has created them. */
export function readSecrets(): E2ESecrets {
  const found = readCredentials().e2e;
  if (found?.users === undefined || found.members === undefined || found.imagingHmacSecretKey === undefined)
    throw new Error('No E2E secrets in .local/credentials.json; the global set-up creates them.');
  return found as E2ESecrets;
}

/** The account of a persona, for fixtures; the global set-up has created it. */
export function account(role: Role): Account {
  const found = readCredentials().e2e?.users?.[role];
  if (!found) throw new Error(`No E2E account for ${role} in .local/credentials.json; the global set-up creates it.`);
  return found;
}

/** The account of a member (0 or 1), for fixtures; the global set-up has created it. */
export function memberAccount(index: number): Account {
  const found = readCredentials().e2e?.members?.[index];
  if (!found) throw new Error(`No E2E member ${index} in .local/credentials.json; the global set-up creates it.`);
  return found;
}
