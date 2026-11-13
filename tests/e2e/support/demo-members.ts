import { expect, type Page } from '@playwright/test';
import { pageOf, textOf, type Language } from './demo-content.ts';
import { signInOnceMore } from './sign-in-retry.ts';
import { withUserLock } from './user-lock.ts';

/**
 * Signs a member in through the demo's sign-in page, so a form for members, such as the workshop booking, shows.
 *
 * Umbraco updates the member as it updates a backoffice user who signs in, so member sign-ins run one at a time with
 * the others (support/user-lock.ts), and one that Umbraco's SQLite stalls is signed in once more
 * (support/sign-in-retry.ts, ADR-0250).
 */
export async function signInMember(
  page: Page,
  email: string,
  password: string,
  language: Language = 'en',
): Promise<void> {
  const account = page.getByRole('heading', { level: 1, name: pageOf('demoMembers', language).heading, exact: true });
  await openSignIn(page, language);
  await withUserLock(() =>
    signInOnceMore({
      who: email,
      attempt: async (timeoutMs) => {
        const main = page.getByRole('main');
        await main.getByLabel(textOf('Demo.Login.Email', language)).fill(email);
        await main.getByLabel(textOf('Demo.Login.Password', language), { exact: true }).fill(password);
        await main.getByRole('button', { name: textOf('Demo.Login.Submit', language) }).click();
        await account.waitFor({ timeout: timeoutMs });
      },
      again: () => openSignIn(page, language),
    }),
  );
}

/** Opens the members area from the home page's menu, which shows a visitor the sign-in page. */
async function openSignIn(page: Page, language: Language): Promise<void> {
  await page.goto(`/${language}/`);
  await page
    .getByRole('navigation')
    .getByRole('link', { name: pageOf('demoMembers', language).name, exact: true })
    .click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(pageOf('demoLogin', language).heading);
}
