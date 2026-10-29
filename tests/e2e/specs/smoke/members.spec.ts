import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, pageOf, textOf } from '../../support/demo-content.ts';
import { MEMBERS } from '../../support/users.ts';

// The two members the global set-up creates sign in to the members area, each in one language (ROADMAP 3.3, 3.4c).
LANGUAGES.forEach((language, index) => {
  test(`member ${index + 1} signs in to the members area in /${language}/`, async ({ page, memberOf }) => {
    const member = memberOf(index);
    expect(member.email).toBe(MEMBERS[index]?.email);

    await page.goto(`/${language}/`);
    await page
      .getByRole('navigation')
      .getByRole('link', { name: pageOf('demoMembers', language).name })
      .click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(pageOf('demoLogin', language).heading);
    // The sign-in form is the page's own; the footer's newsletter asks for an email address too.
    const main = page.getByRole('main');
    await main.getByLabel(textOf('Demo.Login.Email', language)).fill(member.email);
    await main.getByLabel(textOf('Demo.Login.Password', language), { exact: true }).fill(member.password);
    await main.getByRole('button', { name: textOf('Demo.Login.Submit', language) }).click();

    await expect(page.getByRole('heading', { level: 1 })).toHaveText(pageOf('demoMembers', language).heading);
    await expect(page.locator('.site-account strong')).toHaveText(member.name);
  });
});
