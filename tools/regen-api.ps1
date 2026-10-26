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

$baseModules = @('qtcore', 'qtgui', 'qtwidgets', 'qtnetwork', 'qtsql', 'qtsvg', 'qtsvgwidgets', 'qtprintsupport', 'qtxml', 'qtopengl', 'qtopenglwidgets')
$extensionModules = @('charts', 'qt3dcore', 'qt3drender', 'qt3dinput', 'qt3dlogic', 'qt3danimation', 'qt3dextras', 'qtwebsockets')

if (-not $SkipExtract) {
    Write-Host "=== [2b] Extract the addon models (each is checked in, extracted from its own headers) ==="
    New-Item -ItemType Directory -Path "$root\out\addon-models" -Force | Out-Null
    Push-Location $genBin
    foreach ($module in $extensionModules) {
        & "$genBin\qtgen.exe" extract2 "$root\out\addon-models\qtsharp.$module.json" --module $module *> "$root\out\extract-$module.log"
        if ($LASTEXITCODE -ne 0) {
            Pop-Location
            Write-Host "addon extraction failed: $module"
            Get-Content "$root\out\extract-$module.log" | Select-Object -Last 10
            exit 1
        }
    }
    Pop-Location
    # The addon models are extracted rather than derived, so they get the same reproducibility gate
    # as the combined base model: a difference means the Qt kit (or the extractor) changed.
    $addonChanged = @()
    foreach ($module in $extensionModules) {
        $fresh = "$root\out\addon-models\qtsharp.$module.json"
        $checked = "$root\model\qtsharp.$module.json"
        if (-not (Test-Path $checked) -or (Get-FileHash $fresh).Hash -ne (Get-FileHash $checked).Hash) {
            $addonChanged += $module
        }
    }
    if ($addonChanged.Count -eq 0) {
        Write-Host "addon models are byte-identical (reproducible extraction)"
    } elseif ($Adopt) {
        Write-Host "ADDON MODELS CHANGED - adopting deliberately: $($addonChanged -join ', ')"
        foreach ($module in $addonChanged) {
            Copy-Item "$root\out\addon-models\qtsharp.$module.json" "$root\model\qtsharp.$module.json" -Force
        }
    } else {
        Write-Host "ADDON MODELS CHANGED - review the diff, then re-run with -Adopt: $($addonChanged -join ', ')"
        foreach ($module in $addonChanged) {
            git -C $root diff --no-index -- "$root\model\qtsharp.$module.json" "$root\out\addon-models\qtsharp.$module.json" | Select-Object -First 30
        }
        exit 1
    }
}

Write-Host "=== [3] Split the combined base model into one model per Qt module ==="
Push-Location $genBin
& "$genBin\qtgen.exe" split-core "$root\model\qtsharp.api.json" "$root\out\split-models" *> "$root\out\split-core.log"
$splitExit = $LASTEXITCODE
Pop-Location
if ($splitExit -ne 0) {
    Write-Host "split-core failed"
    Get-Content "$root\out\split-core.log" | Select-Object -Last 10
    exit 1
}

# The split models under model/ are the input the ten assemblies are generated from, so they are
# checked in. The split is deterministic; a difference means the combined model or the module
# registry changed, which is reviewed and adopted like the combined model itself.
$splitChanged = @()
foreach ($module in $baseModules) {
    $fresh = "$root\out\split-models\qtsharp.$module.json"
    $checked = "$root\model\qtsharp.$module.json"
    if (-not (Test-Path $checked) -or (Get-FileHash $fresh).Hash -ne (Get-FileHash $checked).Hash) {
        $splitChanged += $module
    }
}
if ($splitChanged.Count -eq 0) {
    Write-Host "split models are byte-identical (deterministic split)"
} elseif ($Adopt) {
    Write-Host "SPLIT MODELS CHANGED - adopting deliberately: $($splitChanged -join ', ')"
    foreach ($module in $splitChanged) {
        Copy-Item "$root\out\split-models\qtsharp.$module.json" "$root\model\qtsharp.$module.json" -Force
    }
} else {
    Write-Host "SPLIT MODELS CHANGED - review the diff, then re-run with -Adopt: $($splitChanged -join ', ')"
    foreach ($module in $splitChanged) {
        git -C $root diff --no-index -- "$root\model\qtsharp.$module.json" "$root\out\split-models\qtsharp.$module.json" | Select-Object -First 30
    }
    exit 1
}

Write-Host "=== [3b] Codegen (twice, to prove determinism) ==="
Push-Location $genBin
# The combined model produces only the shared native glue now: the ten assemblies come from the
# split models, and the addons from their own models.
& "$genBin\qtgen.exe" codegen "$root\model\qtsharp.api.json" "$root\out\api" --native-only *> "$root\out\codegen.log"
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "codegen (native glue) failed"; Get-Content "$root\out\codegen.log" | Select-Object -Last 10; exit 1 }
$allModules = $baseModules + $extensionModules
foreach ($module in $allModules) {
    & "$genBin\qtgen.exe" codegen "$root\model\qtsharp.$module.json" "$root\out\$module" --module $module *> "$root\out\codegen-$module.log"
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "codegen ($module) failed"; Get-Content "$root\out\codegen-$module.log" | Select-Object -Last 10; exit 1 }
    & "$genBin\qtgen.exe" codegen "$root\model\qtsharp.$module.json" "$root\out\.determinism\$module" --module $module *> "$root\out\codegen-$module-check.log"
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "codegen ($module, check run) failed"; exit 1 }
}
Pop-Location
foreach ($module in $allModules) {
    $h1 = Get-GeneratedTreeHash "$root\out\$module"
    $h2 = Get-GeneratedTreeHash "$root\out\.determinism\$module"
    if ($h1 -ne $h2) { Write-Host "CODEGEN NOT DETERMINISTIC: $module"; exit 1 }
}
Remove-Item "$root\out\.determinism" -Recurse -Force
Write-Host "codegen is deterministic for all $($allModules.Count) modules (identical trees on two runs)"

Write-Host "=== [4] Build native bindings ==="
& cmd /c "`"$root\build_api.bat`"" *> "$root\out\api_native.log"
if ($LASTEXITCODE -ne 0) { Write-Host "native build failed"; Get-Content "$root\out\api_native.log" | Select-String "error" | Select-Object -First 6; exit 1 }

Write-Host "=== [4a] Build the ten base assemblies (QtSharp.Api) ==="
dotnet build "$root/nuget/QtSharp.Api/QtSharp.Api.csproj" -c Release --nologo *> "$root/out/base_modulesbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "base module build failed"
    Select-String -Path "$root/out/base_modulesbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

# The extension modules are separate assemblies/packages. Their model and codegen ran in [3b];
# their native glue is built by the parameterized script, and the official Qt3D examples then run
# as their conformance suite, exactly like the core demos below.
Write-Host "=== [4b] Extension modules (Charts, Qt3D's six modules) ==="
foreach ($module in $extensionModules) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\tools\build-native-module.ps1" -Module $module *> "$root\out\native-$module.log"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$module native build failed"
        Get-Content "$root\out\native-$module.log" | Select-String "error" | Select-Object -First 6
        exit 1
    }
}
Write-Host "=== [4c] Build the Charts package assembly ==="
dotnet build "$root/nuget/QtSharp.Charts/QtSharp.Charts.csproj" -c Release --nologo *> "$root/out/chartsbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Charts build failed"
    Select-String -Path "$root/out/chartsbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

dotnet build "$root\demos.qt3d\QtSharp.Qt3D.Demos.csproj" -c Release --nologo *> "$root\out\qt3d_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Qt3D demos build failed"
    Select-String -Path "$root\out\qt3d_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}
& "$root\tools\run-window-tests.ps1" -Exe "$root\demos.qt3d\bin\Release\net10.0\QtSharp.Qt3D.Demos.exe" -Label "qt3d-demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Qt's official Qt Charts examples: the Charts module's conformance suite, built against the
# separate Charts assembly exactly like a consumer would use it.
dotnet build "$root/demos.charts/QtSharp.Charts.Demos.csproj" -c Release --nologo *> "$root/out/charts_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Charts demos build failed"
    Select-String -Path "$root/out/charts_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}
& "$root/tools/run-window-tests.ps1" -Exe "$root/demos.charts/bin/Release/net10.0/QtSharp.Charts.Demos.exe" -Label "charts-demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== [5] Build the demos and binding tests, then run them ==="
# The official demos reference the ten base module assemblies (not a source-compiled binding);
# the binding tests cover the parts of the API that Qt's examples do not. testbind dispatches both.
dotnet build "$root\demos\QtSharp.Demos.csproj" -c Release --nologo *> "$root\out\api_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "demos build failed"
    Select-String -Path "$root\out\api_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

dotnet build "$root\testbind\testbind.csproj" -c Release --nologo *> "$root\out\api_testbuild.log"
if ($LASTEXITCODE -ne 0) { Write-Host "testbind build failed"; Select-String -Path "$root\out\api_testbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }; exit 1 }

# What is covered, in the order the libraries list it: the official Qt examples
# (qtgui-*, qtwidgets-*) and the binding tests under test-* (core, events, exceptions,
# meta-object, ownership, value types, network, document formats, managed models, graphics view).
# Each one drives the generated API, checks what it sees and ends in [SELFTEST OK].
#
# The list comes from the two catalogs (DemoCatalog.Names and BindingTests.Names), so a new example
# is covered by the gate as soon as it is registered. The windows are independent processes, so
# tools/run-window-tests.ps1 runs several at a time; each result line names the window, and a
# failure prints that window's own output.
& "$root/tools/run-window-tests.ps1" -Exe "$root/testbind/bin/Release/net10.0/testapp.exe" `
    -Label "demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

exit 0
