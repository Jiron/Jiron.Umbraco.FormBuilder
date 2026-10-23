/**
 * The shared logic (ROADMAP section 4.2, ADR-0005): rules, validation, merge tags and calculations, written once for
 * the website runtime, the headless element and the backoffice, and tested against the shared vectors in
 * tests/vectors. It has no DOM and no dependencies: its compiler settings leave out the DOM's types, and the lint
 * refuses imports from anywhere else. The engines arrive in Phase 5.
 */
export {};
