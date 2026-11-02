param(
    [string]$MSBuildPath = '',
    [string]$CscToolPath = '',
    [string]$FrameworkRootPath = '',
    [string]$TypeScriptPath = '',
    [string]$PhotoshopTypesPath = ''
)
$ErrorActionPreference = 'Stop'
if (!$MSBuildPath) {
    if ($CscToolPath) { $MSBuildPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/MSBuild.exe' }
    else {
        $reviseMsbuild = Get-Command MSBuild.exe -ErrorAction Stop
        $MSBuildPath = $reviseMsbuild.Source
    }
}
$revisePs = Join-Path $PSScriptRoot 'Photoshop'
if (!$TypeScriptPath) { $TypeScriptPath = Join-Path $revisePs 'node_modules/typescript/bin/tsc' }
if (!$PhotoshopTypesPath) { $PhotoshopTypesPath = Join-Path $revisePs 'node_modules/photoshop.d.ts/dist/cc' }
if (!(Test-Path -LiteralPath $TypeScriptPath)) { throw 'Run npm ci in Photoshop, or provide TypeScriptPath.' }
if (!(Test-Path -LiteralPath $PhotoshopTypesPath)) { throw 'Run npm ci in Photoshop, or provide PhotoshopTypesPath.' }

Push-Location $PSScriptRoot
try {
    $reviseArguments = @('Desktop/LabelRevise.csproj', '/t:Build', '/p:Configuration=Release', '/v:minimal', '/nologo')
    if ($CscToolPath) { $reviseArguments += ('/p:CscToolPath=' + $CscToolPath) }
    if ($FrameworkRootPath) { $reviseArguments += ('/p:TargetFrameworkRootPath=' + $FrameworkRootPath) }
    & $MSBuildPath @reviseArguments
    if ($LASTEXITCODE -ne 0) { throw 'Revise desktop build failed.' }

    $reviseBuild = Join-Path $PSScriptRoot 'build'
    New-Item -ItemType Directory -Force -Path $reviseBuild | Out-Null
    $reviseConfig = Get-Content -LiteralPath (Join-Path $revisePs 'tsconfig.json') -Raw | ConvertFrom-Json
    $reviseConfig.compilerOptions.types = @([IO.Path]::GetFullPath($PhotoshopTypesPath))
    $reviseConfig.compilerOptions.outFile = Join-Path $reviseBuild 'revise.js'
    $reviseConfig.files = @(Join-Path $revisePs 'src/main.ts')
    $reviseConfigPath = Join-Path $reviseBuild 'tsconfig.build.json'
    $reviseConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reviseConfigPath -Encoding UTF8
    & node $TypeScriptPath -p $reviseConfigPath
    if ($LASTEXITCODE -ne 0) { throw 'Revise Photoshop build failed.' }
    $reviseUtf8 = New-Object System.Text.UTF8Encoding($true)
    $reviseNotice = "#target photoshop`r`n/*! LabelRevise 0.6.1. Derived from LabelPlus QC 0.5.1; original copyright and license notices retained. */`r`n"
    $reviseJson = [IO.File]::ReadAllText((Join-Path $revisePs 'src/jam/jamJSON.jsxinc'))
    $reviseCode = [IO.File]::ReadAllText((Join-Path $reviseBuild 'revise.js'))
    [IO.File]::WriteAllText((Join-Path $reviseBuild 'LabelRevise.jsx'), $reviseNotice + $reviseJson + "`r`n" + $reviseCode, $reviseUtf8)
    Write-Output 'Built Desktop/bin/Release/LabelRevise.exe and build/LabelRevise.jsx'
} finally { Pop-Location }
