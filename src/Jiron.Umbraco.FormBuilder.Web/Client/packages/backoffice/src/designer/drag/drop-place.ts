// Where a dragged element lands (ADR-0169): the place a point falls at among the elements of a container, in reading
// order, which also holds for fields side by side in columns; the line that shows that place; and the container a point
// is over. Pure arithmetic over boxes, so the browser's layout is read once and the rest is tested without it.

/** A box in the window's coordinates, as `getBoundingClientRect` gives one. */
export interface Box {
  readonly left: number;
  readonly top: number;
  readonly right: number;
  readonly bottom: number;
}

/** A line that shows a place: where it starts and ends across, and how far down it is. */
export interface Line {
  readonly left: number;
  readonly right: number;
  readonly y: number;
}

/** How far the line lies outside the box it marks, in the gap beside it. */
export const LINE_GAP = 6;

/**
 * The place a point falls at among boxes in reading order: before the first box it comes before, which is a box below
 * it, a box beside others in its row whose middle is to its right, or a box alone in its row whose middle is below it;
 * after the last box when it comes before none. Boxes that always stand in rows, as the page's steps do, are split by
 * their middles across even when one is alone. Whether a box shares its row is judged among `all` the boxes, which
 * hold the dragged one too: a field dragged out of a row of two leaves the other still in a row.
 */
export function indexAt(
  boxes: readonly Box[],
  x: number,
  y: number,
  rows = false,
  all: readonly Box[] = boxes,
): number {
  for (let at = 0; at < boxes.length; at++) {
    const box = boxes[at];
    if (!box) continue;
    if (y < box.top) return at;
    if (y > box.bottom) continue;
    const beside = rows || all.some((other) => other !== box && other.top < box.bottom && other.bottom > box.top);
    if (beside ? x < (box.left + box.right) / 2 : y < (box.top + box.bottom) / 2) return at;
  }
  return boxes.length;
}

/**
 * The line that shows a place among boxes: above the box the place comes before, below the last box for the end, and
 * along the top of the container when it holds no box.
 */
export function lineAt(boxes: readonly Box[], index: number, container: Box): Line {
  const before = boxes[index];
  if (before) return { left: before.left, right: before.right, y: before.top - LINE_GAP };
  const last = boxes[boxes.length - 1];
  if (last) return { left: last.left, right: last.right, y: last.bottom + LINE_GAP };
  return { left: container.left, right: container.right, y: container.top + LINE_GAP };
}

/** Whether a point lies in a box. */
export const holds = (box: Box, x: number, y: number) =>
  x >= box.left && x <= box.right && y >= box.top && y <= box.bottom;

/**
 * The container a point is over: the innermost one that holds it, since a repeatable group's row lies inside a group;
 * else the one nearest above or below it, so a point in the gap between two groups still lands somewhere. Containers come
 * outer before inner, as the document has them.
 */
export function containerAt<T extends { readonly box: Box }>(containers: readonly T[], x: number, y: number): T | null {
  let found: T | null = null;
  for (const container of containers) if (holds(container.box, x, y)) found = container;
  if (found) return found;
  let nearest: T | null = null;
  let distance = Infinity;
  for (const container of containers) {
    const { top, bottom } = container.box;
    const away = y < top ? top - y : y > bottom ? y - bottom : 0;
    if (away < distance) {
      distance = away;
      nearest = container;
    }
  }
  return nearest;
}
