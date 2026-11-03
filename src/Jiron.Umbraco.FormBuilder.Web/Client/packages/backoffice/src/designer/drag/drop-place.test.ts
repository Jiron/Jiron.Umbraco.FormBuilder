// Where a dragged element lands (ADR-0169): the place among boxes in reading order, the line that shows it, and the
// container under the pointer.
import { describe, expect, it } from 'vitest';
import { containerAt, holds, indexAt, LINE_GAP, lineAt, type Box } from './drop-place';

const box = (left: number, top: number, right: number, bottom: number): Box => ({ left, top, right, bottom });

// Three fields one below the other, 100 px wide and 40 px high, 20 px apart.
const column = [box(0, 0, 100, 40), box(0, 60, 100, 100), box(0, 120, 100, 160)];

// Two rows of two fields side by side.
const grid = [box(0, 0, 100, 40), box(120, 0, 220, 40), box(0, 60, 100, 100), box(120, 60, 220, 100)];

describe('the place a point falls at', () => {
  it('is before the first box below it, in a column', () => {
    expect(indexAt(column, 50, -5)).toBe(0);
    expect(indexAt(column, 50, 50)).toBe(1);
    expect(indexAt(column, 50, 110)).toBe(2);
  });

  it('is before a box alone in its row above its middle, and after it below, wherever the point is across', () => {
    expect(indexAt(column, 90, 10)).toBe(0);
    expect(indexAt(column, 10, 30)).toBe(1);
    expect(indexAt(column, 10, 95)).toBe(2);
  });

  it('follows reading order across fields side by side, by their middles across', () => {
    expect(indexAt(grid, 10, 20)).toBe(0);
    expect(indexAt(grid, 150, 20)).toBe(1);
    expect(indexAt(grid, 200, 20)).toBe(2);
    expect(indexAt(grid, 150, 80)).toBe(3);
    expect(indexAt(grid, 200, 80)).toBe(4);
  });

  it('judges a row among all the boxes, so a field dragged out of a row of two leaves the other in a row', () => {
    const left = box(0, 0, 100, 40);
    const dragged = box(120, 0, 220, 40);
    // Over the upper half of the dragged field's own place, right of the other's middle: after the other, where it was.
    expect(indexAt([left], 170, 10, false, [left, dragged])).toBe(1);
    // Alone in its row, the other would be split by its middle down: before it.
    expect(indexAt([left], 170, 10)).toBe(0);
  });

  it('splits boxes that stand in rows by their middles across, one alone too', () => {
    const steps = [box(0, 0, 100, 40)];
    expect(indexAt(steps, 40, 30, true)).toBe(0);
    expect(indexAt(steps, 60, 10, true)).toBe(1);
  });

  it('is the end below every box, and the start of none', () => {
    expect(indexAt(column, 50, 400)).toBe(3);
    expect(indexAt([], 50, 50)).toBe(0);
  });
});

describe('the line that shows a place', () => {
  const container = box(-10, -10, 110, 170);

  it('lies above the box the place comes before, in the gap', () => {
    expect(lineAt(column, 1, container)).toEqual({ left: 0, right: 100, y: 60 - LINE_GAP });
  });

  it('lies below the last box for the end', () => {
    expect(lineAt(column, 3, container)).toEqual({ left: 0, right: 100, y: 160 + LINE_GAP });
  });

  it('lies along the top of a container that holds none', () => {
    expect(lineAt([], 0, container)).toEqual({ left: -10, right: 110, y: -10 + LINE_GAP });
  });
});

describe('the container a point is over', () => {
  const group = { name: 'group', box: box(0, 0, 400, 300) };
  const row = { name: 'row', box: box(20, 100, 380, 200) };
  const other = { name: 'other', box: box(0, 400, 400, 600) };

  it('is the innermost that holds it, as a repeatable group lies in its group', () => {
    expect(containerAt([group, row, other], 50, 150)?.name).toBe('row');
    expect(containerAt([group, row, other], 50, 50)?.name).toBe('group');
  });

  it('is the nearest above or below it when none holds it, so a gap between groups still lands', () => {
    expect(containerAt([group, other], 50, 320)?.name).toBe('group');
    expect(containerAt([group, other], 50, 390)?.name).toBe('other');
  });

  it('is none without containers', () => {
    expect(containerAt([], 0, 0)).toBeNull();
  });

  it('holds a point on its edge', () => {
    expect(holds(group.box, 400, 300)).toBe(true);
    expect(holds(group.box, 401, 300)).toBe(false);
  });
});
