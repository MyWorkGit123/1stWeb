<#
.SYNOPSIS
    Compiles and play-mode-tests the Brinehold Unity client headlessly, then prints a short report.

.DESCRIPTION
    Unity cannot run in this project's CI, so this is the check that closes the loop on a machine
    that has an editor. It runs Unity in batch mode, so it needs no interaction and produces a
    pass/fail rather than a description of what somebody thought they saw on screen.

    The output files are what to send back if something fails:
      unity-tests.xml   per-test results
      unity.log         the full editor log, including compile errors

.EXAMPLE
    tools\dev\unity-check.ps1
    tools\dev\unity-check.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.30f1\Editor\Unity.exe"
#>
param(
    [string]$UnityPath = "",
    [switch]$CompileOnly
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repo "unity\BrineholdClient"

# Find the newest installed editor if one was not named.
if (-not $UnityPath) {
    $hub = "C:\Program Files\Unity\Hub\Editor"
    if (Test-Path $hub) {
        $newest = Get-ChildItem $hub -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($newest) { $UnityPath = Join-Path $newest.FullName "Editor\Unity.exe" }
    }
}

if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Error "Could not find Unity. Pass -UnityPath ""<path to Unity.exe>""."
    exit 2
}

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $project"

# Build the shared packages first: a failure here is far easier to read from dotnet than from
# Unity's console, and it fails in seconds rather than minutes.
Write-Host "`nBuilding the .NET side first..."
& dotnet build (Join-Path $repo "Brinehold.sln") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error "The .NET build failed; fix that before opening Unity."; exit 1 }

$log = Join-Path $repo "unity.log"
$results = Join-Path $repo "unity-tests.xml"
Remove-Item $log, $results -ErrorAction SilentlyContinue

if ($CompileOnly) {
    Write-Host "`nCompiling the Unity project (batch mode)..."
    & $UnityPath -batchmode -quit -projectPath $project -logFile $log
} else {
    Write-Host "`nRunning play-mode tests (batch mode). This takes a few minutes on a first import..."
    & $UnityPath -batchmode -projectPath $project -runTests -testPlatform PlayMode `
                 -testResults $results -logFile $log
}
$unityExit = $LASTEXITCODE

Write-Host "`n──────── compile errors ────────"
if (Test-Path $log) {
    $errors = Select-String -Path $log -Pattern "error CS\d+" | Select-Object -First 40
    if ($errors) { $errors | ForEach-Object { $_.Line.Trim() } }
    else { Write-Host "  none" }
} else {
    Write-Host "  no log was produced — Unity may not have started"
}

if (Test-Path $results) {
    [xml]$xml = Get-Content $results
    $run = $xml."test-run"
    Write-Host "`n──────── play-mode tests ────────"
    Write-Host ("  total {0}  passed {1}  failed {2}  skipped {3}" -f $run.total, $run.passed, $run.failed, $run.skipped)

    if ([int]$run.failed -gt 0) {
        Write-Host "`n  failures:"
        $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
            Write-Host ("   - {0}" -f $_.fullname)
            $message = $_.failure.message.'#cdata-section'
            if ($message) { Write-Host ("     {0}" -f $message.Trim()) }
        }
    }
}

Write-Host "`nFull log:     $log"
Write-Host "Test results: $results"
Write-Host "`nIf anything failed, send those two files back."
exit $unityExit
