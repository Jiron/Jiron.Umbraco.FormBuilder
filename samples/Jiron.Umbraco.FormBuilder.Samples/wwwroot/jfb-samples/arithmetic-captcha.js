// @ts-check
// The arithmetic sample's captcha on the page (docs/extending.md): the script a site's own captcha provider names, which
// the website runtime loads once the visitor agrees to the check, as it loads a third party's. It defines the global the
// provider names (ArithmeticCaptchaProvider.ScriptGlobal) with the two calls the runtime makes: render shows the sum of
// the site key in the place the runtime gives, with a box for the answer in the look of the form's own fields, and says
// the token once the box holds a number; reset empties the box after a send was refused. The sum reads the same in every
// language, "3 + 4 =", and a screen reader says it as it is written.
(function () {
  /** @typedef {{ sitekey: string, callback?: (token: string) => void, 'expired-callback'?: () => void }} Options */
  /** @typedef {{ input: HTMLInputElement, options: Options }} Widget */

  let made = 0;

  /**
   * Shows the sum in the place.
   * @param {HTMLElement} place
   * @param {Options} options
   * @returns {Widget}
   */
  function render(place, options) {
    const split = options.sitekey.indexOf(':');
    const sum = options.sitekey.slice(0, split);
    const sealed = options.sitekey.slice(split + 1);
    const [first, second] = sum.split('+');
    const document = place.ownerDocument;
    const form = place.closest('form');
    made += 1;
    const id = `${form?.id ?? 'jfb'}-arithmetic-${String(made)}`;

    const label = document.createElement('label');
    label.htmlFor = id;
    label.className = form?.querySelector('.jfb-field label:not(.jfb-visually-hidden)')?.className ?? 'jfb-label';
    label.textContent = `${first ?? ''} + ${second ?? ''} =`;

    const input = document.createElement('input');
    input.id = id;
    input.className = form?.querySelector('.jfb-field input:not([type="radio"]):not([type="checkbox"])')?.className ?? 'jfb-control';
    input.inputMode = 'numeric';
    input.autocomplete = 'off';
    input.maxLength = 3;
    input.size = 3;
    input.addEventListener('input', () => {
      const answer = input.value.trim();
      if (/^\d{1,3}$/.test(answer)) options.callback?.(`${sealed}:${answer}`);
      else options['expired-callback']?.();
    });

    const holder = document.createElement('div');
    holder.className = 'jfb-arithmetic';
    holder.append(label, ' ', input);
    place.replaceChildren(holder);
    return { input, options };
  }

  /**
   * Empties the box after a refused send, so the visitor answers again.
   * @param {Widget | undefined} widget
   */
  function reset(widget) {
    if (widget === undefined) return;
    widget.input.value = '';
    widget.options['expired-callback']?.();
  }

  Object.assign(window, { jfbSampleArithmetic: { render, reset } });
})();
