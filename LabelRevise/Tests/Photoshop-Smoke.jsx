#target photoshop
(function () {
    var base = File($.fileName).parent, log = new File(base.fsName + '/photoshop-result.json');
    $.evalFile(new File(base.fsName + '/test-core.jsx'));
    var checks = 0, result = null, previous = [], oldDialogs = app.displayDialogs, oldRuler = app.preferences.rulerUnits;
    function check(value, message) { checks++; if (!value) throw new Error(message); }
    for (var i = 0; i < app.documents.length; i++)
        previous.push({ doc: app.documents[i], id: app.documents[i].id, layers: app.documents[i].layers.length, saved: app.documents[i].saved });
    try {
        app.displayDialogs = DialogModes.NO; app.preferences.rulerUnits = Units.PIXELS;
        var file = new File(base.fsName + '/revise.txt'); file.encoding = 'UTF-8'; file.open('r');
        var project = LabelPlus.parseQcText(file.read()); file.close();
        var output = new Folder(base.fsName + '/output/run-' + (new Date()).getTime()); output.create();
        function fixture(name) {
            var doc = app.open(new File(base.fsName + '/finished/' + name + '.png'));
            doc.resizeImage(undefined, undefined, 300, ResampleMethod.NONE);
            doc.saveAs(new File(output.fsName + '/' + name + '.psd'), new PhotoshopSaveOptions(), false, Extension.LOWERCASE);
            return doc;
        }
        var empty = fixture('002');
        // This document was created by this harness, not open before the test.
        empty.close(SaveOptions.DONOTSAVECHANGES);
        var first = fixture('001');
        var keep = first.artLayers.add(); keep.kind = LayerKind.TEXT; keep.name = 'SMOKE_KEEP_UNSAVED';
        keep.textItem.contents = 'Existing unsaved test layer'; keep.textItem.position = [UnitValue(60, 'px'), UnitValue(1550, 'px')];
        first.selection.select([[30, 30], [90, 30], [90, 90], [30, 90]]);
        first.selection.makeWorkPath(.5);
        var keepPath = first.pathItems[0];
        var pathName = keepPath.name, pathAnchor = keepPath.subPathItems[0].pathPoints[0].anchor.toString();
        first.selection.select([[70, 70], [140, 70], [140, 120], [70, 120]]);
        var selection = first.selection.bounds, channels = first.channels.length, layerCount = first.layers.length;
        var plan = LabelPlus.planQcImport(project, ['001.psd', '002.psd']);
        check(plan.matches.length === 1 && plan.skipped === 1, 'only marked page planned');
        var imported = LabelPlus.executeQcPlan(plan, output);
        check(imported.completed.length === 1 && !imported.errors.length, 'import succeeds: ' + imported.errors.join('; '));
        check(first.layers.length === layerCount + 1 && first.layers[0].name === 'revise' && first.layers[0].kind === LayerKind.NORMAL,
            'one ordinary top-level revise layer');
        check(first.artLayers.getByName('SMOKE_KEEP_UNSAVED').textItem.contents === 'Existing unsaved test layer', 'unsaved text preserved');
        check(!first.saved && first.resolution === 300, 'PSD stays unsaved at 300 DPI');
        check(first.channels.length === channels && first.selection.bounds[0].as('px') === selection[0].as('px') &&
            first.selection.bounds[3].as('px') === selection[3].as('px'), 'selection and channel count preserved');
        var restoredPath = first.pathItems.length === 1 ? first.pathItems[0] : null;
        check(restoredPath && restoredPath.name === pathName && restoredPath.kind === PathKind.WORKPATH &&
            restoredPath.subPathItems[0].pathPoints[0].anchor.toString() === pathAnchor, 'existing Work Path preserved and temporary paths removed');
        check(imported.timings[0].arrowBatches >= 2 && imported.timings[0].pathFills ===
            2 * (imported.timings[0].arrowBatches + imported.timings[0].backgroundBatches), 'crossing arrows split into ordered compound-path batches');
        check(app.documents.length === previous.length + 1, 'no unmarked PSD opened by importer');
        var visibility = [];
        for (var i = 0; i < first.layers.length; i++) visibility.push(first.layers[i].visible);
        try {
            for (var i = 0; i < first.layers.length; i++) first.layers[i].visible = first.layers[i].name === 'revise';
            first.saveAs(new File(base.fsName + '/revise-layer.png'), new PNGSaveOptions(), true, Extension.LOWERCASE);
            for (var i = 0; i < first.layers.length; i++) first.layers[i].visible = visibility[i] && first.layers[i].name !== 'SMOKE_KEEP_UNSAVED';
            first.saveAs(new File(base.fsName + '/photoshop-preview.png'), new PNGSaveOptions(), true, Extension.LOWERCASE);
        } finally {
            for (var i = 0; i < first.layers.length; i++) first.layers[i].visible = visibility[i];
        }
        var repeated = LabelPlus.executeQcPlan(plan, output), current = 0, hidden = 0;
        check(repeated.completed.length === 1 && !repeated.errors.length, 'repeat import succeeds');
        for (var i = 0; i < first.layers.length; i++) {
            if (first.layers[i].name === 'revise') current++;
            if (first.layers[i].name.indexOf('revise_previous_') === 0 && !first.layers[i].visible) hidden++;
        }
        check(current === 1 && hidden === 1 && !first.saved, 'old revise hidden, new revise unique, PSD not saved');
        for (var i = 0; i < previous.length; i++)
            check(previous[i].doc.id === previous[i].id && previous[i].doc.layers.length === previous[i].layers &&
                previous[i].doc.saved === previous[i].saved, 'pre-existing user document preserved');
        result = { ok: true, checks: checks, photoshopVersion: app.version, userDocumentsPreserved: previous.length,
            timings: imported.timings, fixtureFolder: output.fsName };
    } catch (error) {
        result = { ok: false, checks: checks, error: String(error), line: error.line, file: error.fileName,
            importResult: typeof imported === 'undefined' ? null : imported };
    } finally {
        app.displayDialogs = oldDialogs; app.preferences.rulerUnits = oldRuler;
        log.encoding = 'UTF-8'; log.open('w'); log.write(jamJSON.stringify(result)); log.close();
    }
})();
