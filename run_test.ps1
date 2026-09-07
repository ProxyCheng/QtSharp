$ErrorActionPreference = "Stop"
$root = "D:\QtSharp"
$pyside = "H:\ParseDigitAI\.venv\Lib\site-packages\PySide6"
$shiboken = "D:\NewEditor\messiah-qt6\shiboken6"

dotnet build "$root\testapp\testapp.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = "$root\testapp\bin\Release\net10.0"
Copy-Item "$root\out\native\qt_sharp_min.dll" "$out\" -Force

$env:PATH = "$pyside;$shiboken;" + $env:PATH
$env:QT_PLUGIN_PATH = "$pyside\plugins"

& "$out\testapp.exe"
exit $LASTEXITCODE
