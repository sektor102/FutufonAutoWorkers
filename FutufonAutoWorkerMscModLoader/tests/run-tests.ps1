#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Separate syntax trees keep using directives at the beginning of each source file.
$taskFactorySource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\FactoryDemo.cs') -Raw
function Get-FactoryIterator([string]$start, [string]$end) {
    $taskStart = $taskFactorySource.IndexOf($start, [StringComparison]::Ordinal)
    $taskEnd = $taskFactorySource.IndexOf($end, $taskStart, [StringComparison]::Ordinal)
    if ($taskStart -lt 0 -or $taskEnd -le $taskStart) { throw "Factory iterator not found: $start" }
    $taskFactorySource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskIterators = (Get-FactoryIterator 'internal IEnumerator Run()' 'private void SynchronizeShippingContents()') +
    (Get-FactoryIterator 'private void SynchronizeShippingContents()' 'private int CountConsumedStackPackages()') +
    (Get-FactoryIterator 'private int CountConsumedStackPackages()' 'private void PrepareShippingResources()') +
    (Get-FactoryIterator 'private IEnumerator FinishFullShippingBox()' 'private IEnumerator MakePackage()')
$taskHarness = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'autoworker-test-' + [Guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($taskHarness, "using System; using System.Collections;`nnamespace FutufonAutoWorkerMscModLoader { internal partial class FactoryDemo { $taskIterators } }")
    $taskControls = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\FutufonAutoWorkerMscModLoader.cs') -Raw
    $taskKeysStart = $taskControls.IndexOf('private void UpdateSelectionHotkeys()', [StringComparison]::Ordinal)
    $taskKeysEnd = $taskControls.IndexOf('private void SampleFactory(', $taskKeysStart, [StringComparison]::Ordinal)
    $taskModeStart = $taskControls.IndexOf('private void ModeSettingChanged()', [StringComparison]::Ordinal)
    $taskModeEnd = $taskControls.IndexOf('private AutomationOptions CurrentOptions()', $taskModeStart, [StringComparison]::Ordinal)
    if ($taskKeysStart -lt 0 -or $taskModeStart -lt 0) { throw 'Hotkey handler not found.' }
    $taskSelectionSource = $taskControls.Substring($taskKeysStart, $taskKeysEnd - $taskKeysStart) + $taskControls.Substring($taskModeStart, $taskModeEnd - $taskModeStart)
    [IO.File]::AppendAllText($taskHarness, "`nnamespace FutufonAutoWorkerMscModLoader { internal partial class FutufonAutoWorkerMscModLoader { $taskSelectionSource } }")
    Add-Type -Path (Join-Path $PSScriptRoot '..\DemoStepper.cs'), (Join-Path $PSScriptRoot 'DemoStepperTests.cs'), (Join-Path $PSScriptRoot '..\WorkCheckStatus.cs'), (Join-Path $PSScriptRoot 'WorkCheckStatusTests.cs'), (Join-Path $PSScriptRoot '..\AutomationModel.cs'), (Join-Path $PSScriptRoot '..\WorkerText.cs'), (Join-Path $PSScriptRoot 'AutomationTests.cs'), (Join-Path $PSScriptRoot '..\WorkShiftInfo.cs'), (Join-Path $PSScriptRoot 'WorkShiftInfoTests.cs'), (Join-Path $PSScriptRoot 'FactoryRunTests.cs'), (Join-Path $PSScriptRoot 'SelectionTests.cs'), $taskHarness
} finally { Remove-Item -LiteralPath $taskHarness -ErrorAction SilentlyContinue }
Add-Type -Path (Join-Path $PSScriptRoot '..\SessionLogger.cs'), (Join-Path $PSScriptRoot 'SessionLoggerTests.cs')
[DemoStepperTests]::Run()
[WorkCheckStatusTests]::Run()
[AutomationTests]::Run()
[SelectionTests]::Run()
[ShiftReminderTests]::Run()
[WorkShiftInfoTests]::Run()
[FactoryRunTests]::Run()
[SessionLoggerTests]::Run()
