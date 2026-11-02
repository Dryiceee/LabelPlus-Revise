/// <reference path="qc_format.ts" />
namespace LabelPlus {
// Geometry is calculated in JavaScript before calling Photoshop. Every polygon
// here is convex; outer polygons also define the footprint used for batching.
export interface QcPaintShape {
    outer: number[][][];
    inner: number[][][];
    bounds: QcNoteRect;
}

function qcPolygonBounds(polygons: number[][][]): QcNoteRect {
    let left = Number.MAX_VALUE, top = Number.MAX_VALUE, right = -Number.MAX_VALUE, bottom = -Number.MAX_VALUE;
    for (let i = 0; i < polygons.length; i++)
        for (let j = 0; j < polygons[i].length; j++) {
            let point = polygons[i][j];
            left = Math.min(left, point[0]); top = Math.min(top, point[1]);
            right = Math.max(right, point[0]); bottom = Math.max(bottom, point[1]);
        }
    return { x: left, y: top, width: right - left, height: bottom - top };
}

export function qcArrowShape(issue: QcIssue, w: number, h: number, size: number): QcPaintShape {
    let x = issue.x * w, y = issue.y * h, tx = issue.tailX * w, ty = issue.tailY * h;
    let dx = x - tx, dy = y - ty, length = Math.sqrt(dx * dx + dy * dy);
    let angle = Math.atan2(dy, dx), thickness = Math.max(2, size * .12);
    function outline(width: number, headLength: number): number[][][] {
        let polygons: number[][][] = [];
        if (length >= .5) {
            let nx = -dy / length * width / 2, ny = dx / length * width / 2;
            polygons.push([[tx + nx, ty + ny], [x + nx, y + ny], [x - nx, y - ny], [tx - nx, ty - ny]]);
        }
        polygons.push([[x, y], [x - headLength * Math.cos(angle - .48), y - headLength * Math.sin(angle - .48)],
            [x - headLength * Math.cos(angle + .48), y - headLength * Math.sin(angle + .48)]]);
        return polygons;
    }
    let outer = outline(thickness + 3, size * .95), inner = outline(thickness, size * .8);
    return { outer: outer, inner: inner, bounds: qcPolygonBounds(outer) };
}

function qcRectanglePolygon(x: number, y: number, width: number, height: number): number[][] {
    return [[x, y], [x + width, y], [x + width, y + height], [x, y + height]];
}

export function qcBoxShape(rect: QcNoteRect, border: number): QcPaintShape {
    let outer = [qcRectanglePolygon(rect.x, rect.y, rect.width, rect.height)];
    let inner = [qcRectanglePolygon(rect.x + border, rect.y + border,
        Math.max(1, rect.width - 2 * border), Math.max(1, rect.height - 2 * border))];
    return { outer: outer, inner: inner, bounds: rect };
}

function qcRectanglesApart(a: QcNoteRect, b: QcNoteRect, gap: number): boolean {
    return a.x + a.width + gap < b.x || b.x + b.width + gap < a.x ||
        a.y + a.height + gap < b.y || b.y + b.height + gap < a.y;
}

// Separating axes avoid treating the entire bounding rectangle of a diagonal
// arrow as painted. A small gap keeps antialiased edges in separate batches.
function qcPolygonsApart(a: number[][], b: number[][], gap: number): boolean {
    for (let pass = 0; pass < 2; pass++) {
        let polygon = pass === 0 ? a : b;
        for (let i = 0; i < polygon.length; i++) {
            let point = polygon[i], next = polygon[(i + 1) % polygon.length];
            let nx = -(next[1] - point[1]), ny = next[0] - point[0];
            let length = Math.sqrt(nx * nx + ny * ny);
            if (length < .000001) continue;
            nx /= length; ny /= length;
            let minA = Number.MAX_VALUE, maxA = -Number.MAX_VALUE, minB = Number.MAX_VALUE, maxB = -Number.MAX_VALUE;
            for (let j = 0; j < a.length; j++) {
                let value = a[j][0] * nx + a[j][1] * ny;
                minA = Math.min(minA, value); maxA = Math.max(maxA, value);
            }
            for (let j = 0; j < b.length; j++) {
                let value = b[j][0] * nx + b[j][1] * ny;
                minB = Math.min(minB, value); maxB = Math.max(maxB, value);
            }
            if (maxA + gap < minB || maxB + gap < minA) return true;
        }
    }
    return false;
}

function qcPaintShapesOverlap(a: QcPaintShape, b: QcPaintShape): boolean {
    let gap = 2;
    if (qcRectanglesApart(a.bounds, b.bounds, gap)) return false;
    for (let i = 0; i < a.outer.length; i++)
        for (let j = 0; j < b.outer.length; j++)
            if (!qcPolygonsApart(a.outer[i], b.outer[j], gap)) return true;
    return false;
}

export function qcPaintBatches(shapes: QcPaintShape[]): number[][] {
    let batches: number[][] = [], current: number[] = [];
    for (let i = 0; i < shapes.length; i++) {
        // Bound each path's size as well as preserving original paint order.
        let split = current.length >= 64;
        for (let j = 0; !split && j < current.length; j++)
            split = qcPaintShapesOverlap(shapes[current[j]], shapes[i]);
        if (split) { batches.push(current); current = []; }
        current.push(i);
    }
    if (current.length) batches.push(current);
    return batches;
}
}
