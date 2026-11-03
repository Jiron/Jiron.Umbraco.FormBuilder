// The designer's undo and redo (ROADMAP 9.3a and 4.13): each change to the definition is a step back with the selection
// it had; selecting alone and refused edits are none; a new change drops the redo; the oldest steps go past the limit.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition } from './definition';
import type { Edit, EditorState } from './editor-model';
import { EditorHistory } from './history';

const definition = (name: string): DesignDefinition => ({ pages: [{ key: name, groups: [] }] });
const changed = (name: string, selection: string | null = name): Edit => ({
  ok: true,
  state: { definition: definition(name), selection },
});
const START: EditorState = { definition: definition('start'), selection: null };

describe('the history', () => {
  it('undoes a change with the selection it had, and redoes it', () => {
    const history = new EditorHistory(START);
    history.apply(changed('one'), 'Moved Phone');

    expect(history.canUndo).toBe(true);
    expect(history.undoes).toBe('Moved Phone');
    expect(history.undo()).toBe('Moved Phone');
    expect(history.state).toBe(START);
    expect(history.canRedo).toBe(true);
    expect(history.redoes).toBe('Moved Phone');
    expect(history.redo()).toBe('Moved Phone');
    expect(history.state.definition).toEqual(definition('one'));
    expect(history.state.selection).toBe('one');
  });

  it('keeps no step for a selection alone or a refused edit', () => {
    const history = new EditorHistory(START);
    history.apply({ ok: true, state: { ...START, selection: 'start' } }, 'Selected');
    const refused = history.apply({ ok: false, refusal: 'cycle' }, 'Moved Email');

    expect(refused).toEqual({ ok: false, refusal: 'cycle' });
    expect(history.canUndo).toBe(false);
    expect(history.state.selection).toBe('start');
    expect(history.undo()).toBeUndefined();
    expect(history.redo()).toBeUndefined();
  });

  it('drops the redo when a new change follows an undo', () => {
    const history = new EditorHistory(START);
    history.apply(changed('one'), 'first');
    history.undo();
    history.apply(changed('two'), 'second');

    expect(history.canRedo).toBe(false);
    expect(history.undo()).toBe('second');
    expect(history.state).toBe(START);
  });

  it("takes a save's copy of the definition in the present's place, without a step and keeping the selection", () => {
    const history = new EditorHistory(START);
    history.apply(changed('one', 'one'), 'first');
    const saved = definition('one');
    history.rebase(saved);

    expect(history.state).toEqual({ definition: saved, selection: 'one' });
    expect(history.state.definition).toBe(saved);
    expect(history.undo()).toBe('first');
    expect(history.redo()).toBe('first');
    expect(history.state.definition).toBe(saved);
  });

  it('forgets the oldest changes past its limit, and starts over on a reset', () => {
    const history = new EditorHistory(START, 2);
    history.apply(changed('one'), 'first');
    history.apply(changed('two'), 'second');
    history.apply(changed('three'), 'third');

    expect(history.undo()).toBe('third');
    expect(history.undo()).toBe('second');
    expect(history.undo()).toBeUndefined();
    expect(history.state.definition).toEqual(definition('one'));

    history.reset(START);
    expect([history.canUndo, history.canRedo, history.state]).toEqual([false, false, START]);
  });
});
