import { test as base, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { loginThroughApi } from './support/api-login.ts';
import { fillForm, formOn, submitForm, type FieldValue, type SubmitResult } from './support/form.ts';
import { waitForMailIn, type Mail } from './support/mail.ts';
import { E2E_BASE_URL } from './support/paths.ts';
import { signInOnceMore } from './support/sign-in-retry.ts';
import { withUserLock } from './support/user-lock.ts';
import { account, memberAccount, type Account, type Role } from './support/users.ts';

export interface Fixtures {
  /** The account of a persona (its password is never printed). */
  accountOf: (role: Role) => Account;
  /** The account of a member, 0 or 1. */
  memberOf: (index: number) => Account;
  /** Signs in through the backoffice login screen, as a person would; resolves once the backoffice shows. */
  loginAs: (role: Role, page?: Page) => Promise<void>;
  /** An API client signed in as the persona through the Management API's own login and PKCE flow. */
  apiAs: (role: Role, baseUrl?: string) => Promise<APIRequestContext>;
  /** Asks Umbraco to e-mail the persona a password reset link, as the login screen's "Forgotten password?" does. */
  requestPasswordReset: (role: Role) => Promise<void>;
  /** Waits for an e-mail in the host's pickup folder that matches. */
  waitForMail: (match: (mail: Mail) => boolean, timeoutMs?: number) => Promise<Mail>;
  /** Answers fields of a form by alias (the only form on the page, or the given one). */
  fillForm: (values: Record<string, FieldValue>, form?: Locator) => Promise<void>;
  /** Sends a form and waits for its outcome or its errors. */
  submitForm: (form?: Locator) => Promise<SubmitResult>;
}

// Sign-ins and password resets update the user, so they run one at a time across workers (support/user-lock.ts), and
// a sign-in that Umbraco's SQLite stalls is signed in once more (support/sign-in-retry.ts).

async function loginThroughUi(page: Page, user: Account): Promise<void> {
  await openLogin(page);
  await withUserLock(() =>
    signInOnceMore({
      who: user.email,
      attempt: async (timeoutMs) => {
        await page.getByLabel(/email/i).first().fill(user.email);
        await page
          .getByLabel(/password/i)
          .first()
          .fill(user.password);
        await page
          .getByRole('button', { name: /^(login|log in|sign in)$/i })
          .first()
          .click();
        await page.locator('umb-backoffice-header').waitFor({ timeout: timeoutMs });
      },
      again: () => openLogin(page),
    }),
  );
}

/** Opens the backoffice's sign-in page and waits for its form. */
async function openLogin(page: Page): Promise<void> {
  await page.goto('/umbraco');
  const email = page.getByLabel(/email/i).first();
  // Under a full run's load the backoffice's first script can fail to load, which leaves the page blank for good. One
  // reload recovers; a backoffice that does not boot fails the second time too.
  if (
    !(await email.waitFor({ timeout: 45_000 }).then(
      () => true,
      () => false,
    ))
  ) {
    console.warn('e2e: the backoffice login did not appear within 45 s; reloading once.');
    await page.reload();
    await email.waitFor({ timeout: 45_000 });
  }
}

export const test = base.extend<Fixtures>({
  // Playwright requires the destructuring pattern, so fixtures without dependencies take an empty one.
  accountOf: async ({}, use) => {
    await use(account);
  },

  memberOf: async ({}, use) => {
    await use(memberAccount);
  },

  loginAs: async ({ page }, use) => {
    await use((role, target = page) => loginThroughUi(target, account(role)));
  },

  apiAs: async ({}, use) => {
    const contexts: APIRequestContext[] = [];
    await use(async (role, baseUrl = E2E_BASE_URL) => {
      const context = await loginThroughApi(account(role), baseUrl);
      contexts.push(context);
      return context;
    });
    await Promise.all(contexts.map((context) => context.dispose()));
  },

  requestPasswordReset: async ({ request }, use) => {
    // It updates the user as a sign-in does, so Umbraco's SQLite can stall it the same way: it is asked once more.
    await use((role) =>
      withUserLock(() =>
        signInOnceMore({
          who: account(role).email,
          attempt: async (timeoutMs) => {
            const response = await request.post('/umbraco/management/api/v1/security/forgot-password', {
              data: { email: account(role).email },
              timeout: timeoutMs,
            });
            if (!response.ok())
              throw new Error(`The password reset request for ${role} failed with HTTP ${response.status()}.`);
          },
        }),
      ),
    );
  },

  waitForMail: async ({}, use) => {
    const dir = process.env.JFB_E2E_MAIL_DIR;
    if (!dir) throw new Error('JFB_E2E_MAIL_DIR is not set; the global set-up sets it.');
    await use((match, timeoutMs) => waitForMailIn(dir, match, timeoutMs));
  },

  fillForm: async ({ page }, use) => {
    await use((values, form = formOn(page)) => fillForm(form, values));
  },

  submitForm: async ({ page }, use) => {
    await use((form = formOn(page)) => submitForm(form));
  },
});

export { expect };
