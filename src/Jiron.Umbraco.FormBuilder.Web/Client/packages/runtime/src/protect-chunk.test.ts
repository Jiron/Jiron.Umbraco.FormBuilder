// What protects a form on the page (ROADMAP 7.2c, ADR-0157): the puzzle's hash as the server computes it, solved in a worker
// or in slices where none can start, and a captcha of which nothing loads before consent, in each mode the site may choose.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { installGlobal, type JironFormBuilderGlobal } from './global';
import { firstBits, search, solve, enhanceCaptcha, type Captcha, type CaptchaTold } from './protect-chunk';
import { wordsFor } from './messages';

const zeros = (text: string, bits: number): boolean => firstBits(text) >>> (32 - bits) === 0;

describe('the puzzle', () => {
  // The first 32 bits of the SHA-256 of each, from another implementation.
  it.each([
    ['', 3820012610],
    ['a', 3398926610],
    ['abc', 3128432319],
    ['0123456789abcdef0123456789abcdef:1', 3277245358],
    ['0123456789abcdef0123456789abcdef:12345678901234567890', 3777480331],
    ['x'.repeat(55), 3588392296],
  ])('hashes %j as SHA-256 does', (text, first) => {
    expect(firstBits(text)).toBe(first);
  });

  it('finds the first number that solves it', () => {
    const puzzle = { nonce: 'abcdefabcdefabcdefabcdefabcdefab', bits: 10 };
    const found = search(puzzle, 0, 1 << 20);

    expect(found).not.toBeNull();
    expect(zeros(`${puzzle.nonce}:${String(found)}`, 10)).toBe(true);
    for (let number = 0; number < (found as number); number++) {
      expect(zeros(`${puzzle.nonce}:${String(number)}`, 10)).toBe(false);
    }
  });

  it('looks from where it is told, among as many as it is told', () => {
    const puzzle = { nonce: 'abcdefabcdefabcdefabcdefabcdefab', bits: 10 };
    const first = search(puzzle, 0, 1 << 20) as number;

    expect(search(puzzle, first, 1)).toBe(first);
    expect(search(puzzle, first + 1, 1 << 20)).toBeGreaterThan(first);
    expect(search(puzzle, 0, first)).toBeNull();
  });

  it('finds nothing where the numbers it may look at hold no answer', () => {
    const puzzle = { nonce: 'abcdefabcdefabcdefabcdefabcdefab', bits: 24 };

    expect(search(puzzle, 0, 5)).toBeNull();
  });

  describe('as a page solves it', () => {
    afterEach(() => {
      vi.unstubAllGlobals();
    });

    const PUZZLE = { nonce: 'abcdefabcdefabcdefabcdefabcdefab', bits: 16 };

    it('goes on from slice to slice until it has the answer', async () => {
      vi.stubGlobal('Worker', undefined);
      let nonce = 0;
      const beyond = (): { nonce: string; bits: number; first: number } => {
        for (; ; nonce++) {
          const puzzle = { nonce: String(nonce).padStart(32, 'a'), bits: 18 };
          const first = search(puzzle, 0, 1 << 22) as number;
          if (first > 45_000) return { ...puzzle, first };
        }
      };
      const { first, ...puzzle } = beyond();

      expect(await solve(puzzle)).toBe(String(first));
    });

    it('looks in slices where no worker can start, as across origins, and still answers', async () => {
      vi.stubGlobal(
        'Worker',
        class {
          constructor() {
            throw new DOMException('Another origin', 'SecurityError');
          }
          postMessage(): void {
            throw new Error('no worker to ask');
          }
        },
      );
      const answer = await solve(PUZZLE);

      expect(zeros(`${PUZZLE.nonce}:${answer}`, 16)).toBe(true);
    });

    it('takes the answer of its worker, which it asks for this puzzle, and ends it', async () => {
      const made: { url: unknown; options: unknown; asked: unknown; ended: boolean }[] = [];
      vi.stubGlobal(
        'Worker',
        class {
          onmessage: ((event: MessageEvent<number>) => void) | undefined;
          state: (typeof made)[number];
          constructor(url: unknown, options: unknown) {
            this.state = { url, options, asked: null, ended: false };
            made.push(this.state);
          }
          postMessage(data: unknown): void {
            this.state.asked = data;
            setTimeout(() => this.onmessage?.({ data: 41 } as MessageEvent<number>));
          }
          terminate(): void {
            this.state.ended = true;
          }
        },
      );

      expect(await solve(PUZZLE)).toBe('41');
      expect(made).toHaveLength(1);
      expect(made[0]?.asked).toEqual(PUZZLE);
      expect(made[0]?.options).toEqual({ type: 'module' });
      expect(made[0]?.ended).toBe(true);
    });

    it('looks in slices itself when its worker fails, as under a policy that refuses it', async () => {
      vi.stubGlobal(
        'Worker',
        class {
          onerror: (() => void) | undefined;
          postMessage(): void {
            setTimeout(() => this.onerror?.());
          }
        },
      );
      const answer = await solve(PUZZLE);

      expect(zeros(`${PUZZLE.nonce}:${answer}`, 16)).toBe(true);
    });
  });
});

interface FakeProvider {
  ready?: (run: () => void) => void;
  render: ReturnType<typeof vi.fn>;
  reset: ReturnType<typeof vi.fn>;
  execute?: ReturnType<typeof vi.fn>;
}

describe('the captcha', () => {
  const TOLD: CaptchaTold = {
    provider: 'hcaptcha',
    siteKey: 'site',
    scriptAddress: 'https://check.example/api.js?x=1',
    consent: 'explicit',
  };
  let runtime: JironFormBuilderGlobal;
  let address = 0;

  const scriptsOf = (): HTMLScriptElement[] => [...document.head.querySelectorAll<HTMLScriptElement>('script')];
  const open = (more = ''): HTMLFormElement => {
    document.body.innerHTML =
      '<form id="jfb-1" data-jfb-form="f"><section data-jfb-page="p1"></section><section data-jfb-page="p2">' +
      `${more}</section><div class="jfb-navigation"><button data-jfb-nav="submit" class="jfb-button jfb-primary">Send</button></div></form>`;
    return document.querySelector('form') as HTMLFormElement;
  };
  const give = async (
    form: HTMLFormElement,
    told: Partial<CaptchaTold> = {},
    agreeing: HTMLInputElement | null = null,
  ): Promise<Captcha> => {
    address += 1;
    return enhanceCaptcha(
      form,
      'de-DE',
      { ...TOLD, scriptAddress: `https://check.example/${String(address)}.js`, ...told },
      await wordsFor('en-US'),
      agreeing,
    );
  };
  /** The provider's script loads: the fake provider is there, and the tag says so. */
  const scriptLoads = (provider: FakeProvider, name: 'hcaptcha' | 'grecaptcha' = 'hcaptcha'): void => {
    Object.assign(window, { [name]: provider });
    scriptsOf().at(-1)?.dispatchEvent(new Event('load'));
  };
  const provider = (): FakeProvider => ({ render: vi.fn(() => 7), reset: vi.fn() });
  const box = (form: HTMLFormElement): HTMLElement => form.querySelector('.jfb-captcha') as HTMLElement;
  const consent = (): JironFormBuilderGlobal['consent'] => runtime.consent;
  /** Which elements the page shows, as a browser lays them out and a test page does not. */
  const shows = (...elements: Element[]): void => {
    vi.spyOn(Element.prototype, 'getClientRects').mockImplementation(function (this: Element) {
      return (elements.includes(this) ? [{}] : []) as unknown as DOMRectList;
    });
  };

  beforeEach(() => {
    const target = {} as { JironFormBuilder?: JironFormBuilderGlobal };
    runtime = installGlobal(
      target,
      'v',
      () => undefined,
      () => null,
    );
    Object.assign(window, { JironFormBuilder: runtime });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    for (const name of ['JironFormBuilder', 'hcaptcha', 'grecaptcha', 'Cookiebot'])
      Reflect.deleteProperty(window, name);
    document.body.innerHTML = '';
    document.head.innerHTML = '';
  });

  describe('explicit', () => {
    it('loads nothing before the visitor agrees, and says what it asks, with a button in the look of the form', async () => {
      const form = open();
      await give(form);

      expect(scriptsOf()).toEqual([]);
      expect(box(form).querySelector('p')?.textContent).toBe(
        'This form uses a spam check from an outside service, which loads only if you agree.',
      );
      const press = box(form).querySelector('button');
      expect(press?.textContent).toBe('Load the check');
      expect(press?.type).toBe('button');
      expect(press?.className).toBe('jfb-button jfb-primary');
    });

    it('has its place above the buttons when the view wrote it none, and takes the one it wrote', async () => {
      const form = open();
      await give(form);
      expect(box(form).nextElementSibling).toBe(form.querySelector('.jfb-navigation'));
      expect(box(form).tabIndex).toBe(-1);

      const written = open('<div class="jfb-captcha" id="written"></div>');
      await give(written);
      expect(written.querySelectorAll('.jfb-captcha')).toHaveLength(1);
      expect(box(written).id).toBe('written');
      expect(box(written).parentElement).toBe(written.querySelector('[data-jfb-page="p2"]'));
      expect(box(written).querySelector('button')).not.toBeNull();
    });

    it('loads the script at the press, in the language of the page, and shows the check', async () => {
      const form = open();
      await give(form, { scriptAddress: 'https://check.example/press.js?x=1' });
      box(form).querySelector('button')?.click();

      expect(consent().has('captcha')).toBe(true);
      const script = scriptsOf()[0];
      expect(script?.src).toBe('https://check.example/press.js?x=1&hl=de-DE');
      expect(script?.async).toBe(true);
      expect(box(form).querySelector('button'), 'the notice is gone').toBeNull();

      const made = provider();
      scriptLoads(made);
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
      expect(made.render.mock.calls[0]?.[1]).toMatchObject({ sitekey: 'site' });
      expect(box(form).contains(made.render.mock.calls[0]?.[0] as Node)).toBe(true);
    });

    it("carries the page's nonce to the provider's script", async () => {
      const form = open();
      const own = document.createElement('script');
      own.setAttribute('nonce', 'n0nce');
      own.nonce = 'n0nce';
      document.body.append(own);
      await give(form);
      consent().grant('captcha');

      expect(scriptsOf()[0]?.nonce).toBe('n0nce');
    });

    it("shows a site's own provider through the global its challenge names (ADR-0234)", async () => {
      const form = open();
      const captcha = await give(form, { provider: 'arithmetic', global: 'siteChecks' });
      consent().grant('captcha');
      const own = provider();
      const other = provider();
      Object.assign(window, { siteChecks: own, grecaptcha: other });
      scriptsOf().at(-1)?.dispatchEvent(new Event('load'));

      await vi.waitFor(() => expect(own.render).toHaveBeenCalledOnce());
      expect(other.render).not.toHaveBeenCalled();
      (own.render.mock.calls[0]?.[1] as Record<string, (token?: string) => void>)['callback']?.('7:abc');
      expect(await captcha.token()).toBe('7:abc');
      captcha.spent();
      expect(own.reset).toHaveBeenCalledWith(7);
      Reflect.deleteProperty(window, 'siteChecks');
    });

    it('gives the token the check gave, and none once it expired', async () => {
      const form = open();
      const captcha = await give(form);
      expect(await captcha.token()).toBeNull();

      consent().grant('captcha');
      const made = provider();
      scriptLoads(made);
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
      expect(await captcha.token()).toBeNull();

      const options = made.render.mock.calls[0]?.[1] as Record<string, (token?: string) => void>;
      options['callback']?.('tok');
      expect(await captcha.token()).toBe('tok');
      options['expired-callback']?.();
      expect(await captcha.token()).toBeNull();
    });

    it('waits for a provider that says when it is ready', async () => {
      const form = open();
      await give(form, { provider: 'recaptchaV2' });
      consent().grant('captcha');
      let go: () => void = () => undefined;
      const made: FakeProvider = {
        ...provider(),
        ready: (run) => {
          go = run;
        },
      };
      scriptLoads(made, 'grecaptcha');
      await new Promise((resolve) => setTimeout(resolve, 5));
      expect(made.render).not.toHaveBeenCalled();

      go();
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
    });

    it("asks the form's own provider, whichever else the page has", async () => {
      const form = open();
      await give(form, { provider: 'recaptchaV2' });
      consent().grant('captcha');
      const other = provider();
      const own = provider();
      Object.assign(window, { hcaptcha: other });
      scriptLoads(own, 'grecaptcha');
      await vi.waitFor(() => expect(own.render).toHaveBeenCalledOnce());

      expect(other.render).not.toHaveBeenCalled();
    });

    it('shows no check once the visitor withdrew before the script came', async () => {
      const form = open();
      await give(form);
      consent().grant('captcha');
      consent().withdraw();
      const made = provider();
      scriptLoads(made);
      await new Promise((resolve) => setTimeout(resolve, 5));

      expect(made.render).not.toHaveBeenCalled();
      expect(box(form).querySelector('button')?.textContent).toBe('Load the check');
    });

    it('asks for a new check after a refused send', async () => {
      const form = open();
      const captcha = await give(form);
      consent().grant('captcha');
      const made = provider();
      scriptLoads(made);
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
      (made.render.mock.calls[0]?.[1] as { callback: (token: string) => void }).callback('tok');

      captcha.spent();

      expect(await captcha.token()).toBeNull();
      expect(made.reset).toHaveBeenCalledWith(7);
    });

    it('says what is missing and takes the focus to the button, and to the check once it shows', async () => {
      const form = open();
      const captcha = await give(form);
      expect(captcha.ask()).toBe(true);
      const said = box(form).querySelector('.jfb-error');
      expect(said?.textContent).toBe('Complete the check before you send the form.');
      expect(said?.getAttribute('role')).toBe('alert');
      expect(document.activeElement).toBe(box(form).querySelector('button'));

      captcha.ask();
      expect(box(form).querySelectorAll('.jfb-error'), 'once').toHaveLength(1);

      consent().grant('captcha');
      const made = provider();
      scriptLoads(made);
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
      expect(captcha.ask()).toBe(true);
      expect(document.activeElement).toBe(box(form));
      (made.render.mock.calls[0]?.[1] as { callback: (token: string) => void }).callback('tok');
      expect(box(form).querySelector('.jfb-error'), 'gone with the check passed').toBeNull();
    });

    it('brings a place the visitor cannot see to the buttons they are at, and leaves one they can', async () => {
      const form = open('<div class="jfb-captcha" id="written"></div>');
      const captcha = await give(form);
      const place = box(form);
      shows(place);
      captcha.ask();
      expect(place.parentElement, 'on the page they are on').toBe(form.querySelector('[data-jfb-page="p2"]'));

      shows();
      captcha.ask();
      expect(place.nextElementSibling).toBe(form.querySelector('.jfb-navigation'));
    });

    it('takes the check away when consent is withdrawn, with its token', async () => {
      const form = open();
      const captcha = await give(form);
      consent().grant('captcha');
      const made = provider();
      scriptLoads(made);
      await vi.waitFor(() => expect(made.render).toHaveBeenCalledOnce());
      (made.render.mock.calls[0]?.[1] as { callback: (token: string) => void }).callback('tok');

      consent().withdraw();

      expect(await captcha.token()).toBeNull();
      expect(box(form).querySelector('button')?.textContent).toBe('Load the check');
      captcha.spent();
      expect(made.reset, 'no check to reset').not.toHaveBeenCalled();
    });

    it('says when the script cannot load, leaves the send free to keep its answers, and asks again at a new consent', async () => {
      const form = open();
      const captcha = await give(form, { scriptAddress: 'https://check.example/fails.js' });
      consent().grant('captcha');
      scriptsOf()[0]?.dispatchEvent(new Event('error'));
      await vi.waitFor(() =>
        expect(box(form).querySelector('p')?.textContent).toBe(
          'The spam check could not load. You can still send the form.',
        ),
      );
      expect(captcha.ask(), 'nothing the visitor could do').toBe(false);
      expect(box(form).querySelector('.jfb-error')).toBeNull();

      consent().withdraw();
      consent().grant('captcha');
      expect(scriptsOf(), 'asked again').toHaveLength(2);
      expect(captcha.ask()).toBe(true);
    });

    it('asks again at a word of consent that takes nothing back, and counts as loading then', async () => {
      const form = open();
      const captcha = await give(form, { scriptAddress: 'https://check.example/again.js' });
      consent().grant('captcha');
      scriptsOf()[0]?.dispatchEvent(new Event('error'));
      await vi.waitFor(() => expect(captcha.ask()).toBe(false));

      consent().grant('captcha');

      expect(scriptsOf()).toHaveLength(2);
      expect(captcha.ask()).toBe(true);
    });

    it('says so too when the provider cannot show its check', async () => {
      const form = open();
      const captcha = await give(form);
      consent().grant('captcha');
      scriptLoads({
        render: vi.fn(() => {
          throw new Error('wrong key');
        }),
        reset: vi.fn(),
      });

      await vi.waitFor(() => expect(captcha.ask()).toBe(false));
    });

    it('loads one script for two forms of the page', async () => {
      const form = open();
      const told = { scriptAddress: 'https://check.example/shared.js' };
      await give(form, told);
      const second = document.createElement('form');
      second.innerHTML = '<section data-jfb-page="p"></section><div class="jfb-navigation"></div>';
      document.body.append(second);
      await give(second, told);
      consent().grant('captcha');

      expect(scriptsOf()).toHaveLength(1);
    });
  });

  describe('a consent field that says it agrees to the check', () => {
    const FIELD = '<input type="checkbox" id="agree" name="agree">';

    it('takes the place of the button, ticked and unticked, and leaves the consent of the page alone', async () => {
      const form = open(FIELD);
      const agree = form.querySelector('#agree') as HTMLInputElement;
      await give(form, {}, agree);
      expect(box(form).childElementCount, 'the field says it itself').toBe(0);
      expect(scriptsOf()).toEqual([]);

      agree.checked = true;
      agree.dispatchEvent(new Event('change'));
      expect(scriptsOf()).toHaveLength(1);
      expect(consent().has('captcha')).toBe(false);

      agree.checked = false;
      agree.dispatchEvent(new Event('change'));
      expect(box(form).childElementCount).toBe(0);
    });

    it('counts when it was ticked by the time the form woke', async () => {
      const form = open('<input type="checkbox" id="agree" name="agree" checked>');
      await give(form, {}, form.querySelector('#agree'));

      expect(scriptsOf()).toHaveLength(1);
    });

    it('takes the focus when a send asks and the visitor can reach it', async () => {
      const form = open(FIELD);
      const agree = form.querySelector('#agree') as HTMLInputElement;
      const captcha = await give(form, {}, agree);
      shows(agree);
      captcha.ask();

      expect(document.activeElement).toBe(agree);
      expect(box(form).querySelector('button')).toBeNull();
    });

    it('gives way to the button when the visitor cannot reach it, as a rule hid it or it is on another page', async () => {
      const form = open(FIELD);
      const agree = form.querySelector('#agree') as HTMLInputElement;
      agree.disabled = true;
      const captcha = await give(form, {}, agree);
      shows(agree);
      captcha.ask();

      const press = box(form).querySelector('button');
      expect(document.activeElement).toBe(press);
      press?.click();
      expect(scriptsOf()).toHaveLength(1);
    });

    it('is no concern of a form in another mode', async () => {
      const form = open('<input type="checkbox" id="agree" name="agree" checked>');
      await give(form, { consent: 'cmp' }, form.querySelector('#agree'));

      expect(scriptsOf()).toEqual([]);
      expect(box(form).querySelector('p')).not.toBeNull();
    });
  });

  describe('cmp', () => {
    it("says where to agree, with no button, and loads at the consent manager's word", async () => {
      const form = open();
      await give(form, { consent: 'cmp' });
      expect(scriptsOf()).toEqual([]);
      expect(box(form).querySelector('p')?.textContent).toContain('cookie settings');
      expect(box(form).querySelector('button')).toBeNull();

      consent().grant('captcha');
      expect(scriptsOf()).toHaveLength(1);
    });

    it('counts a word that came before the form woke', async () => {
      const form = open();
      consent().grant('captcha');
      await give(form, { consent: 'cmp' });

      expect(scriptsOf()).toHaveLength(1);
      expect(box(form).querySelector('p')).toBeNull();
    });
  });

  describe('implicit', () => {
    it('loads with the form, and gives no other form consent', async () => {
      const form = open();
      await give(form, { consent: 'implicit' });

      expect(scriptsOf()).toHaveLength(1);
      expect(box(form).childElementCount).toBe(0);
      expect(consent().has('captcha')).toBe(false);
    });
  });

  describe('cookiebot', () => {
    const cookiebot = (consents: Record<string, boolean>, renew = vi.fn()): ReturnType<typeof vi.fn> => {
      Object.assign(window, { Cookiebot: { consent: consents, renew } });
      return renew;
    };

    it('names the category and opens the cookie settings, until the visitor agrees to it', async () => {
      const form = open();
      const renew = cookiebot({ marketing: false, statistics: true });
      await give(form, { consent: 'cookiebot', category: 'marketing' });

      expect(scriptsOf()).toEqual([]);
      expect(box(form).querySelector('p')?.textContent).toContain('marketing cookies');
      box(form).querySelector('button')?.click();
      expect(renew).toHaveBeenCalledOnce();

      cookiebot({ marketing: true });
      window.dispatchEvent(new Event('CookiebotOnAccept'));
      expect(scriptsOf()).toHaveLength(1);
    });

    it.each(['CookiebotOnConsentReady', 'CookiebotOnAccept', 'CookiebotOnDecline'])('follows %s', async (type) => {
      const form = open();
      cookiebot({ marketing: true });
      await give(form, { consent: 'cookiebot', category: 'marketing' });
      expect(box(form).querySelector('p')).toBeNull();

      cookiebot({ marketing: false });
      window.dispatchEvent(new Event(type));
      expect(box(form).querySelector('p'), 'the notice again').not.toBeNull();
    });

    it("takes a consent given before the form woke, asks when Cookiebot is not there, and leaves the page's alone", async () => {
      const form = open();
      await give(form, { consent: 'cookiebot', category: 'marketing' });
      expect(scriptsOf()).toEqual([]);

      cookiebot({ marketing: true });
      await give(form, { consent: 'cookiebot', category: 'marketing' });
      expect(scriptsOf()).toHaveLength(1);
      expect(consent().has('captcha')).toBe(false);
    });

    it('loads at once for the necessary category, which Cookiebot always gives, and asks only while Cookiebot is not there', async () => {
      const form = open();
      await give(form, { consent: 'cookiebot', category: 'necessary' });
      expect(scriptsOf()).toEqual([]);
      // Without a category word, the notice says only that the cookie settings load the check.
      expect(box(form).querySelector('p')?.textContent).toBe((await wordsFor('en-US')).captchaManager);

      cookiebot({ necessary: true, marketing: false });
      window.dispatchEvent(new Event('CookiebotOnConsentReady'));
      expect(scriptsOf()).toHaveLength(1);
    });

    it('waits for its own consent when a form beside it agrees by its button', async () => {
      const form = open();
      cookiebot({ marketing: false });
      await give(form, { consent: 'cookiebot', category: 'marketing' });
      consent().grant('captcha');

      expect(scriptsOf()).toEqual([]);
    });
  });

  describe('a score-based provider', () => {
    const SCORED = { provider: 'recaptchaV3', action: 'submit', consent: 'implicit' };

    it('shows no check and is asked at the send, for its action', async () => {
      const form = open();
      const captcha = await give(form, SCORED);
      const execute = vi.fn(() => Promise.resolve('scored'));
      const made: FakeProvider = { ...provider(), execute, ready: (run) => run() };
      scriptLoads(made, 'grecaptcha');
      await new Promise((resolve) => setTimeout(resolve, 5));
      expect(made.render).not.toHaveBeenCalled();

      expect(await captcha.token()).toBe('scored');
      expect(execute).toHaveBeenCalledWith('site', { action: 'submit' });
    });

    it('asks a new token at each send, and resets nothing', async () => {
      const form = open();
      const captcha = await give(form, SCORED);
      const execute = vi.fn(() => Promise.resolve('scored'));
      const made: FakeProvider = { ...provider(), execute, ready: (run) => run() };
      scriptLoads(made, 'grecaptcha');
      await captcha.token();
      captcha.spent();
      await captcha.token();

      expect(execute).toHaveBeenCalledTimes(2);
      expect(made.reset).not.toHaveBeenCalled();
    });

    it('gives none before consent, and none when the provider fails', async () => {
      const form = open();
      const captcha = await give(form, { ...SCORED, consent: 'cmp' });
      expect(await captcha.token()).toBeNull();
      expect(scriptsOf()).toEqual([]);

      consent().grant('captcha');
      const made: FakeProvider = {
        ...provider(),
        execute: vi.fn(() => Promise.reject(new Error('down'))),
        ready: (run) => run(),
      };
      scriptLoads(made, 'grecaptcha');
      expect(await captcha.token()).toBeNull();
    });

    it('has nothing to ask of a visitor who agreed, as a refused score has no check to complete', async () => {
      const form = open();
      const captcha = await give(form, { ...SCORED, consent: 'cmp' });
      expect(captcha.ask(), 'they have yet to agree').toBe(true);

      consent().grant('captcha');
      expect(captcha.ask()).toBe(false);
    });
  });
});

describe('the consent of a page', () => {
  const global = (): JironFormBuilderGlobal => {
    const target = {} as { JironFormBuilder?: JironFormBuilderGlobal };
    return installGlobal(
      target,
      'v',
      () => undefined,
      () => null,
    );
  };

  it('is given for a captcha, taken back, and told on the document each time', () => {
    const { consent } = global();
    const heard = vi.fn();
    document.addEventListener('jfb:consent', heard);
    expect(consent.has('captcha')).toBe(false);

    consent.grant('captcha');
    expect(consent.has('captcha')).toBe(true);
    expect(heard).toHaveBeenCalledTimes(1);

    consent.withdraw();
    expect(consent.has('captcha')).toBe(false);
    expect(heard).toHaveBeenCalledTimes(2);
    document.removeEventListener('jfb:consent', heard);
  });

  it('knows no other purpose', () => {
    const { consent } = global();
    const heard = vi.fn();
    document.addEventListener('jfb:consent', heard);

    consent.grant('marketing');

    expect(consent.has('captcha')).toBe(false);
    expect(consent.has('marketing')).toBe(false);
    expect(heard).not.toHaveBeenCalled();
    consent.grant('captcha');
    expect(consent.has('marketing')).toBe(false);
    document.removeEventListener('jfb:consent', heard);
  });
});
