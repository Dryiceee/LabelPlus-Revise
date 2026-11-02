const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const context = vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(root, 'build/qc_core.js'), 'utf8'), context);
const cases = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
let checks = 0;
for (const item of cases) {
    const rect = context.LabelPlus.placeQcNote(1000, 1000, item.issue, item.width, item.height, 12);
    assert.ok(Math.abs(rect.x - item.expected.x) < .02 && Math.abs(rect.y - item.expected.y) < .02,
        `Photoshop/desktop placement mismatch: ${JSON.stringify(item)}`); checks++;
    assert.ok(rect.x >= 0 && rect.y >= 0 && rect.x + rect.width <= 1000 && rect.y + rect.height <= 1000); checks++;
    if (item.issue.noteX === item.issue.tailX && item.issue.noteY === item.issue.tailY) {
        for (let step = 0; step <= 1000; step++) {
            const t = step / 1000;
            const x = (item.issue.tailX + (item.issue.x - item.issue.tailX) * t) * 1000;
            const y = (item.issue.tailY + (item.issue.y - item.issue.tailY) * t) * 1000;
            assert.ok(!(x >= rect.x && x < rect.x + rect.width && y >= rect.y && y < rect.y + rect.height), 'note covers arrow');
        }
        checks++;
    }
}
console.log(`PASS: ${checks} Photoshop layout checks; ${cases.length} desktop/Photoshop matches`);
