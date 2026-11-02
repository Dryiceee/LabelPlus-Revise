from pathlib import Path
import hashlib
import json
import shutil
from PIL import Image

project = Path(__file__).resolve().parents[1]
workspace = project.parent.parent
run = Path(json.loads((project / 'Tests/current-run.json').read_text(encoding='utf-8'))['run'])
version = '0.6.1'
out = workspace / 'outputs'
package = out / f'LabelPlus-Revise-{version}'
native = json.loads((run / 'photoshop-result.json').read_text(encoding='utf-8-sig'))
desktop = (run / 'desktop-result.txt').read_text()
assert native['ok'] and native['checks'] == 11
assert desktop.startswith('PASS: 16 desktop flow checks; DPI=192')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(run / 'LabelRevise.exe') == sha(package / 'LabelRevise.exe')
bundle = (package / 'PS-Script/LabelRevise.jsx').read_text(encoding='utf-8-sig')
expected = bundle.replace('LabelPlus.runQcDialog();', '// Automatic picker suppressed; test invokes the shipped importer.')
assert expected == (run / 'test-core.jsx').read_text(encoding='utf-8-sig'), 'test must use the shipped bundle without diagnostic modifications'
image = Image.open(run / 'revise-layer.png').convert('RGBA')
assert image.size == (1200, 1600)
assert image.getchannel('A').getextrema() == (0, 255) and image.getpixel((0, 0))[3] == 0
sheet = json.loads((run / 'revise.txt').read_text(encoding='utf-8-sig').split('\n', 1)[1])
for issue in sheet['pages'][0]['issues']:
    x, y = round(issue['x'] * 1200), round(issue['y'] * 1600)
    region = image.crop((x - 12, y - 12, x + 13, y + 13))
    assert sum(1 for r, g, b, a in region.getdata() if r > 150 and g < 120 and b < 130 and a > 0) > 5, 'arrow target misaligned'

validation = {
    'version': version, 'date': '2026-11-02', 'desktopChecks': 16, 'desktopDpi': 192,
    'photoshopChecks': native['checks'], 'photoshopVersion': native['photoshopVersion'],
    'fixtureDpi': 300, 'arrowTargetPixelChecks': 3, 'transparentPixelLayerVerified': True,
    'firstImportMs': native['timings'][0]['totalMs'],
    'scope': 'Real standalone WinForms window with event-driven input; shipped JSX core, automatic picker call suppressed',
    'files': {'LabelRevise.exe': sha(package / 'LabelRevise.exe'),
              'PS-Script/LabelRevise.jsx': sha(package / 'PS-Script/LabelRevise.jsx')}
}
(project / 'VALIDATION.json').write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding='utf-8')
report = f'''# LabelPlus Revise {version} 流程测试报告

日期：2026-11-02。结果：修复后通过。

## 发现并修复

1. PS 路径坐标缺少像素到点的换算，300 DPI 文档中的部分图形落到画布外，导致填充失败。0.6.1 已加入 `72 / doc.resolution` 换算。
2. Photoshop 的 PathItems.add 会内部创建工作路径，可能覆盖原有工作路径。0.6.1 在绘制前备份原始路径组件，结束时恢复，恢复失败则回退本页。

## 通过的检查

- 桌面 16 项：独立窗口直接进入返修界面、程序集没有旧工作区、命令行打开返修单、200% DPI 布局、images 自动匹配、建立三个箭头及中文说明、点击问题进入编辑框、原图切换保持视图、空格抓手不新增问题、保存并重新打开。
- Photoshop 27.11.0 中 11 项：只计划有问题页、正常导入、一个顶层普通 revise 图层、保留原有未保存文字、保持 300 DPI 且不自动保存、选区及通道恢复、工作路径保留且临时路径清理、交叉箭头按顺序拆批、无问题 PSD 不主动打开、重复导入成功、旧 revise 隐藏且新 revise 唯一。
- 导出标注图为 1200×1600，包含透明和不透明像素，左上角透明；三个箭头目标位置附近均检测到红色标注像素。另已查看实际导出图片。

## 本次耗时

本次一页三条问题，首次导入约 {native['timings'][0]['totalMs'] / 1000:.2f} 秒。
箭头 {native['timings'][0]['arrowMs'] / 1000:.2f} 秒，文字 {native['timings'][0]['textMs'] / 1000:.2f} 秒，背景 {native['timings'][0]['backgroundMs'] / 1000:.2f} 秒，合并 {native['timings'][0]['mergeMs'] / 1000:.2f} 秒。
箭头分为 {native['timings'][0]['arrowBatches']} 批，背景 {native['timings'][0]['backgroundBatches']} 批，共 {native['timings'][0]['pathFills']} 次路径填充。这是本次合成样例的测量，没有与旧版作等条件性能比较。

## 范围

测试使用单独的合成图片和 PSD，没有使用真实漫画工程。桌面运行实际打包程序集，通过现有事件处理模拟鼠标和按键。PS 运行打包脚本，仅抑制末尾自动弹出文件选择窗口的一行，再调用原导入函数。文件选择窗口的逐项点击未覆盖。

结果对应文件的 SHA-256 记录在源码中的 VALIDATION.json。修复后的运行文件与最初发布的 0.6 不同，请使用 0.6.1。
'''
(project / 'VALIDATION.md').write_text(report, encoding='utf-8')
(out / f'LabelPlus-Revise-{version}-测试报告.md').write_text(report, encoding='utf-8')
shutil.copy2(run / 'desktop.png', out / f'LabelPlus-Revise-{version}-桌面测试.png')
shutil.copy2(run / 'photoshop-preview.png', out / f'LabelPlus-Revise-{version}-PS测试.png')
print(json.dumps(validation, ensure_ascii=False))
