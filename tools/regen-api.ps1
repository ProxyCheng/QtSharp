param(
    [switch]$SkipExtract,
    [switch]$Adopt,
    [int]$Jobs = 0
)
$ErrorActionPreference = "Continue"
# The repository root is this script's parent, so a clone can live anywhere; everything below runs
# relative to it whatever the caller's working directory was.
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$genBin = "$root\gen\bin\Release\net10.0\win-x64"

# The binding is one C# file per Qt header, so the determinism gate hashes the generated tree
# (relative path + bytes, sorted) rather than a single file.
function Get-GeneratedTreeHash([string]$dir) {
    $files = Get-ChildItem -LiteralPath $dir -Recurse -File |
        Where-Object { $_.Extension -eq '.cs' -or $_.Extension -eq '.cpp' } | Sort-Object FullName
    $stream = New-Object System.IO.MemoryStream
    try {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($dir.Length).TrimStart('\', '/')
            $name = [System.Text.Encoding]::UTF8.GetBytes($relative + "`n")
            $stream.Write($name, 0, $name.Length)
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            $stream.Write($bytes, 0, $bytes.Length)
        }
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return (-join ($sha.ComputeHash($stream.ToArray()) | ForEach-Object { $_.ToString('x2') }))
        } finally { $sha.Dispose() }
    } finally { $stream.Dispose() }
}

Write-Host "=== [0] Prerequisites ==="
$clangHeaders = Get-ChildItem "$root\lib\lib\clang" -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "include\stddef.h") } | Select-Object -First 1
if (-not $clangHeaders) {
    Write-Host "clang's builtin headers are missing."
    Write-Host "Put a clang resource folder at lib\lib\clang\<version>\include (any clang version),"
    Write-Host "or set QTSHARP_CLANG_RESOURCE_DIR - see README.md, section Prerequisites."
    exit 1
}
Write-Host "clang builtin headers: $($clangHeaders.FullName)"

Write-Host "=== [1] Build gen ==="
dotnet build "$root\gen\gen.csproj" -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "gen build failed"; exit 1 }

if (-not $SkipExtract) {
    Write-Host "=== [2] Extract model (extract2: parse-only, one translation unit) ==="
    Push-Location $genBin
    & "$genBin\qtgen.exe" extract2 "$root\out\qtsharp.api.json.new" *> "$root\out\extract.log"
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) { Write-Host "extract failed (exit=$rc)"; Get-Content "$root\out\extract.log" | Select-String "EXCEPTION|FAIL" | Select-Object -First 3; exit 1 }

    Write-Host "=== model diff gate: freshly extracted model vs the checked-in one ==="
    if (-not (Test-Path "$root\model\qtsharp.api.json")) {
        Write-Host "no checked-in model yet - adopting the extracted one"
        Copy-Item "$root\out\qtsharp.api.json.new" "$root\model\qtsharp.api.json" -Force
    } else {
        $a = (Get-FileHash "$root\model\qtsharp.api.json").Hash
        $b = (Get-FileHash "$root\out\qtsharp.api.json.new").Hash
        if ($a -eq $b) {
            Write-Host "model is byte-identical (reproducible extraction)"
        } elseif ($Adopt) {
            Write-Host "MODEL CHANGED - adopting deliberately (diff above):"
            git -C $root diff --no-index -- "$root\model\qtsharp.api.json" "$root\out\qtsharp.api.json.new" | Select-Object -First 40
            Copy-Item "$root\out\qtsharp.api.json.new" "$root\model\qtsharp.api.json" -Force
        } else {
            # One parse of one translation unit is reproducible by construction (verified: three
            # runs give the same hash), so a mismatch is a real API change or a changed Roots
            # table: review it and re-run with -Adopt.
            Write-Host "MODEL CHANGED - review the diff, then re-run with -Adopt:"
            git -C $root diff --no-index -- "$root\model\qtsharp.api.json" "$root\out\qtsharp.api.json.new" | Select-Object -First 60
            exit 1
        }
    }
}

Write-Host "=== [3] Codegen (twice, to prove determinism) ==="
Push-Location $genBin
& "$genBin\qtgen.exe" codegen "$root\model\qtsharp.api.json" "$root\out\api" *> "$root\out\codegen.log"
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "codegen failed"; Get-Content "$root\out\codegen.log" | Select-Object -Last 10; exit 1 }
& "$genBin\qtgen.exe" codegen "$root\model\qtsharp.api.json" "$root\out\api2" *> "$root\out\codegen2.log"
Pop-Location
$h1 = Get-GeneratedTreeHash "$root\out\api"
$h2 = Get-GeneratedTreeHash "$root\out\api2"
if ($h1 -eq $h2) { Write-Host "codegen is deterministic (identical generated tree on two runs)" }
else { Write-Host "CODEGEN NOT DETERMINISTIC"; exit 1 }

Write-Host "=== [4] Build native bindings ==="
& cmd /c "`"$root\build_api.bat`"" *> "$root\out\api_native.log"
if ($LASTEXITCODE -ne 0) { Write-Host "native build failed"; Get-Content "$root\out\api_native.log" | Select-String "error" | Select-Object -First 6; exit 1 }

Write-Host "=== [5] Build the demo library, then build + run the smoke tests ==="
# The ported examples themselves: the generated binding, the managed runtime and every `api-*`
# window in one assembly. testbind (and samples/hello) only reference it.
dotnet build "$root\demos\QtSharp.Demos.csproj" -c Release --nologo *> "$root\out\api_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "demos build failed"
    Select-String -Path "$root\out\api_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

dotnet build "$root\testbind\testbind.csproj" -c Release --nologo *> "$root\out\api_testbuild.log"
if ($LASTEXITCODE -ne 0) { Write-Host "testbind build failed"; Select-String -Path "$root\out\api_testbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }; exit 1 }

# What is covered, in the order the library lists it: the QObject smoke test; the AnalogClock; the
# LineEdits, Calculator, SpinBoxes, Sliders, ShapedClock, Shortcut Editor and Calendar widget
# examples; the whole QtWidgets tour; the graphics view family; the QtCore tour (files, JSON, ids,
# timers, threads, IPC); the input events; ownership; the managed model/delegate/proxy/completer/
# star-delegate examples; the document viewer; the system tray; the painting examples (basic
# drawing, gradients, painter paths, composition, transformations, path stroking); the meta-object
# example. Each one drives the generated API, checks what it sees and ends in [SELFTEST OK].
#
# The list comes from the library itself (ApiDemos.Names), so a new example is covered by the gate
# as soon as it is registered. The windows are independent processes, so tools\run-window-tests.ps1
# runs several at a time; each result line names the window, and a failure prints that window's own
# output, so nothing is lost by running them concurrently.
& "$root\tools\run-window-tests.ps1" -Exe "$root\testbind\bin\Release\net10.0\testapp.exe" `
    -Label "demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# The sample a user starts from: a project outside this repository's test app that imports only
# QtSharp.Api.props and references the demo library. Covered here so the "write your own UI" path
# cannot rot, together with the second entry point into the demos ("hello <demo>").
Write-Host "=== [6] Build + run the standalone sample ==="
dotnet build "$root\samples\hello\Hello.csproj" -c Release --nologo *> "$root\out\api_samplebuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "sample build failed"
    Select-String -Path "$root\out\api_samplebuild.log" -Pattern "error CS" | Select-Object -First 6 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

# The sample's own window.
$env:HELLO_AUTOCLOSE_MS = "1500"
& "$root\samples\hello\bin\Release\net10.0\hello.exe"
$sampleExit = $LASTEXITCODE
Remove-Item Env:\HELLO_AUTOCLOSE_MS -ErrorAction SilentlyContinue
if ($sampleExit -ne 0) { exit $sampleExit }

# The sample's own C#-declared signal and properties (the pyqtSignal / pyqtProperty equivalent,
# samples/hello/HelloCounter.cs) - a windowless run that checks itself.
& "$root\samples\hello\bin\Release\net10.0\hello.exe" selftest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The same sample running a ported example, which is what makes it a usable trial entry.
& "$root\samples\hello\bin\Release\net10.0\hello.exe" api-transform
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# And its list of them.
$names = (& "$root\samples\hello\bin\Release\net10.0\hello.exe" help) -join "`n"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($names -notmatch 'api-pathstroke') { Write-Host "hello help did not list the demos"; exit 1 }

exit 0
