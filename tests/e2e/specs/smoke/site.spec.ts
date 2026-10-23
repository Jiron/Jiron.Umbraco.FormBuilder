import { expect, test } from '../../fixtures.ts';
import { homeOf, LANGUAGES } from '../../support/demo-content.ts';

// The test host serves the seeded demo site in both languages (ROADMAP 3.3).
for (const language of LANGUAGES) {
  test(`the home page /${language}/ answers in its language`, async ({ page }) => {
    const response = await page.goto(`/${language}/`);

    expect(response?.status()).toBe(200);
    await expect(page.locator('html')).toHaveAttribute('lang', new RegExp(`^${language}`));
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(homeOf(language).heading);
  });
}
