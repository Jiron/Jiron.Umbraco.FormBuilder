import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';
import { createHash, randomBytes } from 'node:crypto';
import { SIGN_IN_STALL_MS, signInOnceMore } from './sign-in-retry.ts';
import { withUserLock } from './user-lock.ts';
import type { Account } from './users.ts';

const SECURITY_API = '/umbraco/management/api/v1/security/back-office';
const BACKOFFICE_CLIENT = 'umbraco-back-office';

/**
 * Signs a backoffice user in through the Management API's own login and PKCE flow, one sign-in at a time
 * (support/user-lock.ts), and returns an API client for them.
 *
 * Umbraco 17 keeps the backoffice tokens in HTTP-only cookies: the login sets the identity cookie, the authorize step
 * puts the PKCE code in a cookie, and the token step puts the access token in one. API calls then send
 * `Authorization: Bearer [redacted]`, as the backoffice does, and Umbraco reads the token from the cookie. The
 * returned context carries both.
 */
export async function loginThroughApi(user: Account, baseUrl: string): Promise<APIRequestContext> {
  // A sign-in that Umbraco's SQLite stalls is signed in once more (support/sign-in-retry.ts).
  return withUserLock(() =>
    signInOnceMore({ who: user.email, attempt: (timeoutMs) => apiLoginFlow(user, baseUrl, timeoutMs) }),
  );
}

/** One sign-in, each of its requests within `timeoutMs`; its context goes when it fails. */
async function apiLoginFlow(user: Account, baseUrl: string, timeoutMs = SIGN_IN_STALL_MS): Promise<APIRequestContext> {
  const api = await playwrightRequest.newContext({ baseURL: baseUrl, timeout: timeoutMs });
  try {
    return await apiLoginSteps(api, user, baseUrl);
  } finally {
    await api.dispose();
  }
}

async function apiLoginSteps(api: APIRequestContext, user: Account, baseUrl: string): Promise<APIRequestContext> {
  const login = await api.post(`${SECURITY_API}/login`, { data: { username: user.email, password: user.password } });
  if (!login.ok()) throw new Error(`The API login of ${user.email} failed with HTTP ${login.status()}.`);

  const verifier = randomBytes(32).toString('base64url');
  const challenge = createHash('sha256').update(verifier).digest('base64url');
  const redirectUri = `${baseUrl}/umbraco/oauth_complete`;
  const authorize = await api.get(`${SECURITY_API}/authorize`, {
    params: {
      client_id: BACKOFFICE_CLIENT,
      response_type: 'code',
      redirect_uri: redirectUri,
      code_challenge: challenge,
      code_challenge_method: 'S256',
      scope: 'offline_access',
    },
    maxRedirects: 0,
  });
  const location = authorize.headers()['location'] ?? '';
  const code = new URL(location, baseUrl).searchParams.get('code');
  if (authorize.status() !== 302 || !code)
    throw new Error(`The authorize step for ${user.email} failed with HTTP ${authorize.status()}.`);

  const token = await api.post(`${SECURITY_API}/token`, {
    form: {
      grant_type: 'authorization_code',
      client_id: BACKOFFICE_CLIENT,
      redirect_uri: redirectUri,
      code,
      code_verifier: verifier,
    },
  });
  if (!token.ok()) throw new Error(`The token step for ${user.email} failed with HTTP ${token.status()}.`);

  const storageState = await api.storageState();
  return playwrightRequest.newContext({
    baseURL: baseUrl,
    storageState,
    extraHTTPHeaders: { Authorization: 'Bearer [redacted]' },
  });
}
