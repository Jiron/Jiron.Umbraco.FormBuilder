// The preview's texts in English (ROADMAP 10.3e and 10.3f, ADR-0203, ADR-0204): its full-screen sidebar, the frame, its
// choices and its states, the sample answers, and the test entries with their actions. They load with the form workspace
// (ADR-0201); the footer's Preview is the designer's, at the start.

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
    sample: 'Fill with sample answers',
    startAgain: 'Start again',
    testEntries: 'Test entries',
    runForTests: 'Run actions for test entries',
    noteOff: 'Entries sent here are test entries. They run no actions.',
    noteOn: 'Entries sent here are test entries. Their actions run, so their emails and messages really go out.',
    saved: (number: number) => `Saved as test entry #${number}.`,
    noneRan: 'No actions ran.',
    running: 'Its actions are running.',
    awaiting: 'Its actions run once its email address is confirmed.',
    sentOnly: 'Sent as a test entry.',
    runActions: 'Run actions…',
    runActionsFor: (number: number) => `Run actions for test entry #${number}…`,
    runHeadline: (number: number) => `Run the actions for test entry #${number}?`,
    runText: (count: number) =>
      count === 1
        ? 'This action runs as it would for a real entry, so its email or message really goes out.'
        : `These ${count} actions run as they would for a real entry, so their emails and messages really go out.`,
    runConfirm: (count: number) => (count === 1 ? 'Run 1 action' : `Run ${count} actions`),
    runFailed: "The actions couldn't be run.",
  },
};
