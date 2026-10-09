[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Development')]
param(
    [switch]$SkipTests,
    [switch]$NoRestore,
    [Parameter(ParameterSetName = 'Development')]
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\dev\ProcessSentinel-win-x64'),
    [Parameter(ParameterSetName = 'Package', Mandatory = $true)]
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$versionPath = Join-Path $PSScriptRoot 'VERSION'
$productVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()
if ($productVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'VERSION must contain major.minor.patch, for example 0.1.4.' }
$releaseDirectory = if ($Package) {
    Join-Path $PSScriptRoot "Releases\ProcessSentinel-$productVersion-win-x64"
} else { [System.IO.Path]::GetFullPath($OutputDirectory) }
$zipPath = if ($Package) { Join-Path $PSScriptRoot "Releases\ProcessSentinel-$productVersion-win-x64.zip" } else { $null }
Write-Output "Version: $productVersion (VERSION)"
Write-Output "Output directory: $releaseDirectory"
if ($Package) { Write-Output "Archive: $zipPath" }
$operation = if ($Package) { 'Build, validate and package a formal release' } else { 'Build, validate and update the development app' }
if (-not $PSCmdlet.ShouldProcess($releaseDirectory, $operation)) { return }
$restoreOptions = @()
if ($NoRestore) { $restoreOptions = @('--no-restore') }
dotnet build ProcessSentinel.slnx -c Release @restoreOptions
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not $SkipTests) {
    & '.\tests\ProcessSentinel.SelfTest\bin\Release\net10.0-windows\ProcessSentinel.SelfTest.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
    $uiTestReport = Join-Path $PSScriptRoot 'artifacts\ui-regression-results.txt'
    $uiTest = Start-Process -FilePath '.\src\ProcessSentinel.App\bin\Release\net10.0-windows\ProcessSentinel.exe' -ArgumentList '--verify-ui',('"' + $uiTestReport + '"') -WindowStyle Hidden -PassThru
    if (-not $uiTest.WaitForExit(30000)) { throw 'UI regression timed out' }
    Get-Content -LiteralPath $uiTestReport
    if ($uiTest.ExitCode -ne 0) { throw 'UI regression failed' }
}
dotnet publish src\ProcessSentinel.App\ProcessSentinel.App.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory @restoreOptions
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed' }
dotnet publish src\ProcessSentinel.Collector\ProcessSentinel.Collector.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory @restoreOptions
if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed' }
dotnet publish tests\ProcessSentinel.SelfTest\ProcessSentinel.SelfTest.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory @restoreOptions
if ($LASTEXITCODE -ne 0) { throw 'Diagnostics publish failed' }
Copy-Item -LiteralPath '.\VERSION','.\README.md','.\THIRD-PARTY-NOTICES.md' -Destination $releaseDirectory -Force
$noticesDirectory = Join-Path $releaseDirectory 'third-party'
New-Item -ItemType Directory -Path $noticesDirectory -Force | Out-Null
$packageRoot = (Get-Content -LiteralPath '.\src\ProcessSentinel.Core\obj\project.assets.json' -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name | Select-Object -First 1
$frameworks = (Get-Content -LiteralPath (Join-Path $releaseDirectory 'ProcessSentinel.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
foreach ($framework in $frameworks) {
    if ($framework.name -eq 'Microsoft.NETCore.App') {
        $runtimeDirectory = Join-Path $packageRoot "microsoft.netcore.app.runtime.win-x64\$($framework.version)"
        Copy-Item -LiteralPath (Join-Path $runtimeDirectory 'LICENSE.TXT') -Destination (Join-Path $noticesDirectory 'dotnet-LICENSE.txt') -Force
        Copy-Item -LiteralPath (Join-Path $runtimeDirectory 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $noticesDirectory 'dotnet-THIRD-PARTY-NOTICES.txt') -Force
    }
    if ($framework.name -eq 'Microsoft.WindowsDesktop.App') {
        $runtimeDirectory = Join-Path $packageRoot "microsoft.windowsdesktop.app.runtime.win-x64\$($framework.version)"
        Copy-Item -LiteralPath (Join-Path $runtimeDirectory 'LICENSE') -Destination (Join-Path $noticesDirectory 'windowsdesktop-LICENSE.txt') -Force
    }
}
Write-Output "Portable app: $releaseDirectory\ProcessSentinel.exe"
if ($Package) {
    $builtVersion = (Get-Item -LiteralPath (Join-Path $releaseDirectory 'ProcessSentinel.exe')).VersionInfo.ProductVersion.Split('+')[0]
    if ($builtVersion -ne $productVersion) { throw 'Built program version does not match VERSION.' }
    Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $zipPath -Force
    Write-Output "Archive: $zipPath"
}
