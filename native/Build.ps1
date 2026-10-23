$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$workspaceRoot = Split-Path $nativeRoot -Parent
$localDotnet = Join-Path $workspaceRoot '.tools\dotnet\dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet).Source }
$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:NUGET_PACKAGES = Join-Path $workspaceRoot '.tools\packages'
$env:APPDATA = Join-Path $workspaceRoot '.tools\sdk-data'
$config = Join-Path $nativeRoot 'NuGet.Config'
$desktop = Join-Path $nativeRoot 'Waive.Desktop\Waive.Desktop.csproj'
$checks = Join-Path $nativeRoot 'Waive.Checks\Waive.Checks.csproj'
foreach ($project in @($desktop, $checks)) {
    & $dotnetExe restore $project --configfile $config --disable-parallel -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Dependency resolution failed.' }
    & $dotnetExe build $project -c Release --no-restore -p:UseSharedCompilation=false -m:1 -nr:false --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
}
& $dotnetExe (Join-Path $nativeRoot 'Waive.Checks\bin\Release\net10.0\Waive.Checks.dll') $workspaceRoot
if ($LASTEXITCODE -ne 0) { throw 'Protocol checks failed.' }
$output = Join-Path $workspaceRoot 'dist\WaiveGK6XPlus'
& $dotnetExe publish $desktop -c Release --no-restore -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false --nologo -o $output
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
Copy-Item -LiteralPath (Join-Path $nativeRoot 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $nativeRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $nativeRoot 'GK6CTL-LICENSE.txt') -Destination $output
Write-Output "Native build ready: $output"
