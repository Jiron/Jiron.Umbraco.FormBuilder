// The printable view printed in place (ROADMAP 11.4, ADR-0210): a hidden frame that runs no script holds the server's
// document, the browser's print dialog opens on it, and the frame goes once printing is done.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { must } from '../testing';
import { printDocument } from './export-print';

afterEach(() => {
  document.body.replaceChildren();
});

/** The frame the helper added, once the page holds it. */
const frameOf = () => must(document.body.querySelector('iframe'), 'the print frame');

describe('printing the printable view', () => {
  it('prints the document in a frame that runs nothing, and removes the frame after printing', async () => {
    const printing = printDocument('<!DOCTYPE html><html><body><h1>Entries</h1></body></html>');
    const frame = frameOf();
    const view = must(frame.contentWindow, 'the frame window');
    const print = vi.fn(() => view.dispatchEvent(new Event('afterprint')));
    Object.assign(view, { print, focus: vi.fn() });
    frame.dispatchEvent(new Event('load'));

    expect(await printing).toBe(true);
    expect(frame.getAttribute('sandbox')).toBe('allow-same-origin allow-modals');
    expect(frame.getAttribute('srcdoc')).toContain('<h1>Entries</h1>');
    expect(frame.getAttribute('aria-hidden')).toBe('true');
    expect(print).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(document.body.querySelector('iframe')).toBeNull());
  });

  it('says so when the browser cannot print, and leaves no frame behind', async () => {
    const printing = printDocument('<p>one entry</p>');
    const frame = frameOf();
    Object.assign(must(frame.contentWindow, 'the frame window'), {
      focus: vi.fn(),
      print: () => {
        throw new Error('Printing is blocked.');
      },
    });
    frame.dispatchEvent(new Event('load'));

    expect(await printing).toBe(false);
    expect(document.body.querySelector('iframe')).toBeNull();
  });
});
