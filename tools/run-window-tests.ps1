param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Label = "windows",
    [int]$Jobs = 0
)
$ErrorActionPreference = "Stop"

# Runs a self-testing GUI executable once per name, up to $Jobs at a time. The windows are
# independent processes, so they are started together; stdout/stderr are collected in memory so a
# failure's output stays attributable. A nonzero exit is retried once, because a window can lose
# the teardown race after every assertion has already passed. Exits with the first failing process's
# exit code, or 0.
#
# The list comes from the executable itself (`--list`), which is what keeps the gate and the CI
# runs from drifting from the name -> window table.
#
# 0 means "pick a sensible number": enough to keep a CI runner busy without making the
# timing-sensitive examples fight over the machine. Pass -Jobs 1 for the old one-at-a-time run.
if ($Jobs -le 0) { $Jobs = [Math]::Max(1, [Math]::Min(6, [Environment]::ProcessorCount)) }

$resolved = (Resolve-Path -LiteralPath $Exe).ProviderPath

# The children are started through System.Diagnostics.Process rather than Start-Process: Windows
# PowerShell's Start-Process -PassThru does not fill in ExitCode unless it also waits, which would
# serialize the pool.
function Invoke-WindowTests {
    param(
        [string]$Exe,
        [string[]]$Names,
        [string]$Label
    )

    $pending = [System.Collections.Generic.List[object]]::new()
    foreach ($name in $Names) { $pending.Add([pscustomobject]@{ Name = $name; Attempt = 0 }) }
    $running = [System.Collections.Generic.List[object]]::new()
    $failed = [System.Collections.Generic.List[object]]::new()
    $assertions = 0
    $started = Get-Date

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($running.Count -lt $Jobs -and $pending.Count -gt 0) {
            $item = $pending[0]
            $pending.RemoveAt(0)
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $Exe
            $startInfo.Arguments = '"' + $item.Name + '"'
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            [void]$process.Start()
            # Read while the child runs: a full stdout pipe would block it, and the output of the
            # larger demos is more than the pipe buffer.
            $running.Add([pscustomobject]@{
                Name = $item.Name; Attempt = $item.Attempt + 1; Process = $process
                Stdout = $process.StandardOutput.ReadToEndAsync()
                Stderr = $process.StandardError.ReadToEndAsync()
                Started = Get-Date
            })
        }

        # Wait for one child to finish, then collect every child that has.
        while ($true) {
            $done = @($running | Where-Object { $_.Process.HasExited })
            if ($done.Count -gt 0) { break }
            Start-Sleep -Milliseconds 50
        }
        foreach ($job in $done) {
            [void]$running.Remove($job)
            $job.Process.WaitForExit()
            $stdout = $job.Stdout.GetAwaiter().GetResult()
            $stderr = $job.Stderr.GetAwaiter().GetResult()
            $exitCode = $job.Process.ExitCode
            $job.Process.Dispose()
            $elapsed = ((Get-Date) - $job.Started).TotalMilliseconds

            if ($exitCode -eq 0) {
                $jobAssertions = @($stdout -split "`r?`n" | Where-Object { $_ -match '^\[PASS\]' }).Count
                $assertions += $jobAssertions
                Write-Host ("  --- {0} [OK, {1} assertions, {2:N0} ms]" -f $job.Name, $jobAssertions, $elapsed)
            } elseif ($job.Attempt -lt 2) {
                Write-Host ("  --- {0} retry after exit 0x{1:X8}" -f $job.Name, $exitCode)
                $pending.Add([pscustomobject]@{ Name = $job.Name; Attempt = $job.Attempt })
            } else {
                $failed.Add([pscustomobject]@{
                    Name = $job.Name; ExitCode = $exitCode; Stdout = $stdout; Stderr = $stderr
                })
                Write-Host ("  --- {0} [FAILED, exit 0x{1:X8}]" -f $job.Name, $exitCode)
            }
        }
    }

    $seconds = ((Get-Date) - $started).TotalSeconds
    if ($failed.Count -gt 0) {
        foreach ($job in $failed) {
            Write-Host ("[FAIL] {0} exited with 0x{1:X8}; output:" -f $job.Name, $job.ExitCode)
            if ($job.Stdout) {
                ($job.Stdout -split "`r?`n") | Select-Object -Last 40 |
                    ForEach-Object { Write-Host $_ }
            }
            if ($job.Stderr) {
                Write-Host "--- stderr"
                ($job.Stderr -split "`r?`n") | Select-Object -Last 20 |
                    ForEach-Object { Write-Host $_ }
            }
        }
        return [int]$failed[0].ExitCode
    }
    Write-Host ("[PASS] {0}: {1} window(s), {2} assertions in {3:N1}s ({4} at a time)" `
        -f $Label, $Names.Count, $assertions, $seconds, $Jobs)
    return 0
}

$names = @(& $resolved --list)
if ($LASTEXITCODE -ne 0 -or -not $names) {
    Write-Host "could not list the $Label windows"
    exit 1
}
exit (Invoke-WindowTests -Exe $resolved -Names $names -Label $Label)
