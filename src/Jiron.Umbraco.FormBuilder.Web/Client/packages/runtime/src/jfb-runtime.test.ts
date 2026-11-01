import { describe, expect, it, vi } from 'vitest';
import { installGlobal, type JironFormBuilderGlobal } from './global';

describe('window.JironFormBuilder', () => {
  it('is set by the entry, with the build version, init and get', async () => {
    await import('./jfb-runtime');
    expect(window.JironFormBuilder?.version).toBe(__JFB_VERSION__);
    expect(typeof window.JironFormBuilder?.init).toBe('function');
    expect(window.JironFormBuilder?.get(document.body)).toBeNull();
  });

  it('is frozen, and a second copy of the runtime keeps the first one and enhances through it', () => {
    const target: { JironFormBuilder?: JironFormBuilderGlobal } = {};
    const firstInit = vi.fn();
    const secondInit = vi.fn();
    const first = installGlobal(target, '17.0.0', firstInit, () => null);
    expect(Object.isFrozen(first)).toBe(true);
    const second = installGlobal(target, '17.1.0', secondInit, () => null);
    expect(second).toBe(first);
    expect(target.JironFormBuilder?.version).toBe('17.0.0');
    second.init(document);
    expect(firstInit).toHaveBeenCalledWith(document);
    expect(secondInit).not.toHaveBeenCalled();
  });
});
