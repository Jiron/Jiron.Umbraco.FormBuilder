// @ts-check
// The colour sample's control on a headless site (docs/extending.md, section 8): what <jfb-form> writes in a colour
// field's place, as the type's Razor view (fields/sample.colour.cshtml) writes it, a radio button for each colour with
// its swatch and its name, which the field's legend names. A headless site loads this script beside the element, a
// classic script, which a page may load from another origin without that origin's leave:
//
//   <script type="module" src="https://the-site/jfb/assets/element/jfb-element.js"></script>
//   <script src="https://the-site/jfb-samples/colour-control.js" defer></script>
//
// The element shows again the forms it showed before the control was registered, so the order does not matter.

/** @typedef {import('../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbControlField} JfbControlField */
/** @typedef {import('../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbThemeClasses} JfbThemeClasses */
/** @typedef {import('../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbFormElementConstructor} JfbFormElementConstructor */

(function () {
  const SVG = 'http://www.w3.org/2000/svg';
  const HEX = /^#[0-9a-f]{6}$/;

  /**
   * The colours of the type's public settings, each with a name and a value that is all that reaches the swatch.
   * @param {unknown} settings
   * @returns {{ name: string, value: string }[]}
   */
  function coloursOf(settings) {
    const colours =
      settings !== null && typeof settings === 'object'
        ? /** @type {{ colours?: unknown }} */ (settings).colours
        : undefined;
    if (!Array.isArray(colours)) return [];
    return colours.flatMap((item) =>
      item !== null &&
      typeof item === 'object' &&
      typeof item.name === 'string' &&
      item.name.trim() !== '' &&
      typeof item.value === 'string' &&
      HEX.test(item.value)
        ? [{ name: item.name.trim(), value: item.value }]
        : [],
    );
  }

  /**
   * An SVG element with its attributes: a swatch's fill is a presentation attribute, which no content security policy
   * refuses, where a style would need one that allows it.
   * @param {Document} document
   * @param {string} name
   * @param {Record<string, string>} attributes
   */
  function svg(document, name, attributes) {
    const element = document.createElementNS(SVG, name);
    for (const [attribute, value] of Object.entries(attributes)) element.setAttribute(attribute, value);
    return element;
  }

  /**
   * Writes a colour field's radio buttons.
   * @param {JfbControlField} field
   * @param {Document} document
   * @param {JfbThemeClasses} classes
   * @returns {Node}
   */
  function writeColour(field, document, classes) {
    const settings = field.control?.settings;
    const names = !(
      settings !== null &&
      typeof settings === 'object' &&
      /** @type {{ showNames?: unknown }} */ (settings).showNames === false
    );
    const choices = document.createElement('div');
    choices.className = classes.choices;
    coloursOf(settings).forEach((colour, at) => {
      const id = at === 0 ? field.id : `${field.id}-${at}`;
      const choice = document.createElement('div');
      choice.className = classes.choice;

      const input = document.createElement('input');
      if (classes.choiceInput) input.className = classes.choiceInput;
      input.type = 'radio';
      input.id = id;
      input.name = field.name;
      input.value = colour.value;
      input.checked = field.values.includes(colour.value);
      input.required = field.isRequired;
      input.disabled = field.isDisabled;
      if (field.choiceErrorId) {
        input.setAttribute('aria-invalid', 'true');
        input.setAttribute('aria-describedby', field.choiceErrorId);
      }

      const label = document.createElement('label');
      if (classes.choiceLabel) label.className = classes.choiceLabel;
      label.htmlFor = id;
      const swatch = svg(document, 'svg', {
        width: '1.5em',
        height: '1.5em',
        viewBox: '0 0 2 2',
        'aria-hidden': 'true',
        focusable: 'false',
      });
      swatch.append(
        svg(document, 'circle', {
          cx: '1',
          cy: '1',
          r: '0.94',
          fill: colour.value,
          stroke: 'currentColor',
          'stroke-opacity': '0.25',
          'stroke-width': '0.06',
        }),
      );
      const name = document.createElement('span');
      if (!names) name.className = 'jfb-visually-hidden';
      name.textContent = colour.name;
      label.append(swatch, ' ', name);

      choice.append(input, label);
      choices.append(choice);
    });
    return choices;
  }

  void customElements.whenDefined('jfb-form').then((element) => {
    /** @type {JfbFormElementConstructor} */ (/** @type {unknown} */ (element)).defineControl(
      'sample.colour',
      writeColour,
    );
  });
})();
