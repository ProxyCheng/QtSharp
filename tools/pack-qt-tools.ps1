param(
    [Parameter(Mandatory = $true)][string]$QtToolsSource,
    [string]$QtVersion = '6.11.2',
    [string]$PackageVersion = '0.1.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $QtToolsSource).Path
$payload = Join-Path $root 'out\qt-tools'
$core = Join-Path $source 'Qt6Core.dll'
if (-not (Test-Path -LiteralPath $core)) { throw 'QtToolsSource must contain the tools and Qt6Core.dll (usually the Qt kit bin directory).' }
$actual = (Get-Item -LiteralPath $core).VersionInfo.FileVersion
if ($actual -ne "$QtVersion.0") { throw "Qt6Core version $actual does not match requested $QtVersion.0" }
$required = @('rcc', 'uic', 'lrelease', 'lupdate')
foreach ($tool in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source "$tool.exe"))) { throw "Missing required tool: $tool.exe" }
}
New-Item -ItemType Directory -Path $payload -Force | Out-Null
foreach ($tool in ($required + @('moc'))) {
    $exe = Join-Path $source "$tool.exe"
    if (Test-Path -LiteralPath $exe) { Copy-Item -LiteralPath $exe -Destination $payload }
}
# lupdate builds that support QML also depend on QtQml and QtNetwork.
foreach ($pattern in @('Qt6Core.dll', 'Qt6Qml.dll', 'Qt6Network.dll', 'msvcp140*.dll', 'vcruntime140*.dll', 'concrt140.dll')) {
    Get-ChildItem -Path (Join-Path $source $pattern) -File | Copy-Item -Destination $payload
}
# Preserve upstream notices when staging tools from an installed Qt distribution.
$notices = Join-Path $source 'QtSharp-ThirdPartyNotices'
if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination $payload -Recurse }
foreach ($tool in $required) {
    # Qt's Windows GUI-subsystem tools may show a message box when no output handles exist.
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $payload "$tool.exe"
    $info.Arguments = '-version'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); throw "$tool startup check timed out." }
        if ($process.ExitCode -ne 0) { throw "$tool startup failed: $($stderr.Result) (exit $($process.ExitCode))." }
        Write-Host $stdout.Result.Trim()
        Write-Host $stderr.Result.Trim()
    } finally { $process.Dispose() }
}
dotnet pack "$PSScriptRoot\QtSharp.Tools\QtSharp.Tools.csproj" -c Release -o "$root\out\packages" "-p:Version=$PackageVersion"
if ($LASTEXITCODE -ne 0) { throw 'QtSharp.Tools pack failed.' }
