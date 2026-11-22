import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { credentialsFile, CREDENTIALS_KEY } from './paths.ts';

/**
 * The backoffice personas the tests sign in as (ADR-0013), each with its Jiron Form Builder permissions (ADR-0049); `hr`
 * is the separate group that alone may open the restricted folder.
 */
export type Role =
  | 'admin'
  | 'adminDe'
  | 'designer'
  | 'layoutDesigner'
  | 'entryHandler'
  | 'viewer'
  | 'privacyOfficer'
  | 'noAccess'
  | 'hr'
  | 'outsider'
  | 'editor';

export const ROLES: readonly Role[] = [
  'admin',
  'adminDe',
  'designer',
  'layoutDesigner',
  'entryHandler',
  'viewer',
  'privacyOfficer',
  'noAccess',
  'hr',
  'outsider',
  'editor',
];

/**
 * The personas who are another persona in another language: the walks sign in as them to see what the server writes in
 * the user's own language (ROADMAP 16.6). The checks of each persona's rights leave them out, as their twin's cover them.
 */
export const TWINS: ReadonlySet<Role> = new Set<Role>(['adminDe']);

/** The personas without the Forms section: Umbraco's own groups only. */
export const WITHOUT_FORMS: ReadonlySet<Role> = new Set<Role>(['outsider', 'editor']);

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
 * permissions (ADR-0013). Designers also get Content, where they place forms on pages (6.7), the whole media library,
 * which a card's image is picked from (9.4b), and the whole content tree, which a field's options are picked from
 * (9.4c), with Umbraco's create, update and publish there, which a content action they set up needs (ADR-0235); the
 * others work in the Forms section only.
 */
export const USER_GROUPS = [
  {
    alias: 'jfbDesigners',
    name: 'Form designers',
    sections: ['content', FORMS_SECTION],
    permissions: ['design', 'manageActions', 'manageLibrary'],
    mediaRootAccess: true,
    contentRootAccess: true,
    contentEditing: true,
  },
  // Designs forms but may not change their actions: the Actions view is read-only for it (IA 5.3, ADR-0188).
  { alias: 'jfbLayoutDesigners', name: 'Layout designers', sections: [FORMS_SECTION], permissions: ['design'] },
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

/** Who each persona is, which Umbraco user groups it belongs to, and the backoffice language its account keeps. */
export const PEOPLE: Record<Role, { name: string; email: string; groups: readonly string[]; language?: string }> = {
  // The unattended install's super user, in the groups Umbraco gives it.
  admin: { name: 'Ada Admin', email: 'e2e-admin@jfb.test', groups: ['admin', 'sensitiveData'] },
  // The admin's German twin, whose language the server keeps, so its alerts, digests and sample mails are German.
  adminDe: {
    name: 'Adele Admin',
    email: 'e2e-admin-de@jfb.test',
    groups: ['admin', 'sensitiveData'],
    language: 'de-DE',
  },
  designer: { name: 'Dana Designer', email: 'e2e-designer@jfb.test', groups: ['jfbDesigners'] },
  layoutDesigner: { name: 'Lea Layout', email: 'e2e-layout@jfb.test', groups: ['jfbLayoutDesigners'] },
  entryHandler: { name: 'Eli Entries', email: 'e2e-entries@jfb.test', groups: ['jfbEntryHandlers'] },
  viewer: { name: 'Vera Viewer', email: 'e2e-viewer@jfb.test', groups: ['jfbViewers'] },
  privacyOfficer: { name: 'Pia Privacy', email: 'e2e-privacy@jfb.test', groups: ['jfbPrivacyOfficers'] },
  // The Forms section and no grants.
  noAccess: { name: 'Nina Nogrant', email: 'e2e-no-access@jfb.test', groups: ['jfbNoAccess'] },
  // The only one besides the admin who may open the restricted folder.
  hr: { name: 'Hana Hiring', email: 'e2e-hr@jfb.test', groups: ['jfbHrTeam'] },
  // A backoffice user without the Forms section: Umbraco's writers have Content only.
  outsider: { name: 'Otto Outsider', email: 'e2e-outsider@jfb.test', groups: ['writer'] },
  // Umbraco's editors: Content and Media, publishing, no Forms section; they place forms on pages (6.7h, ADR-0136).
  editor: { name: 'Edda Editor', email: 'e2e-editor@jfb.test', groups: ['editor'] },
};

/** The two members, in the demo's member group, who may open the members area. */
export const MEMBERS = [
  { name: 'Mila Member', email: 'e2e-member-1@jfb.test' },
  { name: 'Moritz Member', email: 'e2e-member-2@jfb.test' },
] as const;

/** The test host's generated secrets, kept under the site's entry in .local/credentials.json ("e2e" for the one site). */
export interface E2ESecrets {
  imagingHmacSecretKey: string;
  users: Record<Role, Account>;
  members: Account[];
}

/** About 32 random characters plus one of each class, so any password policy accepts it. */
const newPassword = (): string => `${randomBytes(24).toString('base64url')}Aa1!`;

type CredentialsFile = Record<string, unknown>;

function readCredentials(): CredentialsFile {
  return existsSync(credentialsFile) ? (JSON.parse(readFileSync(credentialsFile, 'utf8')) as CredentialsFile) : {};
}

/** This site's secrets in the credentials file, as far as they are there. */
function siteSecrets(): Partial<E2ESecrets> | undefined {
  return readCredentials()[CREDENTIALS_KEY] as Partial<E2ESecrets> | undefined;
}

/** Blocks this thread for a moment: the credentials file is written in the global set-up, before any test. */
const pause = (ms: number): void => {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
};

/**
 * Changes the credentials file one process at a time: the sites of a run start side by side and each writes its own
 * entry, so each reads the file, changes its entry and puts the whole file back while it holds a lock directory. The
 * new file replaces the old one in one step, so a test that reads an account never meets half a file.
 */
export function updateCredentials(change: (all: CredentialsFile) => CredentialsFile, file = credentialsFile): void {
  mkdirSync(dirname(file), { recursive: true });
  const lock = `${file}.lock`;
  const deadline = Date.now() + 30_000;
  for (;;) {
    try {
      mkdirSync(lock);
      break;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
      let age: number;
      try {
        age = Date.now() - statSync(lock).mtimeMs;
      } catch {
        continue;
      }
      // A lock this old was left by a process that ended while it held it; writing the file takes milliseconds.
      if (age > 10_000) rmSync(lock, { recursive: true, force: true });
      else if (Date.now() > deadline) throw new Error(`Waited 30 s for the lock ${lock}.`, { cause: error });
      else pause(50);
    }
  }
  try {
    const all = existsSync(file) ? (JSON.parse(readFileSync(file, 'utf8')) as CredentialsFile) : {};
    const next = `${file}.${process.pid}.tmp`;
    writeFileSync(next, JSON.stringify(change(all), null, 2) + '\n', { encoding: 'utf8', mode: 0o600 });
    // Windows refuses to replace a file that another process has open at that moment; it passes.
    for (let attempt = 0; ; attempt++) {
      try {
        renameSync(next, file);
        break;
      } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        if (attempt >= 100 || (code !== 'EPERM' && code !== 'EBUSY' && code !== 'EACCES')) throw error;
        pause(50);
      }
    }
  } finally {
    rmSync(lock, { recursive: true, force: true });
  }
}

/**
 * New secrets for this run, written to this site's entry of .local/credentials.json ("e2e" for the one site; the
 * demo's entry, the other sites' and every other one are kept as they are). Every run installs a fresh database, so
 * nothing needs the old ones, and a password that reaches a failure artifact (Playwright's traces record typed values)
 * stops working when the run ends. The global set-up calls it.
 */
export function createSecrets(): E2ESecrets {
  const users = {} as Record<Role, Account>;
  for (const role of ROLES)
    users[role] = { name: PEOPLE[role].name, email: PEOPLE[role].email, password: newPassword() };
  const members = MEMBERS.map((member) => ({ ...member, password: newPassword() }));
  const secrets: E2ESecrets = { imagingHmacSecretKey: randomBytes(64).toString('base64'), users, members };

  updateCredentials((all) => ({ ...all, [CREDENTIALS_KEY]: secrets }));
  return secrets;
}

/** The run's secrets, for a spec that starts a host of its own beside the run's; the global set-up has created them. */
export function readSecrets(): E2ESecrets {
  const found = siteSecrets();
  if (found?.users === undefined || found.members === undefined || found.imagingHmacSecretKey === undefined)
    throw new Error(
      `No E2E secrets under "${CREDENTIALS_KEY}" in .local/credentials.json; the global set-up creates them.`,
    );
  return found as E2ESecrets;
}

/** The account of a persona, for fixtures; the global set-up has created it. */
export function account(role: Role): Account {
  const found = siteSecrets()?.users?.[role];
  if (!found) throw new Error(`No E2E account for ${role} in .local/credentials.json; the global set-up creates it.`);
  return found;
}

/** The account of a member (0 or 1), for fixtures; the global set-up has created it. */
export function memberAccount(index: number): Account {
  const found = siteSecrets()?.members?.[index];
  if (!found) throw new Error(`No E2E member ${index} in .local/credentials.json; the global set-up creates it.`);
  return found;
}
