import { expect, test } from '../../fixtures.ts';
import {
  PEOPLE,
  RESTRICTED_FOLDERS,
  ROLES,
  TWINS,
  USER_GROUPS,
  WITHOUT_FORMS,
  type Role,
} from '../../support/users.ts';

/** What `/jfb-testing/access` says about one backoffice user (ADR-0049). */
interface UserAccess {
  email: string;
  section: boolean;
  global: string[];
  root: string[];
  folders: { name: string; restricted: boolean; permissions: string[] }[];
}

/** The permissions in the order of ADR-0013's table, as the host names them; the first seven work in folders. */
const PERMISSIONS = [
  'design',
  'manageActions',
  'viewEntries',
  'viewSensitive',
  'editEntries',
  'deleteEntries',
  'export',
  'manageLibrary',
  'manageSettings',
  'managePermissions',
  'privacyTools',
] as const;
const FOLDER_LEVEL: readonly string[] = PERMISSIONS.slice(0, 7);

/** The persona's global grants: its groups' (ADR-0013), everything for the admin, nothing without the Forms section. */
function globalOf(role: Role): string[] {
  if (role === 'admin') return [...PERMISSIONS];
  const granted = new Set<string>(
    USER_GROUPS.filter((group) => PEOPLE[role].groups.includes(group.alias)).flatMap((group) => group.permissions),
  );
  return PERMISSIONS.filter((permission) => granted.has(permission));
}

test('every persona holds its grants, globally, at the root and in the restricted folder', async ({ request }) => {
  const response = await request.get('/jfb-testing/access');
  expect(response.status()).toBe(200);
  const users = ((await response.json()) as { users: UserAccess[] }).users;
  const [restricted] = RESTRICTED_FOLDERS;

  for (const role of ROLES.filter((each) => !TWINS.has(each))) {
    const found = users.find((user) => user.email === PEOPLE[role].email);
    expect(found, role).toBeDefined();
    const user = found!;
    const global = globalOf(role);
    const folder = user.folders.find((candidate) => candidate.name === restricted.name);

    expect(user.section, role).toBe(!WITHOUT_FORMS.has(role));
    expect(user.global, role).toEqual(global);
    expect(user.root, role).toEqual(global.filter((permission) => FOLDER_LEVEL.includes(permission)));
    expect(folder?.restricted, role).toBe(true);
    expect(folder?.permissions, role).toEqual(
      role === 'admin' ? FOLDER_LEVEL : role === 'hr' ? [...restricted.permissions] : [],
    );
  }
});
