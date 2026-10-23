import { describe, expect, it } from 'vitest';
import { installGlobal } from './global';

describe('window.JironFormBuilder', () => {
  it('is set by the entry, with the build version', async () => {
    await import('./jfb-runtime');
    expect(window.JironFormBuilder?.version).toBe(__JFB_VERSION__);
  });

  it('is frozen, and a second copy of the runtime keeps the first one', () => {
    const target: { JironFormBuilder?: { version: string } } = {};
    const first = installGlobal(target, '17.0.0');
    expect(Object.isFrozen(first)).toBe(true);
    expect(installGlobal(target, '17.1.0')).toBe(first);
    expect(target.JironFormBuilder?.version).toBe('17.0.0');
  });
});
