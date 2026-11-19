import { describe, expect, it } from "vitest";
import { CELL_HEIGHT, CELL_WIDTH } from "../world/map";
import { cameraForViewport, cellCenter, preferredZoom, screenToCell, screenToWorld, visibleCellBounds, worldToScreen } from "./projection";

describe("camera projection", () => {
  it("round-trips world and screen coordinates", () => {
    const camera = { x: 32, y: -18, zoom: 1.25 };
    const world = { x: 480, y: 320 };
    expect(screenToWorld(worldToScreen(world, camera), camera)).toEqual(world);
  });

  it("centers the focus cell in the viewport", () => {
    const viewport = { width: 1440, height: 900 };
    const focus = { x: 12, y: 9 };
    const camera = cameraForViewport(viewport, focus, 1);
    const center = cellCenter(focus);
    expect(worldToScreen(center, camera).x).toBeCloseTo(viewport.width / 2);
    expect(worldToScreen(center, camera).y).toBeCloseTo(viewport.height * 0.56);
    expect(screenToCell({ x: viewport.width / 2, y: viewport.height * 0.56 }, camera)).toEqual(focus);
  });

  it("computes a useful visible cell rectangle with padding", () => {
    const viewport = { width: 960, height: 640 };
    const camera = cameraForViewport(viewport, { x: 20, y: 20 }, 1);
    const bounds = visibleCellBounds(camera, viewport, 2);
    expect(bounds.minX).toBeLessThan(20);
    expect(bounds.maxX).toBeGreaterThan(20);
    expect(bounds.maxX - bounds.minX).toBeGreaterThan(Math.floor(viewport.width / CELL_WIDTH));
    expect(bounds.maxY - bounds.minY).toBeGreaterThan(Math.floor(viewport.height / CELL_HEIGHT));
  });

  it("keeps mobile zoom readable and desktop zoom bounded", () => {
    expect(preferredZoom({ width: 375, height: 812 })).toBeGreaterThanOrEqual(0.78);
    expect(preferredZoom({ width: 2560, height: 1440 })).toBeLessThanOrEqual(1.3);
  });
});
