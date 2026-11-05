// How long a redirect after a message counts down (ADR-0120, ADR-0187): the runtime counts it, and the backoffice's
// preview of an outcome says the same seconds.

/** The fewest seconds a redirect after a message counts down: a shorter time leaves none to reach "Stay here" (WCAG 2.2.1). */
export const LEAST_COUNTDOWN = 10;

/** The seconds a countdown starts from for the redirect's own delay; null for none, where "Go now" waits for the visitor. */
export function countdownFrom(delay: number): number | null {
  return delay > 0 ? Math.max(delay, LEAST_COUNTDOWN) : null;
}
