// Whether a duplicate may take its form's actions (ADR-0166): the server asks for manageActions where the copy lands, which
// is the form's folder, or at the top level the user's own permissions; the dialog offers the actions only then.
import { describe, expect, it } from 'vitest';
import { mayTakeActions } from './duplicate-form.action';

const managing = { permissions: ['design', 'manageActions'] };
const designing = { permissions: ['design'] };

describe("a duplicate's actions", () => {
  it("come along where the copy's folder lets the user manage actions", () => {
    expect(mayTakeActions('f', managing, { global: [] })).toBe(true);
    expect(mayTakeActions('f', designing, { global: ['manageActions'] })).toBe(false);
  });

  it("come along at the top level by the user's own permissions", () => {
    expect(mayTakeActions(null, undefined, { global: ['design', 'manageActions'] })).toBe(true);
    expect(mayTakeActions(undefined, managing, { global: ['design'] })).toBe(false);
  });

  it('stay behind when the folder or the permissions did not read', () => {
    expect(mayTakeActions('f', undefined, { global: ['manageActions'] })).toBe(false);
    expect(mayTakeActions(null, undefined, undefined)).toBe(false);
  });
});
