import { CELL_HEIGHT, CELL_WIDTH } from "../world/map";
import type { Point } from "../protocol/types";

export interface CameraTransform {
  x: number;
  y: number;
  zoom: number;
}

export interface Viewport {
  width: number;
  height: number;
}

export function cellCenter(point: Point): { x: number; y: number } {
  return {
    x: point.x * CELL_WIDTH + CELL_WIDTH / 2,
    y: point.y * CELL_HEIGHT + CELL_HEIGHT / 2
  };
}

export function worldToScreen(point: { x: number; y: number }, camera: CameraTransform): { x: number; y: number } {
  return {
    x: point.x * camera.zoom + camera.x,
    y: point.y * camera.zoom + camera.y
  };
}

export function screenToWorld(point: { x: number; y: number }, camera: CameraTransform): { x: number; y: number } {
  return {
    x: (point.x - camera.x) / camera.zoom,
    y: (point.y - camera.y) / camera.zoom
  };
}

export function screenToCell(point: { x: number; y: number }, camera: CameraTransform): Point {
  const world = screenToWorld(point, camera);
  return {
    x: Math.floor(world.x / CELL_WIDTH),
    y: Math.floor(world.y / CELL_HEIGHT)
  };
}

export function cameraForViewport(viewport: Viewport, focus: Point, zoom: number): CameraTransform {
  const center = cellCenter(focus);
  return {
    x: viewport.width / 2 - center.x * zoom,
    y: viewport.height * 0.56 - center.y * zoom,
    zoom
  };
}

export function preferredZoom(viewport: Viewport): number {
  const fitted = Math.min(viewport.width / 1100, viewport.height / 720);
  return Math.max(0.78, Math.min(1.3, fitted));
}

export function visibleCellBounds(camera: CameraTransform, viewport: Viewport, padding = 3): {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
} {
  const topLeft = screenToWorld({ x: 0, y: 0 }, camera);
  const bottomRight = screenToWorld({ x: viewport.width, y: viewport.height }, camera);
  return {
    minX: Math.floor(topLeft.x / CELL_WIDTH) - padding,
    minY: Math.floor(topLeft.y / CELL_HEIGHT) - padding,
    maxX: Math.ceil(bottomRight.x / CELL_WIDTH) + padding,
    maxY: Math.ceil(bottomRight.y / CELL_HEIGHT) + padding
  };
}
