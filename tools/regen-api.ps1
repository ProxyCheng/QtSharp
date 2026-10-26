param(
    [switch]$SkipExtract,
    [switch]$Adopt,
    [int]$Jobs = 0
)
$ErrorActionPreference = "Continue"
# The repository root is this script's parent, so a clone can live anywhere; everything below runs
# relative to it whatever the caller's working directory was. Paths use forward slashes, which work
# on Windows and are the separator on Linux (PowerShell does not treat a backslash as one there).
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

# Host facts. Windows PowerShell 5.1 has none of the $Is* automatic variables, hence the version
# check; the RID is the shape gen.csproj builds into and the native tools use.
$onWindows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
$rid = if ($onWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else { 'osx-x64' }
$genBin = "$root/gen/bin/Release/net10.0/$rid"
$genExe = "$genBin/" + $(if ($onWindows) { 'qtgen.exe' } else { 'qtgen' })
$exeSuffix = if ($onWindows) { '.exe' } else { '' }

# The API model is the Windows one: extraction parses the MSVC Qt headers with MSVC compatibility
# flags. Codegen and the split are platform-neutral, so a Linux checkout rebuilds everything from
# the checked-in model and only skips the extraction gate.
$extract = -not $SkipExtract
if ($extract -and -not $onWindows) {
    Write-Host "=== [2] Model extraction is Windows-only (MSVC Qt headers); using the checked-in model ==="
    $extract = $false
}

# A clone (or a fresh worktree) has no out/ yet, and every step below writes its log there.
New-Item -ItemType Directory -Path "$root/out" -Force | Out-Null

# The steps that do not depend on each other - the addon extractions, the codegen runs and the
# native compiles - go through one process pool, at most this many at a time. 0 means "pick a
# sensible number"; -Jobs 1 gives the old one-at-a-time behaviour. (The window tests below get the
# same -Jobs value and keep their own, smaller default.)
$buildJobs = if ($Jobs -gt 0) { $Jobs } else { [Math]::Max(1, [Math]::Min(8, [Environment]::ProcessorCount)) }
$dotnet = (Get-Command dotnet).Source
$powershell = if ($onWindows) { (Get-Command powershell).Source } else { (Get-Command pwsh).Source }
. (Join-Path $PSScriptRoot 'parallel.ps1')

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
if ($extract) {
    $clangHeaders = Get-ChildItem "$root/lib/lib/clang" -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "include/stddef.h") } | Select-Object -First 1
    if (-not $clangHeaders) {
        Write-Host "clang's builtin headers are missing."
        Write-Host "Put a clang resource folder at lib/lib/clang/<version>/include (any clang version),"
        Write-Host "or set QTSHARP_CLANG_RESOURCE_DIR - see README.md, section Prerequisites."
        exit 1
    }
    Write-Host "clang builtin headers: $($clangHeaders.FullName)"
} else {
    Write-Host "extraction skipped - the checked-in model under model/ is the input"
}

Write-Host "=== [1] Build gen ==="
dotnet build "$root/gen/gen.csproj" -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "gen build failed"; exit 1 }

if ($extract) {
    Write-Host "=== [2] Extract model (extract2: parse-only, one translation unit) ==="
    Push-Location $genBin
    & $genExe extract2 "$root/out/qtsharp.api.json.new" *> "$root/out/extract.log"
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) { Write-Host "extract failed (exit=$rc)"; Get-Content "$root/out/extract.log" | Select-String "EXCEPTION|FAIL" | Select-Object -First 3; exit 1 }

    Write-Host "=== model diff gate: freshly extracted model vs the checked-in one ==="
    if (-not (Test-Path "$root/model/qtsharp.api.json")) {
        Write-Host "no checked-in model yet - adopting the extracted one"
        Copy-Item "$root/out/qtsharp.api.json.new" "$root/model/qtsharp.api.json" -Force
    } else {
        $a = (Get-FileHash "$root/model/qtsharp.api.json").Hash
        $b = (Get-FileHash "$root/out/qtsharp.api.json.new").Hash
        if ($a -eq $b) {
            Write-Host "model is byte-identical (reproducible extraction)"
        } elseif ($Adopt) {
            Write-Host "MODEL CHANGED - adopting deliberately (diff above):"
            git -C $root diff --no-index -- "$root/model/qtsharp.api.json" "$root/out/qtsharp.api.json.new" | Select-Object -First 40
            Copy-Item "$root/out/qtsharp.api.json.new" "$root/model/qtsharp.api.json" -Force
        } else {
            # One parse of one translation unit is reproducible by construction (verified: three
            # runs give the same hash), so a mismatch is a real API change or a changed Roots
            # table: review it and re-run with -Adopt.
            Write-Host "MODEL CHANGED - review the diff, then re-run with -Adopt:"
            git -C $root diff --no-index -- "$root/model/qtsharp.api.json" "$root/out/qtsharp.api.json.new" | Select-Object -First 60
            exit 1
        }
    }
}

$baseModules = @('qtcore', 'qtgui', 'qtwidgets', 'qtnetwork', 'qtsvg', 'qtsvgwidgets', 'qtprintsupport', 'qtopengl', 'qtopenglwidgets')
$extensionModules = @('charts', 'qt3dcore', 'qt3drender', 'qt3dinput', 'qt3dlogic', 'qt3danimation', 'qt3dextras', 'qtmultimedia', 'qtpositioning')
$allModules = $baseModules + $extensionModules

if ($extract) {
    Write-Host "=== [2b] Extract the addon models (each is checked in, extracted from its own headers) ==="
    New-Item -ItemType Directory -Path "$root/out/addon-models" -Force | Out-Null
    $extractTasks = foreach ($module in $extensionModules) {
        [pscustomobject]@{
            Name = $module
            FilePath = $genExe
            Arguments = @('extract2', "$root/out/addon-models/qtsharp.$module.json", '--module', $module)
            WorkingDirectory = $genBin
            LogPath = "$root/out/extract-$module.log"
        }
    }
    foreach ($result in @(Invoke-ProcessPool -Tasks @($extractTasks) -Throttle $buildJobs)) {
        if ($result.ExitCode -ne 0) {
            Write-Host "addon extraction failed: $($result.Name)"
            Get-Content $result.LogPath -ErrorAction SilentlyContinue | Select-Object -Last 10 | Out-Host
            exit 1
        }
    }
    # The addon models are extracted rather than derived, so they get the same reproducibility gate
    # as the combined base model: a difference means the Qt kit (or the extractor) changed.
    $addonChanged = @()
    foreach ($module in $extensionModules) {
        $fresh = "$root/out/addon-models/qtsharp.$module.json"
        $checked = "$root/model/qtsharp.$module.json"
        if (-not (Test-Path $checked) -or (Get-FileHash $fresh).Hash -ne (Get-FileHash $checked).Hash) {
            $addonChanged += $module
        }
    }
    if ($addonChanged.Count -eq 0) {
        Write-Host "addon models are byte-identical (reproducible extraction)"
    } elseif ($Adopt) {
        Write-Host "ADDON MODELS CHANGED - adopting deliberately: $($addonChanged -join ', ')"
        foreach ($module in $addonChanged) {
            Copy-Item "$root/out/addon-models/qtsharp.$module.json" "$root/model/qtsharp.$module.json" -Force
        }
    } else {
        Write-Host "ADDON MODELS CHANGED - review the diff, then re-run with -Adopt: $($addonChanged -join ', ')"
        foreach ($module in $addonChanged) {
            git -C $root diff --no-index -- "$root/model/qtsharp.$module.json" "$root/out/addon-models/qtsharp.$module.json" | Select-Object -First 30
        }
        exit 1
    }
}

Write-Host "=== [3] Split the combined base model into one model per Qt module ==="
Push-Location $genBin
& $genExe split-core "$root/model/qtsharp.api.json" "$root/out/split-models" *> "$root/out/split-core.log"
$splitExit = $LASTEXITCODE
Pop-Location
if ($splitExit -ne 0) {
    Write-Host "split-core failed"
    Get-Content "$root/out/split-core.log" | Select-Object -Last 10
    exit 1
}

# The split models under model/ are the input the ten assemblies are generated from, so they are
# checked in. The split is deterministic; a difference means the combined model or the module
# registry changed, which is reviewed and adopted like the combined model itself.
$splitChanged = @()
foreach ($module in $baseModules) {
    $fresh = "$root/out/split-models/qtsharp.$module.json"
    $checked = "$root/model/qtsharp.$module.json"
    if (-not (Test-Path $checked) -or (Get-FileHash $fresh).Hash -ne (Get-FileHash $checked).Hash) {
        $splitChanged += $module
    }
}
if ($splitChanged.Count -eq 0) {
    Write-Host "split models are byte-identical (deterministic split)"
} elseif ($Adopt) {
    Write-Host "SPLIT MODELS CHANGED - adopting deliberately: $($splitChanged -join ', ')"
    foreach ($module in $splitChanged) {
        Copy-Item "$root/out/split-models/qtsharp.$module.json" "$root/model/qtsharp.$module.json" -Force
    }
} else {
    Write-Host "SPLIT MODELS CHANGED - review the diff, then re-run with -Adopt: $($splitChanged -join ', ')"
    foreach ($module in $splitChanged) {
        git -C $root diff --no-index -- "$root/model/qtsharp.$module.json" "$root/out/split-models/qtsharp.$module.json" | Select-Object -First 30
    }
    exit 1
}

Write-Host "=== [3b] Codegen (twice, to prove determinism) ==="
# The combined model produces only the shared native glue now: the ten assemblies come from the
# split models, and the addons from their own models. The second run of every module writes to a
# separate tree - out/.determinism/<module> - so both runs can go through the pool together.
$codegenTasks = @(
    [pscustomobject]@{
        Name = 'native glue'
        FilePath = $genExe
        Arguments = @('codegen', "$root/model/qtsharp.api.json", "$root/out/api", '--native-only')
        WorkingDirectory = $genBin
        LogPath = "$root/out/codegen.log"
    }
)
foreach ($module in $allModules) {
    $codegenTasks += [pscustomobject]@{
        Name = $module
        FilePath = $genExe
        Arguments = @('codegen', "$root/model/qtsharp.$module.json", "$root/out/$module", '--module', $module)
        WorkingDirectory = $genBin
        LogPath = "$root/out/codegen-$module.log"
    }
    $codegenTasks += [pscustomobject]@{
        Name = "$module (check run)"
        FilePath = $genExe
        Arguments = @('codegen', "$root/model/qtsharp.$module.json", "$root/out/.determinism/$module", '--module', $module)
        WorkingDirectory = $genBin
        LogPath = "$root/out/codegen-$module-check.log"
    }
}
foreach ($result in @(Invoke-ProcessPool -Tasks $codegenTasks -Throttle $buildJobs)) {
    if ($result.ExitCode -ne 0) {
        Write-Host "codegen ($($result.Name)) failed"
        Get-Content $result.LogPath -ErrorAction SilentlyContinue | Select-Object -Last 10 | Out-Host
        exit 1
    }
}
foreach ($module in $allModules) {
    $h1 = Get-GeneratedTreeHash "$root/out/$module"
    $h2 = Get-GeneratedTreeHash "$root/out/.determinism/$module"
    if ($h1 -ne $h2) { Write-Host "CODEGEN NOT DETERMINISTIC: $module"; exit 1 }
}
Remove-Item "$root/out/.determinism" -Recurse -Force
Write-Host "codegen is deterministic for all $($allModules.Count) modules (identical trees on two runs)"

Write-Host "=== [4] Build the native bindings and the ten base assemblies (in parallel) ==="
# Three kinds of independent work: the core wrapper, one wrapper per extension module, and the
# managed base assemblies. None of them needs another's output, so they share one pool. The
# native tools are the host's: MSVC through build_api.bat on Windows, g++ through build_api.sh
# on Linux.
$apiTask = if ($onWindows) {
    [pscustomobject]@{
        Name = 'api'
        FilePath = $env:ComSpec
        RawArguments = '/c "' + "$root/build_api.bat" + '"'
        WorkingDirectory = $root
        LogPath = "$root/out/api_native.log"
    }
} else {
    [pscustomobject]@{
        Name = 'api'
        FilePath = 'bash'
        Arguments = @("$root/build_api.sh")
        WorkingDirectory = $root
        LogPath = "$root/out/api_native.log"
    }
}
$buildTasks = @(
    $apiTask,
    [pscustomobject]@{
        Name = 'QtSharp.Api'
        FilePath = $dotnet
        Arguments = @('build', "$root/nuget/QtSharp.Api/QtSharp.Api.csproj", '-c', 'Release', '--nologo')
        WorkingDirectory = $root
        LogPath = "$root/out/base_modulesbuild.log"
    }
)
foreach ($module in $extensionModules) {
    $buildTasks += if ($onWindows) {
        [pscustomobject]@{
            Name = $module
            FilePath = $powershell
            Arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$root/tools/build-native-module.ps1", '-Module', $module)
            WorkingDirectory = $root
            LogPath = "$root/out/native-$module.log"
        }
    } else {
        [pscustomobject]@{
            Name = $module
            FilePath = 'bash'
            Arguments = @("$root/tools/build-native-module.sh", $module)
            WorkingDirectory = $root
            LogPath = "$root/out/native-$module.log"
        }
    }
}
foreach ($result in @(Invoke-ProcessPool -Tasks $buildTasks -Throttle $buildJobs)) {
    if ($result.ExitCode -eq 0) { continue }
    switch ($result.Name) {
        'QtSharp.Api' {
            Write-Host "base module build failed"
            Select-String -Path $result.LogPath -Pattern "error CS" | Select-Object -First 8 |
                ForEach-Object { Write-Host $_.Line.Trim() }
        }
        'api' {
            Write-Host "native build failed"
            Get-Content $result.LogPath -ErrorAction SilentlyContinue | Select-String "error" |
                Select-Object -First 6 | ForEach-Object { Write-Host $_.Line.Trim() }
        }
        default {
            Write-Host "$($result.Name) native build failed"
            Get-Content $result.LogPath -ErrorAction SilentlyContinue | Select-String "error" |
                Select-Object -First 6 | ForEach-Object { Write-Host $_.Line.Trim() }
        }
    }
    exit 1
}

# The extension modules are separate assemblies/packages. Their model and codegen ran in [3b];
# their native glue was built with the core above, and the official Qt3D and Qt Charts examples
# then run as their conformance suite, exactly like the core demos below.
Write-Host "=== [4a] Build the Charts package assembly ==="
dotnet build "$root/nuget/QtSharp.Charts/QtSharp.Charts.csproj" -c Release --nologo *> "$root/out/chartsbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Charts build failed"
    Select-String -Path "$root/out/chartsbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}

Write-Host "=== [4b] Build and run the official Qt3D examples ==="
dotnet build "$root/demos.qt3d/QtSharp.Qt3D.Demos.csproj" -c Release --nologo *> "$root/out/qt3d_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Qt3D demos build failed"
    Select-String -Path "$root/out/qt3d_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}
& "$root/tools/run-window-tests.ps1" -Exe "$root/demos.qt3d/bin/Release/net10.0/QtSharp.Qt3D.Demos$exeSuffix" -Label "qt3d-demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Qt's official Qt Charts examples: the Charts module's conformance suite, built against the
# separate Charts assembly exactly like a consumer would use it.
Write-Host "=== [4c] Build and run the official Qt Charts examples ==="
dotnet build "$root/demos.charts/QtSharp.Charts.Demos.csproj" -c Release --nologo *> "$root/out/charts_demosbuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Charts demos build failed"
    Select-String -Path "$root/out/charts_demosbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}
& "$root/tools/run-window-tests.ps1" -Exe "$root/demos.charts/bin/Release/net10.0/QtSharp.Charts.Demos$exeSuffix" -Label "charts-demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== [5] Build the demos and binding tests, then run them ==="
# The official demos reference the ten base module assemblies (not a source-compiled binding); the
# binding tests cover the parts of the API that Qt's examples do not. testbind references both
# demos/QtSharp.Demos.csproj and tests/QtSharp.BindingTests.csproj, so this one build produces all
# three assemblies - a separate demos build would only repeat the first half.
dotnet build "$root/testbind/testbind.csproj" -c Release --nologo *> "$root/out/api_testbuild.log"
if ($LASTEXITCODE -ne 0) { Write-Host "testbind build failed"; Select-String -Path "$root/out/api_testbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }; exit 1 }

# What is covered, in the order the libraries list it: the official Qt examples
# (qtgui-*, qtwidgets-*) and the binding tests under test-* (core, events, exceptions,
# meta-object, ownership, value types, network, document formats, managed models, graphics view).
# Each one drives the generated API, checks what it sees and ends in [SELFTEST OK].
#
# The list comes from the two catalogs (DemoCatalog.Names and BindingTests.Names), so a new example
# is covered by the gate as soon as it is registered. The windows are independent processes, so
# tools/run-window-tests.ps1 runs several at a time; each result line names the window, and a
# failure prints that window's own output.
& "$root/tools/run-window-tests.ps1" -Exe "$root/testbind/bin/Release/net10.0/testapp$exeSuffix" `
    -Label "demos" -Jobs $Jobs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

exit 0
