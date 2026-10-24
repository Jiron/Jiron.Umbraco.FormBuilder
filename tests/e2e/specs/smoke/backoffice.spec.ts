import { expect, test } from '../../fixtures.ts';
import { FORMS_SECTION, PEOPLE, ROLES, USER_GROUPS } from '../../support/users.ts';

const MANAGEMENT_API = '/umbraco/management/api/v1';

interface CurrentUser {
  email: string;
  isAdmin: boolean;
  userGroupIds: { id: string }[];
  allowedSections: string[];
}

interface UserGroupPage {
  items: { id: string; alias: string }[];
}

/** The sections a persona's groups open; the admin's come from Umbraco, so only its Settings section is checked. */
const sectionsOf = (role: (typeof ROLES)[number]): Set<string> =>
  new Set(USER_GROUPS.filter((group) => PEOPLE[role].groups.includes(group.alias)).flatMap((group) => group.sections));

for (const role of ROLES) {
  test(`${role} signs in through the backoffice login screen`, async ({ page, loginAs }) => {
    await loginAs(role);

    await expect(page.locator('umb-current-user-header-app')).toBeVisible();
    const labels = await page
      .locator('umb-backoffice-header-sections uui-tab')
      .evaluateAll((tabs) => tabs.map((tab) => tab.getAttribute('label')));
    // Only the admin group opens Settings in a fresh install; Content shows for the personas whose groups open it.
    expect(labels.includes('Settings'), 'Settings').toBe(role === 'admin');
    if (role !== 'admin')
      expect(labels.includes('Content'), 'Content').toBe(role === 'outsider' || sectionsOf(role).has('content'));
  });
}

test('every persona signs in through the Management API with exactly its groups and sections', async ({ apiAs }) => {
  const admin = await apiAs('admin');
  const groups = await admin.get(`${MANAGEMENT_API}/user-group`, { params: { skip: 0, take: 100 } });
  expect(groups.status()).toBe(200);
  const aliasById = new Map(((await groups.json()) as UserGroupPage).items.map((group) => [group.id, group.alias]));

  for (const role of ROLES) {
    const api = role === 'admin' ? admin : await apiAs(role);
    const response = await api.get(`${MANAGEMENT_API}/user/current`);
    expect(response.status(), role).toBe(200);
    const user = (await response.json()) as CurrentUser;

    expect(user.email, role).toBe(PEOPLE[role].email);
    expect(user.isAdmin, role).toBe(role === 'admin');
    expect(user.userGroupIds.map((group) => aliasById.get(group.id)).sort(), role).toEqual(
      [...PEOPLE[role].groups].sort(),
    );
    // Every persona but the outsider has the Forms section; access.spec.ts checks what each may do there.
    if (role !== 'admin') expect(user.allowedSections.includes(FORMS_SECTION), role).toBe(role !== 'outsider');
  }
});

test('the Management API refuses a caller that has not signed in', async ({ request }) => {
  const response = await request.get(`${MANAGEMENT_API}/user/current`);

  expect(response.status()).toBe(401);
});
