param([switch]$Probe, [switch]$Launch)
$ErrorActionPreference = 'Stop'
try { $revisePs = [Runtime.InteropServices.Marshal]::GetActiveObject('Photoshop.Application.200') }
catch {
    if (!$Launch) { throw }
    try { $revisePs = New-Object -ComObject Photoshop.Application.200 }
    catch {
        $reviseExe = Join-Path $env:ProgramFiles 'Adobe/Adobe Photoshop 2026/Photoshop.exe'
        if (!(Test-Path -LiteralPath $reviseExe)) { throw }
        Start-Process -FilePath $reviseExe -ArgumentList '/Automation' -WindowStyle Hidden | Out-Null
        $revisePs = $null
        for ($reviseAttempt = 0; $reviseAttempt -lt 60; $reviseAttempt++) {
            Start-Sleep -Milliseconds 500
            try { $revisePs = [Runtime.InteropServices.Marshal]::GetActiveObject('Photoshop.Application.200'); break } catch { }
        }
        if (!$revisePs) { throw 'Photoshop started, but its automation connection is unavailable.' }
    }
    $revisePs.Visible = $true
}
Write-Output ('Photoshop ' + $revisePs.Version + '; documents=' + $revisePs.Documents.Count)
if ($Probe) { return }
$reviseRun = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'current-run.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$revisePs.DoJavaScriptFile((Join-Path $reviseRun.run 'Photoshop-Smoke.jsx'))
$reviseResultPath = Join-Path $reviseRun.run 'photoshop-result.json'
Get-Content -LiteralPath $reviseResultPath -Raw -Encoding UTF8
$reviseResult = Get-Content -LiteralPath $reviseResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$reviseResult.ok) { throw 'Native Photoshop flow test failed; see photoshop-result.json.' }
