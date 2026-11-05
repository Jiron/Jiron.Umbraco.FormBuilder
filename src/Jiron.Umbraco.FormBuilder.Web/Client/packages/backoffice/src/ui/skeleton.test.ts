import { html, render } from '@umbraco-cms/backoffice/external/lit';
import { describe, expect, it } from 'vitest';
import { skeletonLines } from './skeleton';

describe('the skeleton lines', () => {
  it('stand still where words are read, busy, and say what is read for screen readers alone', () => {
    const box = document.createElement('div');
    render(html`${skeletonLines('Loading', [60, 40, 20])}`, box);
    const busy = box.querySelector('[aria-busy="true"]');
    expect([...box.querySelectorAll<HTMLElement>('.skeleton')].map((line) => line.style.width)).toEqual([
      '60%',
      '40%',
      '20%',
    ]);
    expect([busy?.querySelector('.visually-hidden')?.textContent, busy?.textContent?.trim()]).toEqual([
      'Loading',
      'Loading',
    ]);
  });
});
