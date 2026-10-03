#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
# Separate syntax trees keep using directives at the beginning of each source file.
Add-Type -Path (Join-Path $PSScriptRoot '..\DemoStepper.cs'), (Join-Path $PSScriptRoot 'DemoStepperTests.cs'), (Join-Path $PSScriptRoot '..\WorkCheckStatus.cs'), (Join-Path $PSScriptRoot 'WorkCheckStatusTests.cs')
[DemoStepperTests]::Run()
[WorkCheckStatusTests]::Run()
