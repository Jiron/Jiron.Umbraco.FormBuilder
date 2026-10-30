import { expect, type Page } from '@playwright/test';
import { pageOf, textOf } from './demo-content.ts';

/** Signs a member in through the demo's sign-in page, so a form for members, such as the workshop booking, shows. */
export async function signInMember(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/en/');
  await page
    .getByRole('navigation')
    .getByRole('link', { name: pageOf('demoMembers', 'en').name })
    .click();
  const main = page.getByRole('main');
  await main.getByLabel(textOf('Demo.Login.Email', 'en')).fill(email);
  await main.getByLabel(textOf('Demo.Login.Password', 'en'), { exact: true }).fill(password);
  await main.getByRole('button', { name: textOf('Demo.Login.Submit', 'en') }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(pageOf('demoMembers', 'en').heading);
}
