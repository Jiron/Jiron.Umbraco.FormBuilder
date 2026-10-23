/**
 * The headless element (ADR-0015): `<jfb-form>` renders a form from its public projection in the light DOM, so the
 * themes and the runtime's behaviour apply unchanged. Rendering comes in 6.6; until then the element only registers.
 */
export class JfbFormElement extends HTMLElement {}

/** Registers `<jfb-form>` unless the page has it already (a second copy of the script changes nothing). */
export function defineElement(registry: CustomElementRegistry = customElements): void {
  if (!registry.get('jfb-form')) registry.define('jfb-form', JfbFormElement);
}

defineElement();
