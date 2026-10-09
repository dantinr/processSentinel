param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
dotnet build ProcessSentinel.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if (-not $SkipTests) {
    & '.\tests\ProcessSentinel.SelfTest\bin\Release\net10.0-windows\ProcessSentinel.SelfTest.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
    $uiTestReport = Join-Path $PSScriptRoot 'artifacts\ui-regression-results.txt'
    $uiTest = Start-Process -FilePath '.\src\ProcessSentinel.App\bin\Release\net10.0-windows\ProcessSentinel.exe' -ArgumentList '--verify-ui',$uiTestReport -WindowStyle Hidden -PassThru
    if (-not $uiTest.WaitForExit(30000)) { throw 'UI regression timed out' }
    Get-Content -LiteralPath $uiTestReport
    if ($uiTest.ExitCode -ne 0) { throw 'UI regression failed' }
}
$releaseDirectory = Join-Path $PSScriptRoot 'Releases\ProcessSentinel-win-x64'
dotnet publish src\ProcessSentinel.App\ProcessSentinel.App.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'UI publish failed' }
dotnet publish src\ProcessSentinel.Collector\ProcessSentinel.Collector.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Collector publish failed' }
dotnet publish tests\ProcessSentinel.SelfTest\ProcessSentinel.SelfTest.csproj -c Release -r win-x64 --self-contained true -o $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Diagnostics publish failed' }
Copy-Item -LiteralPath '.\README.md','.\THIRD-PARTY-NOTICES.md' -Destination $releaseDirectory -Force
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
$productVersion = (Get-Item -LiteralPath (Join-Path $releaseDirectory 'ProcessSentinel.exe')).VersionInfo.ProductVersion.Split('+')[0]
$zipPath = Join-Path $PSScriptRoot "Releases\ProcessSentinel-$productVersion-win-x64.zip"
Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $zipPath -Force
Write-Output "Portable app: $releaseDirectory\ProcessSentinel.exe"
Write-Output "Archive: $zipPath"
