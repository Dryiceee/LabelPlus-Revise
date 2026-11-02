# LabelRevise 0.6.1 构建说明

此目录是独立的删减版源码，构建时不引用旧 LabelPlus 的翻译工作区或旧 PS 译文导入项目。

## 代码来源

- `Desktop/QcModel.cs`、`QcCanvas.cs`、`QcWorkspace.cs` 直接复制自桌面 QC 0.4，保留现有功能实现。
- `Desktop/MainFrm.cs` 从原有 `MainFrm.Qc.cs` 提取独立窗口及快捷键处理，去掉旧模式和旧工作区链接，并保留退出时的未保存确认。
- `Desktop/Program.cs` 保留 WinForms 启动、高 DPI 和区域设置，去掉旧语言包与翻译配置加载。
- 桌面项目仅编译上述 5 个源文件及程序集信息，没有隐藏的翻译模块。
- PS 的 `qc.ts`、`qc_paths.ts`、`qc_format.ts` 来自 QC 0.5.1；删除 `chooseQcMode`，新 `main.ts` 只调用返修导入。
- PS 保留必需的 jamJSON 及原始版权/许可声明，没有译文解析器、模板或 xtools。
- `LabelPlus-QC 1` 文件头与 JSON 结构保持一致，兼容已有 `revise.txt`。
- 0.6.1 在构造 PathPointInfo 时显式乘以 `72 / doc.resolution`，将像素几何转换成点坐标，避免非 72 DPI 文档发生偏移。
- PathItems.add 会先创建工作路径，可能覆盖用户已有工作路径。0.6.1 通过 Action Manager 备份原始 pathComponents，在 finally 恢复工作路径和选区；恢复失败将触发整页回退。

## 常规构建

需要 .NET Framework 4.8 targeting pack、支持 C# 7 的 MSBuild/C# 编译器、Node.js 与 npm。

在 `Photoshop` 目录运行 `npm ci`，安装锁定的 TypeScript 及 Photoshop 类型声明。在 Visual Studio 开发者 PowerShell 中回到源码根目录，运行：

```powershell
./Build-Revise.ps1
```

输出：

- `Desktop/bin/Release/LabelRevise.exe`
- `Desktop/bin/Release/LabelRevise.exe.config`
- `build/LabelRevise.jsx`

运行软件时同时携带 EXE 与 config。脚本已经合并必需的 JSON 库，并写入 UTF-8 BOM，可独立运行。

## 本工作区使用的便携工具

在原项目根目录运行：

```powershell
& work/LabelPlus-Revise/Build-Revise.ps1 `
  -CscToolPath "$PWD\work\roslyn\tasks\net472" `
  -FrameworkRootPath "$PWD\work\net48-refs\build\" `
  -TypeScriptPath "$PWD\work\PS-Script-1.7.4\node_modules\typescript\bin\tsc" `
  -PhotoshopTypesPath "$PWD\work\PS-Script-1.7.4\node_modules\photoshop.d.ts\dist\cc"
```

上面旧目录只提供编译工具与类型声明，不将旧软件或译文导入代码带入新版。亦可通过参数指定其他本机工具位置。受限环境下 MSBuild 可能需要正常本机权限才能使用构建缓存。

## 验证范围

0.6 最初仅完成编译与打包。0.6.1 已运行 `Tests/ReviseSmoke.cs` 与 `Tests/Photoshop-Smoke.jsx`：桌面 16 项、原生 Photoshop 27.11.0 导入 11 项检查通过，另检查输出透明度与三个箭头目标位置。详见 `VALIDATION.md` 和包含运行文件哈希的 `VALIDATION.json`。此前 0.3 的测试结果不视为本版本验证结果。

原工作区可运行 `Tests/Prepare-Smoke.py` 建立独立合成样例，编译并运行 ReviseSmoke（参数为生成的 revise.txt 完整路径），再用 Windows PowerShell 5 运行 `Tests/Run-Photoshop-Smoke.ps1`。COM 启动器匹配本机 Photoshop 2026，其他安装版本需调整连接。测试不会操作真实漫画文件。
