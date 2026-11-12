// A configuration key wraps after its colons only (16.6's review).
import { html, render } from '@umbraco-cms/backoffice/external/lit';
import { describe, expect, it } from 'vitest';
import { configKey } from './config-key';

describe('a configuration key', () => {
  it('is code with a place to wrap after each colon and nowhere else', () => {
    const host = document.createElement('p');
    render(html`${configKey('JironFormBuilder:Privacy:AllowRetentionOff')}`, host);
    const code = host.querySelector('code');
    expect(code?.textContent).toBe('JironFormBuilder:Privacy:AllowRetentionOff');
    expect(code?.querySelectorAll('wbr')).toHaveLength(2);
    expect(code?.innerHTML.replace(/<!--[^>]*-->/g, '')).toBe('JironFormBuilder:<wbr>Privacy:<wbr>AllowRetentionOff');
  });

  it('is code as it is without a colon', () => {
    const host = document.createElement('p');
    render(html`${configKey('Paused')}`, host);
    expect(host.querySelector('code')?.querySelectorAll('wbr')).toHaveLength(0);
  });
});
