import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import { describe, expect, it, vi } from 'vitest';
import { client } from './api/client.gen';
import { createClientConfig } from './hey-api';

describe('the generated client (ADR-0050)', () => {
  it("starts from the backoffice's HTTP client: its address, its credentials and its placeholder token", () => {
    const backoffice = umbHttpClient.getConfig();
    const config = client.getConfig();

    expect(config.credentials).toBe('include');
    expect(config.auth).toBe(backoffice.auth);
    expect(config.baseUrl).toBe(backoffice.baseUrl);
  });

  it("lets the backoffice's settings win over the generated defaults", () => {
    // The backoffice sets the site's address when it starts, before it loads the bundle.
    const started = { ...umbHttpClient.getConfig(), baseUrl: 'https://site.test' };
    const getConfig = vi.spyOn(umbHttpClient, 'getConfig').mockReturnValue(started);
    try {
      const config = createClientConfig({ baseUrl: 'https://elsewhere.test', credentials: 'omit' });

      expect(config.baseUrl).toBe('https://site.test');
      expect(config.credentials).toBe('include');
    } finally {
      getConfig.mockRestore();
    }
  });
});
