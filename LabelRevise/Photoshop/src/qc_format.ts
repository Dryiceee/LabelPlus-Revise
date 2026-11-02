/// <reference path="legacy.d.ts" />
namespace LabelPlus {
export interface QcIssue {
    id: number; x: number; y: number;
    tailX: number; tailY: number; noteX: number; noteY: number; text: string;
}
export interface QcPage { file: string; width: number; height: number; issues: QcIssue[]; }
export interface QcProject { kind: string; version: number; pages: QcPage[]; }
export interface QcMatch { page: QcPage; file: string; }
export interface QcPlan { matches: QcMatch[]; errors: string[]; skipped: number; }
export interface QcNoteRect { x: number; y: number; width: number; height: number; }

// Equal note/tail coordinates mean automatic placement. A separately dragged
// note keeps its explicit top-left coordinates, including in existing files.
export function placeQcNote(w: number, h: number, issue: QcIssue, width: number, height: number, gap: number): QcNoteRect {
    let tx = issue.tailX * w, ty = issue.tailY * h, hx = issue.x * w, hy = issue.y * h;
    function fit(x: number, y: number): QcNoteRect {
        return { x: Math.max(2, Math.min(x, w - width - 2)), y: Math.max(2, Math.min(y, h - height - 2)), width: width, height: height };
    }
    if (Math.abs(issue.noteX - issue.tailX) >= .000001 || Math.abs(issue.noteY - issue.tailY) >= .000001)
        return fit(issue.noteX * w, issue.noteY * h);
    let sides = [[tx - gap - width, ty - height / 2], [tx + gap, ty - height / 2],
        [tx - width / 2, ty - gap - height], [tx - width / 2, ty + gap]];
    let preferred = Math.abs(hx - tx) >= Math.abs(hy - ty) ? (hx >= tx ? 0 : 1) : (hy >= ty ? 2 : 3);
    let candidates = [sides[preferred]].concat(sides, [
        [tx - gap - width, ty - gap - height], [tx + gap, ty - gap - height],
        [tx - gap - width, ty + gap], [tx + gap, ty + gap],
        [2, 2], [w - width - 2, 2], [2, h - height - 2], [w - width - 2, h - height - 2]]);
    function overlap(rect: QcNoteRect): number {
        let low = 0, high = 1, padding = gap * .25;
        let start = [tx, ty], delta = [hx - tx, hy - ty];
        let minimum = [rect.x - padding, rect.y - padding], maximum = [rect.x + width + padding, rect.y + height + padding];
        for (let axis = 0; axis < 2; axis++) {
            if (Math.abs(delta[axis]) < .000001) {
                if (start[axis] < minimum[axis] || start[axis] > maximum[axis]) return -1;
            } else {
                let first = (minimum[axis] - start[axis]) / delta[axis], second = (maximum[axis] - start[axis]) / delta[axis];
                low = Math.max(low, Math.min(first, second)); high = Math.min(high, Math.max(first, second));
                if (low > high) return -1;
            }
        }
        return high - low;
    }
    let best = fit(sides[preferred][0], sides[preferred][1]), bestScore = Number.MAX_VALUE;
    for (let i = 0; i < candidates.length; i++) {
        let rect = fit(candidates[i][0], candidates[i][1]), amount = overlap(rect);
        if (amount < 0) return rect;
        let padding = gap * .25;
        let containsHead = hx >= rect.x - padding && hx <= rect.x + width + padding && hy >= rect.y - padding && hy <= rect.y + height + padding;
        let score = (containsHead ? 1e12 : 0) + amount * 1e6 + Math.pow(rect.x + width / 2 - tx, 2) + Math.pow(rect.y + height / 2 - ty, 2);
        if (score < bestScore) { bestScore = score; best = rect; }
    }
    return best;
}

function qcNumber(value: any, minimum: number, maximum: number): boolean {
    return typeof value === "number" && isFinite(value) && value >= minimum && value <= maximum;
}
export function parseQcText(text: string): QcProject {
    text = text.replace(/^\uFEFF/, "");
    let newline = text.indexOf("\n");
    if (newline < 0 || text.substring(0, newline).replace(/\r$/, "") !== "LabelPlus-QC 1")
        throw new Error("请选择 LabelPlus 监修模式导出的 revise.txt 返修单。");
    let data = jamJSON.parse(text.substring(newline + 1), true);
    if (!data || data.kind !== "labelplus-qc" || data.version !== 1 || !(data.pages instanceof Array))
        throw new Error("不支持的 QC 文件类型或版本。");
    let names: any = {}, stems: any = {};
    for (let i = 0; i < data.pages.length; i++) {
        let p = data.pages[i];
        if (!p || typeof p.file !== "string" || !p.file.length || /[\\\/:*?"<>|\x00-\x1f]/.test(p.file) ||
            p.file === "." || p.file === "..") throw new Error("无效的图片文件名。");
        let key = "$" + p.file.toLowerCase(), stem = "$" + qcStem(p.file);
        if (names[key] || stems[stem]) throw new Error("图片名重复，不能唯一匹配 PSD：" + p.file);
        names[key] = true; stems[stem] = true;
        if (!qcNumber(p.width, 1, 300000) || Math.floor(p.width) !== p.width ||
            !qcNumber(p.height, 1, 300000) || Math.floor(p.height) !== p.height || !(p.issues instanceof Array))
            throw new Error("图片尺寸或问题列表无效：" + p.file);
        let ids: any = {};
        for (let j = 0; j < p.issues.length; j++) {
            let issue = p.issues[j];
            if (!issue || !qcNumber(issue.id, 1, 2147483647) || Math.floor(issue.id) !== issue.id || ids["$" + issue.id] ||
                typeof issue.text !== "string") throw new Error("问题编号或说明无效：" + p.file);
            ids["$" + issue.id] = true;
            let coords = [issue.x, issue.y, issue.tailX, issue.tailY, issue.noteX, issue.noteY];
            for (let k = 0; k < coords.length; k++)
                if (!qcNumber(coords[k], 0, 1)) throw new Error("问题坐标无效：" + p.file + " #" + issue.id);
        }
    }
    return data;
}
export function qcStem(name: string): string {
    let dot = name.lastIndexOf(".");
    return (dot > 0 ? name.substring(0, dot) : name).toLowerCase();
}
export function planQcImport(project: QcProject, filenames: string[]): QcPlan {
    let result: QcPlan = { matches: [], errors: [], skipped: 0 };
    let map: any = {};
    for (let i = 0; i < filenames.length; i++) {
        if (!/\.psd$/i.test(filenames[i])) continue;
        let key = "$" + qcStem(filenames[i]);
        if (!map[key]) map[key] = [];
        map[key].push(filenames[i]);
    }
    for (let i = 0; i < project.pages.length; i++) {
        let page = project.pages[i];
        if (!page.issues.length) { result.skipped++; continue; }
        let empty = false;
        for (let j = 0; j < page.issues.length; j++)
            if (!/\S/.test(page.issues[j].text)) empty = true;
        if (empty) { result.errors.push(page.file + "：有空白问题说明，请先填写。"); continue; }
        let matches = map["$" + qcStem(page.file)];
        if (!matches || !matches.length) result.errors.push(page.file + "：未找到同名 PSD。");
        else if (matches.length !== 1) result.errors.push(page.file + "：同名 PSD 不唯一。");
        else result.matches.push({ page: page, file: matches[0] });
    }
    return result;
}

// Explicit wrapping keeps the note on the page and works with CJK and Latin text.
export function wrapQcText(text: string, maxUnits: number): string {
    text = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
    let output = "", lineUnits = 0;
    for (let i = 0; i < text.length; i++) {
        let ch = text.charAt(i), code = text.charCodeAt(i);
        if (ch === "\n") { output += "\r"; lineUnits = 0; continue; }
        let unit = code > 255 ? 1 : .62;
        // Never split a UTF-16 surrogate pair between lines.
        if (lineUnits + unit > maxUnits && lineUnits > 0 && !(code >= 0xDC00 && code <= 0xDFFF)) {
            output += "\r"; lineUnits = 0;
        }
        output += ch; lineUnits += unit;
    }
    return output;
}
}
