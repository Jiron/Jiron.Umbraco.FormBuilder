import type { Page } from '@playwright/test';

// The backoffice's look for a test: Umbraco's theme, which it reads from local storage, and the user's language, which
// a test gives by answering the current user's language for its own page only, so nothing changes on the shared host
// (ROADMAP 9.6a).

/** Umbraco's theme aliases. */
export const THEMES = {
  light: 'umb-light-theme',
  dark: 'umb-dark-theme',
  highContrast: 'umb-high-contrast-theme',
} as const;

/** Shows the backoffice in one of Umbraco's themes from the next page load on. */
export async function inTheme(page: Page, theme: keyof typeof THEMES): Promise<void> {
  await page.addInitScript((alias) => localStorage.setItem('umb-theme-alias', alias), THEMES[theme]);
}

/** Shows the backoffice in a language, for example `de-DE`, from the next page load on. */
export async function inLanguage(page: Page, isoCode: string): Promise<void> {
  await page.route(
    (url) => url.pathname.endsWith('/management/api/v1/user/current'),
    async (route) => {
      try {
        const response = await route.fetch();
        const body = (await response.json()) as Record<string, unknown>;
        await route.fulfill({ response, json: { ...body, languageIsoCode: isoCode } });
      } catch (error) {
        // A request the page gave up on, as a navigation does, is answered by nobody.
        if (!/disposed|closed/i.test(String(error))) throw error;
      }
    },
  );
}
