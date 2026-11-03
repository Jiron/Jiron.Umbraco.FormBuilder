// The names of the designer's keys as its texts show them: Ctrl (Strg in German) on Windows and Linux, Cmd on a Mac,
// where the designer's shortcuts take Cmd in its place (ROADMAP 9.3c).

/** Whether the backoffice runs on a Mac or an iPad, where Cmd does what Ctrl does elsewhere. */
export const onMac = (agent = navigator.userAgent): boolean => /Macintosh|Mac OS X|iPhone|iPad/.test(agent);

/** The term of the key the shortcuts hold down: Cmd on a Mac, else Ctrl. */
export const modifierTerm = (mac = onMac()): string => (mac ? 'jfbDesigner_keyCmd' : 'jfbDesigner_keyCtrl');
