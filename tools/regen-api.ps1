param(
    [switch]$SkipExtract
)
$ErrorActionPreference = "Continue"
$root = "D:\QtSharp"
$genBin = "$root\gen\bin\Release\net10.0\win-x64"

Write-Host "=== [1] Build gen ==="
dotnet build "$root\gen\gen.csproj" -c Release --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "gen build failed"; exit 1 }

if (-not $SkipExtract) {
    Write-Host "=== [2] Extract model (also runs the legacy generation) ==="
    Push-Location $genBin
    & "$genBin\qtgen.exe" extract "$root\out\qtsharp.api.json.new" *> "$root\out\extract.log"
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
        if ($a -eq $b) { Write-Host "model is byte-identical (deterministic extraction)" }
        else {
            Write-Host "MODEL CHANGED - review the diff and adopt it deliberately:"
            git -C $root diff --no-index -- "$root\model\qtsharp.api.json" "$root\out\qtsharp.api.json.new" | Select-Object -First 40
            Copy-Item "$root\out\qtsharp.api.json.new" "$root\model\qtsharp.api.json" -Force
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

Write-Host "=== [4] Copy generated C# next to the test app ==="
Copy-Item "$root\out\api\QtSharp.Api.cs" "$root\testbind\QtSharp.Api.cs" -Force

Write-Host "=== [5] Build native bindings ==="
& cmd /c "`"$root\build_api.bat`"" *> "$root\out\api_native.log"
if ($LASTEXITCODE -ne 0) { Write-Host "native build failed"; Get-Content "$root\out\api_native.log" | Select-String "error" | Select-Object -First 6; exit 1 }

Write-Host "=== [6] Build + run the smoke test ==="
dotnet build "$root\testbind\testbind.csproj" -c Release --nologo *> "$root\out\api_testbuild.log"
if ($LASTEXITCODE -ne 0) { Write-Host "testbind build failed"; Select-String -Path "$root\out\api_testbuild.log" -Pattern "error CS" | Select-Object -First 8 | ForEach-Object { Write-Host $_.Line.Trim() }; exit 1 }

& "$root\testbind\bin\Release\net10.0\testapp.exe" api
exit $LASTEXITCODE
