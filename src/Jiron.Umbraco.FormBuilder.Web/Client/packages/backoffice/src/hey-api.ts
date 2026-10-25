/**
 * The generated client's configuration at start-up (scripts/generate-client.mjs, ADR-0050). The backoffice's own HTTP
 * client brings the site's address, the credentials and the placeholder token that the server exchanges for the real
 * one from its cookie (1.2b), so no script ever holds a token.
 */
import { umbHttpClient } from '@umbraco-cms/backoffice/http-client';
import type { CreateClientConfig } from './api/client.gen';

export const createClientConfig: CreateClientConfig = (config) => ({ ...config, ...umbHttpClient.getConfig() });
