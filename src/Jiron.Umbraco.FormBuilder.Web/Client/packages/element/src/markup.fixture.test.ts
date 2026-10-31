// The element held to the server (ROADMAP 6.6e, ADR-0128): for every form of the fixture (a field of every built-in type
// in each built-in theme and hidden by its rule, pages with steps and with a bar, texts that recall answers, a default
// with merge tags and the notices), in English and in German, the markup the element writes from the element
// endpoint's answer is the server's markup for the same first render, element for element and attribute for attribute,
// without what only a post without JavaScript uses; its model data's script holds the answer's model data.
import { describe, expect, it } from 'vitest';
import { canonical, LANGUAGES, serverMarkup } from './testing';
import { writeForm } from './views';

describe.each(LANGUAGES)('the fixture in %s', (_language, cases) => {
  it.each(cases.map((each) => [each.name, each] as const))(
    '%s: the element writes the server markup',
    (_name, element) => {
      const written = writeForm({ document, api: element.answer.form.apiBase }, element.answer.form);

      expect(canonical(written).join('\n')).toBe(canonical(serverMarkup(document, element)).join('\n'));
      const script = written.querySelector('script[data-jfb-model]');
      if (element.answer.form.display === 'form') expect(script?.textContent).toBe(element.answer.form.modelData);
      else expect(script).toBeNull();
    },
  );

  it('holds every form the step names, the sinks in each built-in theme', () => {
    expect(cases.map((each) => each.name)).toEqual([
      'sink',
      'sink-bootstrap5',
      'sink-unstyled',
      'sink-hidden',
      'paged',
      'paged-bootstrap5',
      'paged-bar',
      'recall',
      'closed',
      'opens',
      'members',
    ]);
  });
});
