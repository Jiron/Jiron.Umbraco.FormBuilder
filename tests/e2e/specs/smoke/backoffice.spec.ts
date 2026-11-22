import { expect, test } from '../../fixtures.ts';
import { FORMS_SECTION, PEOPLE, ROLES, TWINS, USER_GROUPS, WITHOUT_FORMS } from '../../support/users.ts';

const MANAGEMENT_API = '/umbraco/management/api/v1';

interface CurrentUser {
  email: string;
  isAdmin: boolean;
  languageIsoCode: string | null;
  userGroupIds: { id: string }[];
  allowedSections: string[];
}

interface UserGroupPage {
  items: { id: string; alias: string }[];
}

/** The sections a persona's groups open; the admin's come from Umbraco, so only its Settings section is checked. */
const sectionsOf = (role: (typeof ROLES)[number]): Set<string> =>
  new Set(USER_GROUPS.filter((group) => PEOPLE[role].groups.includes(group.alias)).flatMap((group) => group.sections));

for (const role of ROLES.filter((each) => !TWINS.has(each))) {
  test(`${role} signs in through the backoffice login screen`, async ({ page, loginAs }) => {
    // The top bar draws a tab for each section as its package's manifests arrive, Umbraco's before the Form Builder's,
    // whose bundle registers its section together with its words: once the words are asked for, the section is known,
    // so a tab it must not draw is not drawn later. The tabs it must draw are waited for, then all are read.
    const registered = page.waitForResponse((response) =>
      /\/App_Plugins\/JironFormBuilder\/chunks\/en-[^/]+\.js$/.test(new URL(response.url()).pathname),
    );
    await loginAs(role);
    await registered;

    await expect(page.locator('umb-current-user-header-app')).toBeVisible();
    const header = page.locator('umb-backoffice-header-sections');
    const drawn = [
      ...(role === 'admin' ? ['Settings'] : []),
      ...(WITHOUT_FORMS.has(role) ? [] : ['Form Builder']),
      ...(role !== 'admin' && (WITHOUT_FORMS.has(role) || sectionsOf(role).has('content')) ? ['Content'] : []),
    ];
    for (const label of drawn) await expect(header.locator(`uui-tab[label="${label}"]`)).toBeVisible();
    const labels = await header.locator('uui-tab').evaluateAll((tabs) => tabs.map((tab) => tab.getAttribute('label')));
    // Only the admin group opens Settings in a fresh install; Content shows for the personas whose groups open it.
    expect(labels.includes('Settings'), 'Settings').toBe(role === 'admin');
    // The section is "Form Builder" in the top bar for every persona that has it (ADR-0171).
    expect(labels.includes('Form Builder'), 'Form Builder').toBe(!WITHOUT_FORMS.has(role));
    if (role !== 'admin')
      expect(labels.includes('Content'), 'Content').toBe(WITHOUT_FORMS.has(role) || sectionsOf(role).has('content'));
  });
}

test('every persona signs in through the Management API with exactly its groups and sections', async ({ apiAs }) => {
  const admin = await apiAs('admin');
  const groups = await admin.get(`${MANAGEMENT_API}/user-group`, { params: { skip: 0, take: 100 } });
  expect(groups.status()).toBe(200);
  const aliasById = new Map(((await groups.json()) as UserGroupPage).items.map((group) => [group.id, group.alias]));

  for (const role of ROLES.filter((each) => !TWINS.has(each))) {
    const api = role === 'admin' ? admin : await apiAs(role);
    const response = await api.get(`${MANAGEMENT_API}/user/current`);
    expect(response.status(), role).toBe(200);
    const user = (await response.json()) as CurrentUser;

    expect(user.email, role).toBe(PEOPLE[role].email);
    expect(user.isAdmin, role).toBe(role === 'admin');
    expect(user.userGroupIds.map((group) => aliasById.get(group.id)).sort(), role).toEqual(
      [...PEOPLE[role].groups].sort(),
    );
    // Every persona but those in Umbraco's own groups only has the Forms section; access.spec.ts checks what each may
    // do there.
    if (role !== 'admin') expect(user.allowedSections.includes(FORMS_SECTION), role).toBe(!WITHOUT_FORMS.has(role));
  }
});

test("each persona's twin is that persona in its own language, kept on the server", async ({ apiAs }) => {
  // ROADMAP 16.6: the walks sign in as a twin to see what the server writes in the user's language.
  for (const twin of TWINS) {
    const user = (await (await (await apiAs(twin)).get(`${MANAGEMENT_API}/user/current`)).json()) as CurrentUser;
    expect(user.email, twin).toBe(PEOPLE[twin].email);
    expect(user.languageIsoCode, twin).toBe(PEOPLE[twin].language);
    expect(user.isAdmin, twin).toBe(PEOPLE[twin].groups.includes('admin'));
  }
});

test('the Management API refuses a caller that has not signed in', async ({ request }) => {
  const response = await request.get(`${MANAGEMENT_API}/user/current`);

  expect(response.status()).toBe(401);
});
