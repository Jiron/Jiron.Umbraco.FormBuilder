// A background export's file downloads through a link with its ticket (ROADMAP 11.4, ADR-0210): the link is the API's
// own address with the ticket the server issued, and the browser follows it as a download, saving the file as it comes.
import { afterEach, describe, expect, it, vi } from 'vitest';

const ticket = vi.fn();
vi.mock('../api', () => ({
  entryExportPlan: vi.fn(),
  entryExportStart: vi.fn(),
  entryExportStatus: vi.fn(),
  entryExportTicket: (options: unknown) => ticket(options),
  entryExportView: vi.fn(),
  entryExportWriteFile: vi.fn(),
}));
vi.mock('../api/client.gen', () => ({ client: { getConfig: () => ({ baseUrl: 'https://site.test' }) } }));
vi.mock('@umbraco-cms/backoffice/resources', () => ({ tryExecute: vi.fn() }));
vi.mock('../library/library-changes', () => ({ codeOf: () => undefined }));
const { downloadExport, followLink } = await import('./export-api');

const PATH = '/umbraco/jfb/management/api/v1/form/f1/entries/export/jobs/k1/file?ticket=abc';

afterEach(() => {
  ticket.mockReset();
  vi.restoreAllMocks();
});

describe("a ready export's file", () => {
  it("downloads through the link its ticket gives, at the API's own address", async () => {
    ticket.mockResolvedValueOnce({ data: { url: PATH, expiresUtc: '2026-11-08T10:01:00Z' } });
    const followed: string[] = [];

    expect(await downloadExport('f1', 'k1', (url) => followed.push(url))).toBe(true);
    expect(ticket).toHaveBeenCalledWith({ path: { key: 'f1', exportKey: 'k1' } });
    expect(followed).toEqual([`https://site.test${PATH}`]);
  });

  it('follows no link when no ticket comes: the export is gone, or the server did not answer', async () => {
    const followed: string[] = [];
    ticket.mockResolvedValueOnce({ data: undefined });
    expect(await downloadExport('f1', 'k1', (url) => followed.push(url))).toBe(false);
    ticket.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    expect(await downloadExport('f1', 'k1', (url) => followed.push(url))).toBe(false);
    expect(followed).toEqual([]);
  });

  it("is a link the browser saves rather than opens, clicked off the page, where the backoffice's router never sees it", () => {
    const clicked: { link: HTMLAnchorElement; onPage: boolean }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ link: this, onPage: this.isConnected });
    });

    followLink(`https://site.test${PATH}`);

    expect(clicked).toHaveLength(1);
    const [{ link, onPage } = { link: undefined, onPage: true }] = clicked;
    expect([link?.href, link?.hasAttribute('download'), link?.rel, onPage]).toEqual([
      `https://site.test${PATH}`,
      true,
      'noopener',
      false,
    ]);
    expect(document.body.querySelector('a'), 'nothing left on the page').toBeNull();
  });
});
