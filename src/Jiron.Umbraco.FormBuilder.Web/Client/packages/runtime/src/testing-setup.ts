/**
 * Every test file of the runtime and the element runs with its forms covered as a browser covers them (testing-cover.ts),
 * so a plain read of a form's member in the website code fails its suite as it fails on the page.
 */
import { coverForms } from './testing-cover';

coverForms(window);
