param([string]$MSBuildPath = '', [string]$CscToolPath = '', [string]$FrameworkRootPath = '')
$ErrorActionPreference = 'Stop'
if (!$MSBuildPath) {
    $qcFound = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($qcFound) { $MSBuildPath = $qcFound.Source }
    elseif ($CscToolPath) { $MSBuildPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/MSBuild.exe' }
    else { throw 'Run in a Visual Studio developer PowerShell, or specify MSBuildPath and CscToolPath.' }
}
Push-Location $PSScriptRoot
try {
    $qcArguments = @('LabelPlus&Revise.sln', '/t:Build', '/p:Configuration=Release', '/p:PreBuildEvent=', '/p:PostBuildEvent=', '/p:GenerateManifests=false', '/v:minimal', '/nologo')
    if ($CscToolPath) { $qcArguments += ('/p:CscToolPath=' + $CscToolPath) }
    if ($FrameworkRootPath) { $qcArguments += ('/p:TargetFrameworkRootPath=' + $FrameworkRootPath) }
    & $MSBuildPath @qcArguments
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
    Copy-Item -Path Resource/* -Destination LabelPlus/bin/Release -Recurse -Force
    Write-Output 'Built LabelPlus/bin/Release/LabelPlus&Revise.exe'
} finally { Pop-Location }
