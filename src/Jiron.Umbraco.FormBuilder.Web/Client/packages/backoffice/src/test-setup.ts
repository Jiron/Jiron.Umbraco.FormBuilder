// What the elements' tests need from the browser that jsdom lacks (Vitest setupFiles, ADR-0134): jsdom's
// ElementInternals has no constraint validation, which UUI's and Umbraco's form controls call on every update. This
// adds the missing members in the shape the HTML standard gives them; it runs in tests only, and does nothing for the
// tests that run in Node without a DOM.
const internals = (globalThis as { ElementInternals?: { prototype: object } }).ElementInternals?.prototype;

if (internals && !('setValidity' in internals)) {
  const states = new WeakMap<object, { flags: ValidityStateFlags; message: string }>();
  const validity = (target: object): ValidityState => {
    const flags = states.get(target)?.flags ?? {};
    const invalid = Object.entries(flags).some(([flag, set]) => flag !== 'valid' && set === true);
    return { ...flags, valid: !invalid } as ValidityState;
  };
  Object.defineProperties(internals, {
    setValidity: {
      value(this: object, flags: ValidityStateFlags = {}, message = '') {
        states.set(this, { flags, message });
      },
    },
    validity: {
      get(this: object) {
        return validity(this);
      },
    },
    validationMessage: {
      get(this: object) {
        return states.get(this)?.message ?? '';
      },
    },
    checkValidity: {
      value(this: object) {
        return validity(this).valid;
      },
    },
    reportValidity: {
      value(this: object) {
        return validity(this).valid;
      },
    },
    willValidate: { get: () => true },
  });
  if (!('setFormValue' in internals)) Object.defineProperty(internals, 'setFormValue', { value: () => undefined });
  if (!('form' in internals)) Object.defineProperty(internals, 'form', { get: () => null });
}

// Umbraco's router listens for clicks on the window and asks whether the target is an SVG link, a type jsdom lacks.
if (typeof Element !== 'undefined' && !('SVGAElement' in globalThis)) {
  (globalThis as Record<string, unknown>)['SVGAElement'] = class SVGAElement extends Element {};
}

// Some of UUI's elements watch their own size; jsdom lays nothing out, so an observer that never reports is the truth.
if (!('ResizeObserver' in globalThis)) {
  (globalThis as Record<string, unknown>)['ResizeObserver'] = class ResizeObserver {
    observe = (): undefined => undefined;
    unobserve = (): undefined => undefined;
    disconnect = (): undefined => undefined;
  };
}

// jsdom has no popovers, which UUI's tab group opens and closes for the tabs that don't fit; nothing is shown either way.
if (typeof HTMLElement !== 'undefined' && !('hidePopover' in HTMLElement.prototype)) {
  for (const name of ['showPopover', 'hidePopover', 'togglePopover'])
    Object.defineProperty(HTMLElement.prototype, name, { value: (): undefined => undefined });
}

// jsdom lays nothing out, so it scrolls nothing into view either; the elements that ask for it go on as in a browser.
if (typeof Element !== 'undefined' && !('scrollIntoView' in Element.prototype)) {
  Object.defineProperty(Element.prototype, 'scrollIntoView', { value: (): undefined => undefined });
}
