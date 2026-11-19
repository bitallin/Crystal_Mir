import type { Point } from "../protocol/types";

export interface MapCell {
  backIndex: number;
  backImage: number;
  middleIndex: number;
  middleImage: number;
  frontIndex: number;
  frontImage: number;
  doorIndex: number;
  doorOffset: number;
  frontAnimationFrame: number;
  frontAnimationTick: number;
  middleAnimationFrame: number;
  middleAnimationTick: number;
  tileAnimationImage: number;
  tileAnimationOffset: number;
  tileAnimationFrames: number;
  light: number;
  walkable: boolean;
  fishing: boolean;
}

export const CELL_WIDTH = 48;
export const CELL_HEIGHT = 32;
export const MAP_CELL_STRIDE = 32;

export function decodeMapCells(bytes: Uint8Array, stride = MAP_CELL_STRIDE): MapCell[] {
  if (stride !== MAP_CELL_STRIDE) throw new Error(`Unsupported map cell stride ${stride}`);
  if (bytes.byteLength % stride !== 0) throw new Error("Map cells.bin has a partial cell");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const cells: MapCell[] = new Array(bytes.byteLength / stride);
  for (let index = 0; index < cells.length; index++) {
    const offset = index * stride;
    cells[index] = {
      backIndex: view.getInt16(offset, true),
      backImage: view.getInt32(offset + 2, true),
      middleIndex: view.getInt16(offset + 6, true),
      middleImage: view.getInt32(offset + 8, true),
      frontIndex: view.getInt16(offset + 12, true),
      frontImage: view.getInt32(offset + 14, true),
      doorIndex: view.getUint8(offset + 18),
      doorOffset: view.getUint8(offset + 19),
      frontAnimationFrame: view.getUint8(offset + 20),
      frontAnimationTick: view.getUint8(offset + 21),
      middleAnimationFrame: view.getUint8(offset + 22),
      middleAnimationTick: view.getUint8(offset + 23),
      tileAnimationImage: view.getInt16(offset + 24, true),
      tileAnimationOffset: view.getInt16(offset + 26, true),
      tileAnimationFrames: view.getUint8(offset + 28),
      light: view.getUint8(offset + 29),
      walkable: view.getUint8(offset + 30) !== 0,
      fishing: view.getUint8(offset + 31) !== 0
    };
  }
  return cells;
}

export function cellAt(cells: readonly MapCell[], width: number, height: number, x: number, y: number): MapCell | undefined {
  if (x < 0 || y < 0 || x >= width || y >= height) return undefined;
  return cells[x * height + y];
}

export function cellIndex(width: number, height: number, point: Point): number {
  return point.x * height + point.y;
}

export function isWalkable(cells: readonly MapCell[], width: number, height: number, point: Point): boolean {
  return cellAt(cells, width, height, point.x, point.y)?.walkable ?? false;
}

const DIRECTIONS: ReadonlyArray<Point & { direction: number }> = [
  { x: 0, y: -1, direction: 0 },
  { x: 1, y: -1, direction: 1 },
  { x: 1, y: 0, direction: 2 },
  { x: 1, y: 1, direction: 3 },
  { x: 0, y: 1, direction: 4 },
  { x: -1, y: 1, direction: 5 },
  { x: -1, y: 0, direction: 6 },
  { x: -1, y: -1, direction: 7 }
];

export function directionTo(from: Point, to: Point): number {
  const dx = Math.sign(to.x - from.x);
  const dy = Math.sign(to.y - from.y);
  const match = DIRECTIONS.find(value => value.x === dx && value.y === dy);
  return match?.direction ?? 4;
}

export function nextPoint(from: Point, direction: number, distance = 1): Point {
  const value = DIRECTIONS[direction] ?? DIRECTIONS[4]!;
  return { x: from.x + value.x * distance, y: from.y + value.y * distance };
}

export function findPath(
  cells: readonly MapCell[],
  width: number,
  height: number,
  start: Point,
  target: Point,
  maxNodes = 4096
): number[] {
  if (!isWalkable(cells, width, height, target)) return [];
  if (start.x === target.x && start.y === target.y) return [];
  const startIndex = cellIndex(width, height, start);
  const targetIndex = cellIndex(width, height, target);
  const queue: number[] = [startIndex];
  const previous = new Int32Array(width * height);
  previous.fill(-1);
  previous[startIndex] = startIndex;
  let visited = 0;
  for (let cursor = 0; cursor < queue.length && visited < maxNodes; cursor++) {
    const currentIndex = queue[cursor]!;
    if (currentIndex === targetIndex) break;
    const x = Math.floor(currentIndex / height);
    const y = currentIndex % height;
    for (const direction of DIRECTIONS) {
      const nx = x + direction.x;
      const ny = y + direction.y;
      if (!isWalkable(cells, width, height, { x: nx, y: ny })) continue;
      const next = nx * height + ny;
      if (previous[next] !== -1) continue;
      previous[next] = currentIndex;
      queue.push(next);
      visited++;
    }
  }
  if (previous[targetIndex] === -1) return [];

  const path: number[] = [];
  let current = targetIndex;
  while (current !== startIndex) {
    const prior = previous[current]!;
    const x = Math.floor(current / height);
    const y = current % height;
    const px = Math.floor(prior / height);
    const py = prior % height;
    path.push(directionTo({ x: px, y: py }, { x, y }));
    current = prior;
  }
  return path.reverse();
}
