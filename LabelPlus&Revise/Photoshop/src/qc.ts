/// <reference path="qc_paths.ts" />
namespace LabelPlus {
export interface QcPageTiming {
    file: string; issues: number; completed: boolean;
    openMs: number; arrowMs: number; textMs: number; backgroundMs: number; mergeMs: number; totalMs: number;
    arrowBatches: number; backgroundBatches: number; pathFills: number;
}
export interface QcBatchResult { completed: string[]; errors: string[]; cancelled: boolean; timings: QcPageTiming[]; }
interface QcTextBox { layer: ArtLayer; shape: QcPaintShape; }
let qcJob: { doc: Document; page: QcPage; font: string; timing: QcPageTiming } | null = null;
let qcPathSerial = 0;

function qcNow(): number { return (new Date()).getTime(); }

function qcColor(r: number, g: number, b: number): SolidColor {
    let color = new SolidColor(); color.rgb.red = r; color.rgb.green = g; color.rgb.blue = b; return color;
}
function qcFillPath(doc: Document, polygons: number[][][], color: SolidColor, timing: QcPageTiming): void {
    if (!polygons.length) return;
    // Legacy PathPointInfo coordinates are points, regardless of rulerUnits.
    // Repair-sheet geometry uses pixels; convert so 300-DPI PSDs stay aligned.
    let pointsPerPixel = 72 / doc.resolution;
    let subpaths: SubPathInfo[] = [];
    for (let i = 0; i < polygons.length; i++) {
        let points: PathPointInfo[] = [];
        for (let j = 0; j < polygons[i].length; j++) {
            let point = new PathPointInfo(), xy = polygons[i][j];
            let x = xy[0] * pointsPerPixel, y = xy[1] * pointsPerPixel;
            point.kind = PointKind.CORNERPOINT;
            // The upstream declarations incorrectly type anchor as number[][].
            (<any>point).anchor = [x, y];
            point.leftDirection = [x, y]; point.rightDirection = [x, y];
            points.push(point);
        }
        let subpath = new SubPathInfo(); subpath.closed = true; subpath.operation = ShapeOperation.SHAPEADD;
        subpath.entireSubPath = <any>points; subpaths.push(subpath);
    }
    // Photoshop creates a Work Path internally before naming it. The render
    // transaction backs up the user's Work Path and restores it afterwards.
    let name = "__LabelPlus_QC_path_" + qcNow() + "_" + (++qcPathSerial);
    let path = doc.pathItems.add(name, subpaths);
    try {
        path.fillPath(color, ColorBlendMode.NORMAL, 100, false, 0, true, true);
        timing.pathFills++;
    } finally { path.remove(); }
}

function qcPaintBatch(doc: Document, shapes: QcPaintShape[], batch: number[],
    outerColor: SolidColor, innerColor: SolidColor, timing: QcPageTiming): void {
    let outer: number[][][] = [], inner: number[][][] = [];
    for (let i = 0; i < batch.length; i++) {
        let shape = shapes[batch[i]];
        outer = outer.concat(shape.outer); inner = inner.concat(shape.inner);
    }
    qcFillPath(doc, outer, outerColor, timing);
    qcFillPath(doc, inner, innerColor, timing);
}

function qcWorkPathContents(doc: Document): ActionList | null {
    for (let i = 0; i < doc.pathItems.length; i++) {
        if (doc.pathItems[i].kind !== PathKind.WORKPATH) continue;
        let reference = new ActionReference();
        reference.putProperty(app.charIDToTypeID("Path"), app.charIDToTypeID("WrPt"));
        return app.executeActionGet(reference).getObjectValue(app.stringIDToTypeID("pathContents"))
            .getList(app.stringIDToTypeID("pathComponents"));
    }
    return null;
}

function qcSelectedPathName(): string | null {
    try {
        let reference = new ActionReference();
        reference.putEnumerated(app.charIDToTypeID("Path"), app.charIDToTypeID("Ordn"), app.charIDToTypeID("Trgt"));
        return app.executeActionGet(reference).getString(app.charIDToTypeID("PthN"));
    } catch (noSelectedPath) { return null; }
}

function qcRestoreWorkPath(doc: Document, contents: ActionList | null, selectedName: string | null): void {
    if (contents) {
        let reference = new ActionReference(), descriptor = new ActionDescriptor();
        reference.putProperty(app.charIDToTypeID("Path"), app.charIDToTypeID("WrPt"));
        descriptor.putReference(app.charIDToTypeID("null"), reference);
        descriptor.putList(app.charIDToTypeID("T   "), contents);
        app.executeAction(app.charIDToTypeID("setd"), descriptor, DialogModes.NO);
    }
    // Selection restoration is best-effort for special paths such as masks;
    // restoring the actual Work Path geometry above must succeed or roll back.
    try {
        if (selectedName) doc.pathItems.getByName(selectedName).select();
        else if (contents)
            for (let i = 0; i < doc.pathItems.length; i++)
                if (doc.pathItems[i].kind === PathKind.WORKPATH) { doc.pathItems[i].deselect(); break; }
    } catch (specialPathSelection) { }
}
function qcFont(): string {
    let candidates = ["MicrosoftYaHei", "MicrosoftYaHeiUI", "SimHei", "SimSun", "AdobeHeitiStd-Regular", "NotoSansCJKsc-Regular", "SourceHanSansSC-Regular"];
    for (let i = 0; i < candidates.length; i++)
        try { return app.fonts.getByName(candidates[i]).postScriptName; } catch (ignored) { }
    throw new Error("未找到支持中文的标注字体，请安装微软雅黑、黑体或思源黑体后重试。");
}

function qcTextBox(doc: Document, group: LayerSet, text: string, font: string, size: number,
    issue: QcIssue, maxWidth: number, name: string): QcTextBox {
    let w = doc.width.as("px"), h = doc.height.as("px"), pad = size * .4;
    let usable = Math.max(1, maxWidth - 2 * pad);
    let wrapped = wrapQcText(text, usable / size);
    let layer = group.artLayers.add(); layer.kind = LayerKind.TEXT; layer.name = name;
    let item = layer.textItem;
    item.kind = TextType.POINTTEXT; item.font = font;
    item.size = UnitValue(size * 72 / doc.resolution, "pt");
    item.color = qcColor(222, 44, 56); item.direction = Direction.HORIZONTAL;
    item.justification = Justification.LEFT;
    item.useAutoLeading = false; item.leading = UnitValue(size * 1.35 * 72 / doc.resolution, "pt");
    item.position = [UnitValue(0, "px"), UnitValue(size, "px")]; item.contents = wrapped;
    let bounds = layer.bounds;
    let bw = bounds[2].as("px") - bounds[0].as("px"), bh = bounds[3].as("px") - bounds[1].as("px");
    if (bw + 2 * pad > w - 2 || bh + 2 * pad > h - 2)
        throw new Error("问题说明超出画布，请缩短或拆分说明：" + name);
    let boxW = Math.max(size, bw) + 2 * pad, boxH = Math.max(size, bh) + 2 * pad;
    let rect = placeQcNote(w, h, issue, boxW, boxH, size * .6);
    let x = rect.x, y = rect.y;
    layer.translate(UnitValue(x + pad - bounds[0].as("px"), "px"), UnitValue(y + pad - bounds[1].as("px"), "px"));
    return { layer: layer, shape: qcBoxShape(rect, Math.max(1, size * .075)) };
}

// Called synchronously by Photoshop's suspendHistory in the same ExtendScript engine.
export function qcRenderActive(): void {
    if (!qcJob) throw new Error("QC import job missing");
    let doc = qcJob.doc, page = qcJob.page, font = qcJob.font, timing = qcJob.timing;
    let w = doc.width.as("px"), h = doc.height.as("px");
    let size = Math.max(16, Math.min(52, w * .018));
    let oldChannels = doc.activeChannels;
    let savedSelection: Channel | null = null;
    let savedWorkPath = qcWorkPathContents(doc), selectedPathName = qcSelectedPathName();
    try {
        try {
            let selectedBounds = doc.selection.bounds;
            if (selectedBounds) {
                savedSelection = doc.channels.add(); savedSelection.name = "__labelplus_qc_selection";
                doc.selection.store(savedSelection);
            }
        } catch (noSelection) { if (savedSelection) { savedSelection.remove(); savedSelection = null; } }
        doc.activeChannels = doc.componentChannels;
        doc.selection.deselect();
        let group = doc.layerSets.add(); group.name = "__LabelPlus_QC_pending";
        group.blendMode = BlendMode.NORMAL;
        let arrows = group.artLayers.add(); arrows.name = "QC arrows";
        doc.activeLayer = arrows;
        let red = qcColor(222, 44, 56), white = qcColor(255, 255, 255), started = qcNow();
        let shapes: QcPaintShape[] = [], notes: QcTextBox[] = [];
        try {
            for (let i = 0; i < page.issues.length; i++) shapes.push(qcArrowShape(page.issues[i], w, h, size));
            let batches = qcPaintBatches(shapes); timing.arrowBatches = batches.length;
            for (let i = 0; i < batches.length; i++) qcPaintBatch(doc, shapes, batches[i], white, red, timing);
        } finally { timing.arrowMs = qcNow() - started; }
        started = qcNow();
        try {
            for (let i = 0; i < page.issues.length; i++) {
                let issue = page.issues[i];
                notes.push(qcTextBox(doc, group, "#" + issue.id + "  " + issue.text, font, size,
                    issue, Math.min(w - 4, Math.max(100, w * .30)), "QC #" + issue.id));
            }
        } finally { timing.textMs = qcNow() - started; }
        started = qcNow();
        try {
            shapes = [];
            for (let i = 0; i < notes.length; i++) shapes.push(notes[i].shape);
            let batches = qcPaintBatches(shapes); timing.backgroundBatches = batches.length;
            for (let i = 0; i < batches.length; i++) {
                let batch = batches[i], background = group.artLayers.add();
                background.name = "QC backgrounds " + (i + 1);
                // Text layers were added in issue order (newest at the top).
                // Placing each background below its first text preserves the
                // original interleaving when later boxes overlap earlier text.
                background.move(notes[batch[0]].layer, ElementPlacement.PLACEAFTER);
                doc.activeLayer = background;
                qcPaintBatch(doc, shapes, batch, red, white, timing);
            }
        } finally { timing.backgroundMs = qcNow() - started; }
        started = qcNow();
        try {
            let merged = group.merge(); merged.name = "__LabelPlus_QC_complete";
            let stamp = qcNow();
            for (let i = 0; i < doc.layers.length; i++) {
                let old = doc.layers[i];
                if (old !== merged && old.name === "revise") { old.name = "revise_previous_" + stamp; old.visible = false; }
            }
            merged.name = "revise"; merged.visible = true;
            doc.activeLayer = merged;
        } finally { timing.mergeMs = qcNow() - started; }
    } finally {
        try { qcRestoreWorkPath(doc, savedWorkPath, selectedPathName); }
        finally {
            try {
                if (savedSelection) {
                    try { doc.selection.load(savedSelection, SelectionType.REPLACE); } finally { savedSelection.remove(); }
                } else doc.selection.deselect();
            } finally { try { doc.activeChannels = oldChannels; } catch (ignored) { } }
        }
    }
}

function qcPath(file: File): string { return file.fsName.replace(/\\/g, "/").toLowerCase(); }
function qcOpened(file: File): Document | null {
    let path = qcPath(file);
    for (let i = 0; i < app.documents.length; i++)
        try { if (qcPath(app.documents[i].fullName) === path) return app.documents[i]; } catch (untitled) { }
    return null;
}

export function executeQcPlan(plan: QcPlan, folder: Folder,
    progress?: (current: number, total: number, name: string) => boolean): QcBatchResult {
    let result: QcBatchResult = { completed: [], errors: plan.errors.slice(0), cancelled: false, timings: [] };
    let ruler = app.preferences.rulerUnits, dialogs = app.displayDialogs;
    let font = qcFont();
    try {
        app.preferences.rulerUnits = Units.PIXELS; app.displayDialogs = DialogModes.NO;
        for (let i = 0; i < plan.matches.length; i++) {
            let match = plan.matches[i];
            if (progress && !progress(i, plan.matches.length, match.file)) { result.cancelled = true; break; }
            let doc: Document | null = null, history: any = null;
            let started = qcNow();
            let timing: QcPageTiming = { file: match.file, issues: match.page.issues.length, completed: false,
                openMs: 0, arrowMs: 0, textMs: 0, backgroundMs: 0, mergeMs: 0, totalMs: 0,
                arrowBatches: 0, backgroundBatches: 0, pathFills: 0 };
            result.timings.push(timing);
            try {
                let file = new File(folder.fsName + "/" + match.file);
                try {
                    doc = qcOpened(file);
                    if (!doc) doc = app.open(file);
                    app.activeDocument = doc;
                } finally { timing.openMs = qcNow() - started; }
                if (!doc) throw new Error("不能打开 PSD。");
                if (Math.abs(doc.width.as("px") - match.page.width) > .5 || Math.abs(doc.height.as("px") - match.page.height) > .5)
                    throw new Error("PSD 尺寸与监修成品不同，已跳过。");
                if (doc.mode === DocumentMode.BITMAP || doc.mode === DocumentMode.INDEXEDCOLOR || doc.mode === DocumentMode.MULTICHANNEL)
                    throw new Error("该文档颜色模式不支持标注图层，已跳过。");
                history = doc.activeHistoryState;
                qcJob = { doc: doc, page: match.page, font: font, timing: timing };
                doc.suspendHistory("LabelPlus&Revise 返修标注", "LabelPlus.qcRenderActive()");
                result.completed.push(match.file);
                timing.completed = true;
            } catch (error) {
                // Roll back this page only, preserving pre-existing unsaved work.
                if (doc && history) try { doc.activeHistoryState = history; }
                    catch (rollback) { result.errors.push(match.file + "：本页回退失败，请检查当前文档：" + String(rollback)); }
                result.errors.push(match.file + "：" + String(error));
            } finally { timing.totalMs = qcNow() - started; qcJob = null; }
        }
    } finally {
        app.preferences.rulerUnits = ruler; app.displayDialogs = dialogs;
    }
    return result;
}

function qcFilename(file: File): string { try { return decodeURI(file.name); } catch (ignored) { return file.name; } }
function qcFileList(folder: Folder): string[] {
    let entries = folder.getFiles(), names: string[] = [];
    for (let i = 0; i < entries.length; i++)
        if (entries[i] instanceof File && /\.psd$/i.test(entries[i].name)) names.push(qcFilename(<File>entries[i]));
    return names;
}
export function findQcOutputFolder(file: File): Folder | null {
    let candidate = new Folder(file.parent.fsName + "/output");
    return candidate.exists ? candidate : null;
}
export function runQcDialog(): void {
    let dialog = new Window("dialog", "LabelPlus&Revise 0.6.1 · 返修导入");
    dialog.orientation = "column"; dialog.alignChildren = "fill";
    dialog.add("statictext", undefined, "导入箭头、编号和说明，合并为普通 revise 图层。PSD 保持打开，由你手动保存。");
    let fileRow = dialog.add("group"); fileRow.add("statictext", undefined, "返修单：");
    let fileText = fileRow.add("edittext", undefined, ""); fileText.characters = 58; fileText.enabled = false;
    let fileButton = fileRow.add("button", undefined, "选择…");
    let folderRow = dialog.add("group"); folderRow.add("statictext", undefined, "PSD 目录：");
    let folderText = folderRow.add("edittext", undefined, ""); folderText.characters = 56; folderText.enabled = false;
    let folderButton = folderRow.add("button", undefined, "选择…");
    let summary = dialog.add("statictext", undefined, "选择返修单和 PSD 文件夹后，预览文件对应关系。");
    let list = dialog.add("listbox", undefined, [], { multiselect: false }); list.preferredSize = [740, 280];
    dialog.add("statictext", undefined, "仅处理有问题的同名 PSD；旧 revise 会改名并隐藏；原先打开的文档保持打开。");
    let actions = dialog.add("group"); actions.alignment = "right";
    actions.add("button", undefined, "取消", { name: "cancel" });
    let run = actions.add("button", undefined, "生成 revise", { name: "ok" }); run.enabled = false;
    let project: QcProject | null = null, folder: Folder | null = null, plan: QcPlan | null = null;
    function selectFolder(): void {
        let chosen = Folder.selectDialog("选择嵌字完成的 PSD 文件夹"); if (!chosen) return;
        folder = chosen; folderText.text = chosen.fsName; preview();
    }
    function preview(): void {
        list.removeAll(); plan = null; run.enabled = false;
        if (!project || !folder) return;
        try {
            plan = planQcImport(project, qcFileList(folder));
            for (let i = 0; i < plan.matches.length; i++) {
                let m = plan.matches[i]; list.add("item", m.page.file + "  →  " + m.file + "  （" + m.page.issues.length + " 条）");
            }
            for (let i = 0; i < plan.errors.length; i++) list.add("item", "[跳过] " + plan.errors[i]);
            summary.text = "可导入 " + plan.matches.length + " 页；无问题 " + plan.skipped + " 页；异常 " + plan.errors.length + " 项。";
            run.enabled = plan.matches.length > 0;
        } catch (error) { summary.text = String(error); }
    }
    fileButton.onClick = function (): void {
        let file = File.openDialog("选择 QC 返修单", "*.txt"); if (!file) return;
        try {
            file.encoding = "UTF-8"; if (!file.open("r")) throw new Error("不能读取返修单。");
            let contents = file.read(); file.close(); project = parseQcText(contents); fileText.text = file.fsName;
            // Re-evaluate for every new repair sheet, so a previous batch cannot leak its directory.
            folder = findQcOutputFolder(file); folderText.text = folder ? folder.fsName : "";
            preview();
            if (!folder) {
                alert("没有找到返修单同目录下的 output 文件夹，请手动选择 PSD 文件夹。");
                selectFolder();
            }
        } catch (error) { try { file.close(); } catch (ignored) { } alert(String(error)); }
    };
    folderButton.onClick = selectFolder;
    if (dialog.show() !== 1 || !plan || !folder) return;
    let progress = new Window("palette", "正在导入 QC 标注"); progress.orientation = "column"; progress.alignChildren = "fill";
    let label = progress.add("statictext", undefined, "准备导入…"); label.preferredSize = [460, 25];
    let bar = progress.add("progressbar", undefined, 0, 100); bar.preferredSize = [460, 18];
    let cancel = progress.add("button", undefined, "处理完当前页后停止"); let stop = false;
    cancel.onClick = function (): void { stop = true; }; progress.onClose = function (): boolean { stop = true; return true; };
    progress.show();
    try {
        let result = executeQcPlan(plan, folder, function (current: number, total: number, name: string): boolean {
            label.text = (current + 1) + " / " + total + "  " + name; bar.value = current * 100 / total; progress.update(); return !stop;
        });
        progress.close();
        if (result.errors.length) alert("以下文件未能导入，请检查：\r\n" + result.errors.join("\r\n"));
    } catch (error) { progress.close(); alert("QC 导入失败：\r\n" + String(error)); }
}

export function chooseQcMode(): boolean | null {
    let dialog = new Window("dialog", "LabelPlus&Revise"); dialog.orientation = "column"; dialog.alignChildren = "fill";
    dialog.add("statictext", undefined, "选择工作模式");
    let translation = dialog.add("radiobutton", undefined, "译文导入（原有功能）"); translation.value = true;
    let qc = dialog.add("radiobutton", undefined, "检修模式（导入 QC 返修单）");
    let actions = dialog.add("group"); actions.alignment = "right";
    actions.add("button", undefined, "取消", { name: "cancel" }); actions.add("button", undefined, "继续", { name: "ok" });
    if (dialog.show() !== 1) return null;
    return qc.value;
}
}
