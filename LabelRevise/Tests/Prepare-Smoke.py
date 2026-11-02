from pathlib import Path
from datetime import datetime
import json
import shutil

project = Path(__file__).resolve().parents[1]
workspace = project.parent.parent
package = workspace / 'outputs/LabelRevise-1.0'
run = workspace / 'work/qa' / ('revise-smoke-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
run.mkdir(parents=True)
for name in ['LabelRevise.exe', 'LabelRevise.exe.config']:
    shutil.copy2(package / name, run / name)
shutil.copy2(package / 'LabelRevise.exe.config', run / 'ReviseSmoke.exe.config')
for old, new in [('成品', 'finished'), ('images', 'images')]:
    shutil.copytree(package / 'Sample' / old, run / new)
(run / 'output').mkdir()
code = (package / 'PS-Script/LabelRevise.jsx').read_text(encoding='utf-8-sig')
entry = 'LabelPlus.runQcDialog();'
assert code.count(entry) == 1
# Load exactly the shipped bundle, suppressing only its automatic file-picker UI.
code = code.replace(entry, '// Test harness invokes the shipped importer below.')
(run / 'test-core.jsx').write_text(code, encoding='utf-8-sig')
shutil.copy2(project / 'Tests/Photoshop-Smoke.jsx', run / 'Photoshop-Smoke.jsx')
metadata = {'run': str(run), 'repairSheet': str(run / 'revise.txt')}
(project / 'Tests/current-run.json').write_text(json.dumps(metadata, ensure_ascii=False), encoding='utf-8')
print(json.dumps(metadata, ensure_ascii=False))
