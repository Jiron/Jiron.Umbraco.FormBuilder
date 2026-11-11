import { createServer } from 'node:http';
import { setTimeout as delay } from 'node:timers/promises';
import { E2E_BASE_URL, STATIC_PORT } from './paths.ts';

// The static page of the headless tests (ROADMAP 4.11, 6.6f; ADR-0031): a site that does not run Umbraco, on an origin
// of its own beside the host, whose page loads the element from the host and shows a form through it. It serves one
// page, made from its query: the element's attributes, and `script`, a script of the samples' on the host that the
// page loads beside the element (ADR-0234). The server runs in the test's own process and stops with it.

/** The static site while it runs: its address, and how to stop it. */
export interface StaticSite {
  /** The address the host's public API allows, `http://127.0.0.1:5521`. */
  readonly url: string;
  /** The same site under a name the host's public API does not allow, `http://localhost:5521`. */
  readonly otherOrigin: string;
  stop(): Promise<void>;
}

/** The page's own heading, in its language. */
const HEADINGS: Readonly<Record<string, string>> = {
  en: 'Volunteer with the library',
  de: 'Engagieren Sie sich in der Bibliothek',
};

const escape = (text: string): string =>
  text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');

/**
 * The page: the element's module from the host and a `<jfb-form>` for the host's public API with the attributes
 * given, in a page of the culture's language, as a site would write it; with the scripts of the samples' named, each a
 * path under `/jfb-samples/` on the host.
 */
export function headlessPage(
  host: string,
  attributes: Readonly<Record<string, string>>,
  scripts: readonly string[] = [],
): string {
  for (const script of scripts)
    if (!/^\/jfb-samples\/[a-z0-9-]+\.js$/.test(script)) throw new Error(`"${script}" is no script of the samples.`);
  const all = { api: `${host}/jfb/api/v1`, ...attributes };
  const written = Object.entries(all)
    .map(([name, value]) => {
      if (!/^[a-z][a-z0-9-]*$/.test(name)) throw new Error(`"${name}" is no attribute name.`);
      return ` ${name}="${escape(value)}"`;
    })
    .join('');
  const language = (attributes.culture ?? 'en').split('-')[0] ?? 'en';
  const heading = HEADINGS[language] ?? HEADINGS.en ?? '';
  return [
    '<!doctype html>',
    `<html lang="${escape(language)}">`,
    '<head>',
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    `<title>${escape(heading)}</title>`,
    // No icon, so the browser asks the site for none.
    '<link rel="icon" href="data:,">',
    // The site's own look, as a site has one: the form takes its type and its width from it.
    '<style>body { margin: 0; font: 100%/1.5 system-ui, sans-serif; color: #1f2328; } h1 { line-height: 1.2; } main { max-inline-size: 40rem; margin-inline: auto; padding: 2rem 1rem; }</style>',
    `<script type="module" src="${escape(host)}/jfb/assets/element/jfb-element.js"></script>`,
    // Classic scripts, which a page loads from the host without the host's leave, as a module would need.
    ...scripts.map((script) => `<script src="${escape(host)}${script}" defer></script>`),
    '</head>',
    '<body>',
    '<main>',
    `<h1>${escape(heading)}</h1>`,
    `<jfb-form${written}></jfb-form>`,
    '</main>',
    '</body>',
    '</html>',
    '',
  ].join('\n');
}

/** How long a spec waits for another worker's static site to stop: hooks that start one allow for it. */
export const STATIC_SITE_WAIT_MS = 4 * 60_000;

/**
 * Starts the static site on 5521 for the host's address, the run's host by default. The public API allows that one
 * origin, so the port is the lock: a spec on another worker that holds it is waited for, until it stops its site.
 */
export async function startStaticSite(host: string = E2E_BASE_URL): Promise<StaticSite> {
  const server = createServer((request, response) => {
    const url = new URL(request.url ?? '/', 'http://static.invalid');
    if (request.method !== 'GET' || url.pathname !== '/') {
      response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' }).end('Not found');
      return;
    }
    try {
      const scripts = url.searchParams.getAll('script');
      url.searchParams.delete('script');
      const page = headlessPage(host, Object.fromEntries(url.searchParams), scripts);
      response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }).end(page);
    } catch (error) {
      response.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' }).end(String(error));
    }
  });
  const deadline = Date.now() + STATIC_SITE_WAIT_MS;
  for (;;) {
    try {
      await new Promise<void>((resolve, reject) => {
        // A failed attempt takes both its listeners away, so waiting adds none.
        const failed = (error: Error) => {
          server.off('listening', listening);
          reject(error);
        };
        const listening = () => {
          server.off('error', failed);
          resolve();
        };
        server.once('error', failed);
        server.once('listening', listening);
        server.listen(STATIC_PORT, '127.0.0.1');
      });
      break;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EADDRINUSE' || Date.now() > deadline) throw error;
      await delay(500);
    }
  }
  return {
    url: `http://127.0.0.1:${STATIC_PORT}`,
    otherOrigin: `http://localhost:${STATIC_PORT}`,
    stop: () =>
      new Promise<void>((resolve, reject) => {
        server.closeAllConnections();
        server.close((error) => (error ? reject(error) : resolve()));
      }),
  };
}
