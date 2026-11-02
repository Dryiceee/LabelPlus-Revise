$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force build | Out-Null
    & node node_modules/typescript/bin/tsc --preserveConstEnums -p . --out build/app.js
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript build failed' }
    & node node_modules/typescript/bin/tsc -p tsconfig.qc.json
    if ($LASTEXITCODE -ne 0) { throw 'QC TypeScript build failed' }
    & python flatten_jsx.py build/app.js 'build/LabelPlus&Revise.jsx' -I build src
    if ($LASTEXITCODE -ne 0) { throw 'JSX flatten failed' }
    $qcEncoding = New-Object System.Text.UTF8Encoding($true)
    $qcNotice = "#target photoshop`r`n/*! LabelPlus&Revise 1.0, based on PS-Script v1.7.4 (compound paths). GPL-2.0; see LICENSE.txt and third-party notices. */`r`n"
    $qcCombined = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'build/LabelPlus&Revise.jsx'))
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build/LabelPlus&Revise.jsx'), $qcNotice + $qcCombined, $qcEncoding)
    $qcJson = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/jam/jamJSON.jsxinc'))
    $qcCore = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'build/qc_core.js'))
    # ExtendScript needs an explicit UTF-8 BOM when a long ASCII prefix precedes Chinese text.
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build/qc_core.js'), $qcCore, $qcEncoding)
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build/LabelPlus&Revise_Revise.jsx'), $qcNotice + $qcJson + "`r`n" + $qcCore + "`r`nLabelPlus.runQcDialog();`r`n", $qcEncoding)
    New-Item -ItemType Directory -Force build/ps_script_res | Out-Null
    Copy-Item -Path doc_template/* -Destination build/ps_script_res -Recurse -Force
    Write-Output 'Built build/LabelPlus&Revise.jsx and build/LabelPlus&Revise_Revise.jsx'
} finally { Pop-Location }
