const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const context = vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(root, 'src/jam/jamJSON.jsxinc'), 'utf8'), context);
vm.runInContext(fs.readFileSync(path.join(root, 'build/qc_core.js'), 'utf8'), context);
const api = context.LabelPlus;
const text = fs.readFileSync(process.argv[2], 'utf8');
const project = api.parseQcText(text);
let count = 0;
function check(value, description) { assert.ok(value, description); count++; }
check(project.pages.length === 2 && project.pages[0].issues.length === 2, 'C# output is readable by JSX parser');
check(project.pages[0].issues[0].text.includes('\n') && project.pages[0].issues[0].text.includes('“'), 'CJK, newlines and quotes');
// Arrays must belong to the same VM realm as the ExtendScript implementation.
function files(names) { context.testNames = names.join('|'); return vm.runInContext('testNames.split("|")', context); }
let plan = api.planQcImport(project, files(['001.PSD', '002.psd', 'unrelated.psd']));
check(plan.matches.length === 1 && plan.matches[0].file === '001.PSD', 'case-insensitive matching');
check(plan.skipped === 1 && plan.errors.length === 0, 'empty page is skipped before opening');
plan = api.planQcImport(project, files(['002.psd']));
check(plan.matches.length === 0 && plan.errors.length === 1, 'missing PSD reported');
plan = api.planQcImport(project, files(['001.psd', '001.PSD']));
check(plan.matches.length === 0 && plan.errors.length === 1, 'ambiguous matches rejected');
for (const mutate of [
  p => { p.version = 2; },
  p => { p.pages[0].file = '../001.png'; },
  p => { p.pages[0].issues[0].x = -1; },
  p => { p.pages[0].issues[0].tailY = '0.3'; },
  p => { p.pages[0].issues[0].noteX = null; },
  p => { p.pages[0].issues[1].id = p.pages[0].issues[0].id; },
  p => { p.pages[1].file = '001.jpg'; }
]) {
  const p = JSON.parse(text.replace(/^\uFEFF/, '').split('\n').slice(1).join('\n'));
  mutate(p); assert.throws(() => api.parseQcText('LabelPlus-QC 1\n' + JSON.stringify(p))); count++;
}
assert.throws(() => api.parseQcText('1,0\ntranslation')); count++;
const draft = api.parseQcText(text); draft.pages[0].issues[0].text = '  \n';
check(api.planQcImport(draft, files(['001.psd'])).errors.length === 1, 'blank descriptions require completion');
check(api.wrapQcText('中文English\n下一行', 6).includes('\r'), 'explicit wrapping');
const emoji = api.wrapQcText('甲😀乙😀丙', 2);
check(!/[\uD800-\uDBFF]\r[\uDC00-\uDFFF]/.test(emoji), 'surrogate pairs are kept intact');
console.log(`PASS: ${count} JSX format/planning checks`);
