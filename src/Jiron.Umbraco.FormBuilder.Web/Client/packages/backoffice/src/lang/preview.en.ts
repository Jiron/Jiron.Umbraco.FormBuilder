// The preview's texts in English (ROADMAP 10.3e, ADR-0203): its full-screen sidebar, the frame, its choices and its
// states. They load with the form workspace (ADR-0201); the footer's Preview is the designer's, at the start.

export default {
  jfbPreview: {
    headline: (name: string) => `Preview of ${name}`,
    width: 'Width',
    more: 'More preview options',
    scheme: 'Colour scheme',
    light: 'Light',
    dark: 'Dark',
    frame: 'The form as visitors see it',
    loading: 'Loading the preview…',
    rulesInvalid:
      "This draft's rules have errors, so it can't be shown as visitors would get it. The form check on the Design view lists them.",
  },
};
