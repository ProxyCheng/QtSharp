param(
    [switch]$SkipExtract,
    [switch]$Adopt
)
$ErrorActionPreference = "Continue"
$root = "D:\QtSharp"
$genBin = "$root\gen\bin\Release\net10.0\win-x64"

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
$h1 = (Get-FileHash "$root\out\api\QtSharp.Api.cs").Hash
$h2 = (Get-FileHash "$root\out\api2\QtSharp.Api.cs").Hash
if ($h1 -eq $h2) { Write-Host "codegen is deterministic (identical output on two runs)" }
else { Write-Host "CODEGEN NOT DETERMINISTIC"; exit 1 }

Write-Host "=== [4] Build native bindings ==="
& cmd /c "`"$root\build_api.bat`"" *> "$root\out\api_native.log"
if ($LASTEXITCODE -ne 0) { Write-Host "native build failed"; Get-Content "$root\out\api_native.log" | Select-String "error" | Select-Object -First 6; exit 1 }

Write-Host "=== [5] Build + run the smoke tests ==="
dotnet build "$root\testbind\testbind.csproj" -c Release --nologo *> "$root\out\api_testbuild.log"
if ($LASTEXITCODE -ne 0) { Write-Host "testbind build failed"; Select-String -Path "$root\out\api_testbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }; exit 1 }

& "$root\testbind\bin\Release\net10.0\testapp.exe" api
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The AnalogClock demo on the new pipeline: a real Qt event loop, a real widget and a real
# paintEvent override, all reached through generated code.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-analogclock
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The LineEdits demo: layouts, group boxes, combo boxes, validators and signals.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-lineedits
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The QtWidgets tour: menus, tool bars, tabs, splitters, item views, model/view/proxy, a data
# widget mapper, a calendar and an undo stack - the whole widget surface the model now covers.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-widgets
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The graphics view family: scene, view, three item kinds and an off-screen render.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-graphics
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The QtCore value classes: files and directories, JSON, ids, random numbers, timers and the
# command line - the batch that took QtCore from 42 bound classes to 64.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-core
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Ownership: only the wrapper that created an object may release it, and Qt is asked first
# (a reparented QObject belongs to its parent, not to the GC).
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-ownership
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The input events and the rich text types: a wheel turn and a key press are delivered by Qt and
# read through the generated event classes, and a QTextCharFormat is used through its QTextFormat
# base (the model's base lists, not just the layout side).
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-events
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The extension points: a table model and an item delegate written in C#, with Qt itself driving
# them (the view asks for rowCount/data, the delegate is asked to paint).
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-custommodel
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The Calculator example, rewritten on the new pipeline: a managed QToolButton subclass whose
# sizeHint Qt lays out with, and a keyPressEvent override that reads the key code out of a bound
# QKeyEvent - the demo that used to be impossible without binding the input events.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-calculator
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The Spin Boxes example: the whole spin box property surface (range, step, prefix, suffix,
# special value text, integer base, group separator) plus the date and time edits.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-spinboxes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The Shortcut Editor example: a *hierarchical* model written in C#, which needs the protected
# createIndex/beginInsertRows/... the model publishes, plus a delegate that edits the shortcut
# column. This is the demo the legacy pipeline crashed on when a tree node was opened.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-shortcut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Signals and properties declared in C# itself (the pyqtSignal/Property equivalent): the runtime
# builds a meta-object for the managed subclass, so indexOfSignal finds it, connect() works and
# QObject::property reaches the C# accessors.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-metaobject
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The window-shape example: a frameless translucent clock with a round mask and mouse dragging.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-shapedclock
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The sliders example: sliders/scroll bar/dial groups whose layout direction follows the resize.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-sliders
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The calendar widget example: preview/general options/dates/text formats, with the real
# dateChanged signals the legacy binding could not use.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-calendarwidget
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The menus example: menu bar, exclusive alignment actions, status tips and a context menu.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-menus
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The basic layouts example: menu bar in a layout, horizontal/grid/form groups, a scroll area
# and a button box.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-basiclayouts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The scribble example: an off-screen QImage paint area with File/Options/Help menus and Save As.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-scribble
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The widget gallery: the whole widget surface behind a tree of groups, tabs and item views.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-gallery
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The system tray example: tray icon/menu/actions, balloon messages and the close-to-tray logic.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-systray
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The document viewer demo: menus/tool bar/splitter skeleton, QSettings-backed working directory
# and recent files, and the text/JSON/image viewers (no PDF or printing - those modules are not
# shipped, and the official native viewer plugins cannot be loaded from managed code).
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-docviewer
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The basic drawing example: thirteen shapes drawn through QPainter with pen/brush/antialiasing/
# transformation settings, asserted on the pixels they leave behind.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-basicdrawing
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The gradients example: QLinearGradient/QRadialGradient/QConicalGradient, their stops, the three
# spread modes and the QGradient::Preset table.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-gradients
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The painter paths example: nine hand-built QPainterPath objects (arcs, cubics, text outlines, a
# pentagram), the fill rules and the gradient fill of the official RenderArea.
& "$root\testbind\bin\Release\net10.0\testapp.exe" api-painterpaths
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The sample a user starts from: a project outside this repository's test app that only imports
# QtSharp.Api.props. Covered here so the "write your own UI" path cannot rot.
Write-Host "=== [6] Build + run the standalone sample ==="
dotnet build "$root\samples\hello\Hello.csproj" -c Release --nologo *> "$root\out\api_samplebuild.log"
if ($LASTEXITCODE -ne 0) {
    Write-Host "sample build failed"
    Select-String -Path "$root\out\api_samplebuild.log" -Pattern "error CS" | Select-Object -First 6 | ForEach-Object { Write-Host $_.Line.Trim() }
    exit 1
}
$env:HELLO_AUTOCLOSE_MS = "1500"
& "$root\samples\hello\bin\Release\net10.0\hello.exe"
$sampleExit = $LASTEXITCODE
Remove-Item Env:\HELLO_AUTOCLOSE_MS -ErrorAction SilentlyContinue
exit $sampleExit
