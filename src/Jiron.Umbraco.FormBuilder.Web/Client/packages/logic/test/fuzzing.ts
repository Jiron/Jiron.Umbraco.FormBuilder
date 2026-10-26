// What the fuzz tests share (ROADMAP 5.3a, 5.3b): a seeded generator, so every run tries the same inputs, and how a
// report shows an input.

/** A small seeded generator (xorshift, 32 bits), so every run tries the same inputs. */
export class Random {
  private state: number;

  constructor(seed: number) {
    this.state = seed >>> 0 || 1;
  }

  /** A whole number from 0 up to, not including, the bound. */
  next(bound: number): number {
    let x = this.state;
    x ^= x << 13;
    x ^= x >>> 17;
    x ^= x << 5;
    this.state = x >>> 0;
    return this.state % bound;
  }

  /** A whole number from the low bound up to, not including, the high one. */
  between(low: number, high: number): number {
    return low + this.next(high - low);
  }

  pick<T>(items: readonly T[]): T {
    return items[this.next(items.length)] as T;
  }
}

const BACKSLASH = String.fromCharCode(92);

/** An input as a report shows it: short, and ASCII, with each other code unit as its code. */
export function show(input: string): string {
  const text = input.length > 200 ? input.slice(0, 200) + '...' : input;
  let shown = '';
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    shown += code >= 0x20 && code <= 0x7e ? text.charAt(i) : BACKSLASH + 'u' + code.toString(16).padStart(4, '0');
  }
  return shown;
}
