import { describe, expect, it } from "vitest";
import type { MapCell } from "./map";
import { CELL_HEIGHT, CELL_WIDTH, MAP_CELL_STRIDE, cellAt, decodeMapCells, directionTo, findPath, isWalkable, nextPoint } from "./map";

function cell(index: number, walkable = true): MapCell {
  return {
    backIndex: 0,
    backImage: 1,
    middleIndex: 1,
    middleImage: index + 1,
    frontIndex: -1,
    frontImage: 0,
    doorIndex: 0,
    doorOffset: 0,
    frontAnimationFrame: 0,
    frontAnimationTick: 0,
    middleAnimationFrame: 0,
    middleAnimationTick: 0,
    tileAnimationImage: 0,
    tileAnimationOffset: 0,
    tileAnimationFrames: 0,
    light: index % 255,
    walkable,
    fishing: false
  };
}

function encode(cells: MapCell[]): Uint8Array {
  const bytes = new Uint8Array(cells.length * MAP_CELL_STRIDE);
  const view = new DataView(bytes.buffer);
  cells.forEach((value, index) => {
    const offset = index * MAP_CELL_STRIDE;
    view.setInt16(offset, value.backIndex, true);
    view.setInt32(offset + 2, value.backImage, true);
    view.setInt16(offset + 6, value.middleIndex, true);
    view.setInt32(offset + 8, value.middleImage, true);
    view.setInt16(offset + 12, value.frontIndex, true);
    view.setInt32(offset + 14, value.frontImage, true);
    view.setUint8(offset + 18, value.doorIndex);
    view.setUint8(offset + 19, value.doorOffset);
    view.setUint8(offset + 20, value.frontAnimationFrame);
    view.setUint8(offset + 21, value.frontAnimationTick);
    view.setUint8(offset + 22, value.middleAnimationFrame);
    view.setUint8(offset + 23, value.middleAnimationTick);
    view.setInt16(offset + 24, value.tileAnimationImage, true);
    view.setInt16(offset + 26, value.tileAnimationOffset, true);
    view.setUint8(offset + 28, value.tileAnimationFrames);
    view.setUint8(offset + 29, value.light);
    view.setUint8(offset + 30, value.walkable ? 1 : 0);
    view.setUint8(offset + 31, value.fishing ? 1 : 0);
  });
  return bytes;
}

describe("map cells", () => {
  it("decodes x-major cells at a 32-byte stride", () => {
    const source = [cell(1), cell(2, false), cell(3)];
    const decoded = decodeMapCells(encode(source));
    expect(decoded).toEqual(source);
    expect(MAP_CELL_STRIDE).toBe(32);
    expect(CELL_WIDTH).toBe(48);
    expect(CELL_HEIGHT).toBe(32);
  });

  it("rejects partial cells", () => {
    expect(() => decodeMapCells(new Uint8Array(31))).toThrow(/partial/);
  });
});

describe("map navigation", () => {
  const width = 7;
  const height = 7;
  const cells = Array.from({ length: width * height }, (_, index) => cell(index));
  const blocked = (x: number, y: number) => {
    cells[x * height + y] = cell(x * height + y, false);
  };

  it("uses x-major addressing and checks bounds", () => {
    expect(cellAt(cells, width, height, 2, 4)).toBe(cells[2 * height + 4]);
    expect(cellAt(cells, width, height, -1, 0)).toBeUndefined();
    expect(isWalkable(cells, width, height, { x: 3, y: 3 })).toBe(true);
  });

  it("maps coordinates to Crystal directions", () => {
    expect(directionTo({ x: 4, y: 4 }, { x: 4, y: 1 })).toBe(0);
    expect(directionTo({ x: 4, y: 4 }, { x: 6, y: 4 })).toBe(2);
    expect(directionTo({ x: 4, y: 4 }, { x: 1, y: 6 })).toBe(5);
    expect(nextPoint({ x: 4, y: 4 }, 7)).toEqual({ x: 3, y: 3 });
  });

  it("routes around obstacles and never crosses blocked cells", () => {
    blocked(3, 2);
    blocked(3, 3);
    blocked(3, 4);
    blocked(3, 5);
    const path = findPath(cells, width, height, { x: 1, y: 3 }, { x: 5, y: 3 });
    expect(path.length).toBeGreaterThan(0);
    let point = { x: 1, y: 3 };
    for (const direction of path) {
      point = nextPoint(point, direction);
      expect(isWalkable(cells, width, height, point)).toBe(true);
    }
    expect(point).toEqual({ x: 5, y: 3 });
  });

  it("returns no path for blocked destinations or disconnected regions", () => {
    blocked(6, 6);
    expect(findPath(cells, width, height, { x: 0, y: 0 }, { x: 6, y: 6 })).toEqual([]);
  });
});
