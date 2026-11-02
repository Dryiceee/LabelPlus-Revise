const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const repairText = fs.readFileSync(process.argv[2], 'utf8');
let checks = 0;
function runScenario(autoExists, manualPath, secondFile) {
  const controls = [], alerts = [], selectedDirs = [];
  let selected = 'C:/测试项目/revise.txt';
  class Folder {
    constructor(name) { this.fsName = name; this.exists = autoExists && name === 'C:/测试项目/output'; }
    getFiles() { return [new File(this.fsName + '/001.psd'), new File(this.fsName + '/002.psd')]; }
    static selectDialog() { selectedDirs.push(true); return manualPath ? new Folder(manualPath) : null; }
  }
  class File {
    constructor(name) {
      this.fsName = name; this.name = name.slice(name.lastIndexOf('/') + 1);
      this.parent = new Folder(name.slice(0, name.lastIndexOf('/'))); this.exists = true;
    }
    static openDialog() { return new File(selected); }
    open() { return true; }
    read() { return repairText; }
    close() { }
  }
  function control(type, bounds, text) {
    const value = { type, text: text || '', children: [], enabled: true, add(t, b, s) {
      const child = control(t, b, s); this.children.push(child); return child;
    }, removeAll() { this.children = []; } };
    controls.push(value); return value;
  }
  let result;
  function Window() {
    const dialog = control('dialog');
    dialog.show = () => {
      const choose = controls.filter(c => c.type === 'button' && c.text === '选择…')[0];
      choose.onClick();
      if (secondFile) { selected = 'C:/另一个项目/revise.txt'; choose.onClick(); }
      const edits = controls.filter(c => c.type === 'edittext');
      result = { folder: edits[1].text, run: controls.find(c => c.text === '生成 revise').enabled,
        alerts, manuallyPrompted: selectedDirs.length };
      return 0; // Cancel the outer dialog; do not invoke any Photoshop document API.
    };
    return dialog;
  }
  const ctx = vm.createContext({ File, Folder, Window, alert: text => alerts.push(text) });
  vm.runInContext(fs.readFileSync(path.join(root, 'src/jam/jamJSON.jsxinc'), 'utf8'), ctx);
  vm.runInContext(fs.readFileSync(path.join(root, 'build/qc_core.js'), 'utf8'), ctx);
  ctx.LabelPlus.runQcDialog();
  return result;
}
let r = runScenario(true, null, false);
assert.equal(r.folder, 'C:/测试项目/output'); checks++;
assert.ok(r.run && r.alerts.length === 0 && r.manuallyPrompted === 0); checks++;
r = runScenario(false, 'D:/手选PSD', false);
assert.equal(r.folder, 'D:/手选PSD'); checks++;
assert.ok(r.run && r.alerts.length === 1 && r.manuallyPrompted === 1); checks++;
r = runScenario(false, null, false);
assert.ok(!r.run && r.folder === '' && r.alerts.length === 1); checks++;
r = runScenario(true, null, true);
assert.ok(!r.run && r.folder === '' && r.alerts.length === 1, 'selecting a second sheet clears the old auto directory'); checks++;
console.log(`PASS: ${checks} QC dialog auto-directory checks`);
