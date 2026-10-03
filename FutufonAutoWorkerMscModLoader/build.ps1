#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$GamePath,
    [string]$MSCLoaderPath,
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $taskVsWhere)) { throw 'Visual Studio Installer/vswhere.exe was not found.' }
$taskMsBuild = @(& $taskVsWhere -products '*' -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe') | Select-Object -First 1
if (!$taskMsBuild) { throw 'Visual Studio MSBuild was not found.' }

$taskBuildInfo = [System.Diagnostics.ProcessStartInfo]::new()
$taskBuildInfo.FileName = $taskMsBuild
$taskBuildInfo.UseShellExecute = $false
$taskBuildInfo.CreateNoWindow = $true
$taskBuildInfo.RedirectStandardOutput = $true
$taskBuildInfo.RedirectStandardError = $true

# Windows may inherit both Path and PATH; child build tasks need one entry.
$taskBuildInfo.Environment.Clear()
$taskEnvironment = [Environment]::GetEnvironmentVariables()
foreach ($taskEnvironmentName in $taskEnvironment.Keys) {
    $taskBuildInfo.Environment[$taskEnvironmentName] = [string]$taskEnvironment[$taskEnvironmentName]
}

$taskBuildInfo.ArgumentList.Add((Join-Path $PSScriptRoot 'FutufonAutoWorkerMscModLoader.csproj'))
foreach ($taskBuildArgument in @('/t:Rebuild', "/p:Configuration=$Configuration", '/p:UseSharedCompilation=false', '/nologo', '/verbosity:minimal')) {
    $taskBuildInfo.ArgumentList.Add($taskBuildArgument)
}
if ($GamePath) { $taskBuildInfo.ArgumentList.Add("/p:GamePath=$GamePath") }
if ($MSCLoaderPath) { $taskBuildInfo.ArgumentList.Add("/p:MSCLoaderPath=$MSCLoaderPath") }
if ($Deploy) { $taskBuildInfo.ArgumentList.Add('/p:DeployMod=true') }

$taskBuildProcess = [System.Diagnostics.Process]::Start($taskBuildInfo)
$taskBuildOutput = $taskBuildProcess.StandardOutput.ReadToEndAsync()
$taskBuildErrors = $taskBuildProcess.StandardError.ReadToEndAsync()
$taskBuildProcess.WaitForExit()
Write-Output $taskBuildOutput.Result
if ($taskBuildErrors.Result) { Write-Output $taskBuildErrors.Result }
exit $taskBuildProcess.ExitCode
