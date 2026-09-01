<#
.SYNOPSIS
    Compiles and play-mode-tests the Brinehold Unity client headlessly, then prints a short report.

.DESCRIPTION
    Unity cannot run in this project's CI, so this is the check that closes the loop on a machine
    that has an editor. It runs Unity in batch mode, so it needs no interaction and produces a
    pass/fail rather than a description of what somebody thought they saw on screen.

    Close the Unity editor before running this: Unity cannot open the same project twice.

    The output files are what to send back if something fails:
      unity-tests.xml   per-test results
      unity.log         the full editor log, including compile errors

    This file is deliberately pure ASCII and uses no backtick line continuations. Windows PowerShell
    5.1 reads a .ps1 without a byte-order mark as Windows-1252, so a UTF-8 em dash decodes into a
    stray smart quote and breaks string parsing several lines later. Keep it ASCII.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\dev\unity-check.ps1
    powershell -ExecutionPolicy Bypass -File .\tools\dev\unity-check.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.30f1\Editor\Unity.exe"
#>
param(
    [string]$UnityPath = "",
    [switch]$CompileOnly
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repo "unity\BrineholdClient"

# Find the newest installed editor if one was not named.
if (-not $UnityPath) {
    foreach ($hub in @("C:\Program Files\Unity\Hub\Editor", "$env:LOCALAPPDATA\Unity\Hub\Editor")) {
        if (-not (Test-Path $hub)) { continue }
        $newest = Get-ChildItem $hub -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($newest) {
            $candidate = Join-Path $newest.FullName "Editor\Unity.exe"
            if (Test-Path $candidate) { $UnityPath = $candidate; break }
        }
    }
}

if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
    Write-Host "Could not find Unity."
    Write-Host "Pass the editor path, for example:"
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\tools\dev\unity-check.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.30f1\Editor\Unity.exe"'
    exit 2
}

Write-Host "Unity:   $UnityPath"
Write-Host "Project: $project"
Write-Host ""

# Build the shared packages first: a failure here is far easier to read from dotnet than from
# Unity's console, and it fails in seconds rather than minutes.
Write-Host "Building the .NET side first..."
& dotnet build (Join-Path $repo "Brinehold.sln") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "The .NET build failed. Fix that before opening Unity."
    exit 1
}

$log = Join-Path $repo "unity.log"
$results = Join-Path $repo "unity-tests.xml"
Remove-Item $log -ErrorAction SilentlyContinue
Remove-Item $results -ErrorAction SilentlyContinue

Write-Host ""
if ($CompileOnly) {
    Write-Host "Compiling the Unity project (batch mode)..."
    & $UnityPath -batchmode -quit -projectPath $project -logFile $log
} else {
    Write-Host "Running play-mode tests (batch mode). The first import takes a few minutes..."
    & $UnityPath -batchmode -projectPath $project -runTests -testPlatform PlayMode -testResults $results -logFile $log
}
$unityExit = $LASTEXITCODE

Write-Host ""
Write-Host "-------- compile errors --------"
if (Test-Path $log) {
    $compileErrors = Select-String -Path $log -Pattern "error CS[0-9]+" | Select-Object -First 40
    if ($compileErrors) {
        foreach ($line in $compileErrors) { Write-Host ("  " + $line.Line.Trim()) }
    } else {
        Write-Host "  none"
    }
} else {
    Write-Host "  no log was produced, so Unity may not have started"
}

if (Test-Path $results) {
    [xml]$xml = Get-Content $results
    $run = $xml."test-run"
    Write-Host ""
    Write-Host "-------- play-mode tests --------"
    Write-Host ("  total {0}  passed {1}  failed {2}  skipped {3}" -f $run.total, $run.passed, $run.failed, $run.skipped)

    if ([int]$run.failed -gt 0) {
        Write-Host ""
        Write-Host "  failures:"
        foreach ($case in $xml.SelectNodes("//test-case[@result='Failed']")) {
            Write-Host ("   - " + $case.fullname)
            if ($case.failure -and $case.failure.message) {
                $message = [string]$case.failure.message.InnerText
                if (-not $message) { $message = [string]$case.failure.message }
                if ($message) { Write-Host ("     " + $message.Trim()) }
            }
        }
    }
}

Write-Host ""
Write-Host "Full log:     $log"
Write-Host "Test results: $results"
Write-Host ""
Write-Host "If anything failed, send those two files back."
exit $unityExit
