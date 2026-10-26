# Runs independent child processes several at a time.
#
# The build steps this repository drives - the addon extractions, the codegen runs, the native
# compiles, the module builds - have no dependency on each other, so the only reason they used to be
# serial is that the driver called them in a foreach. Each task is a hashtable (or pscustomobject):
#
#   Name              what to name when it fails
#   FilePath          the executable; a .bat needs cmd.exe, so pass $env:ComSpec + RawArguments
#   Arguments         string[] - quoted for the Windows command line here
#   RawArguments      a command line passed through as-is (for cmd /c "path\to\script.bat")
#   WorkingDirectory  optional, defaults to the caller's
#   LogPath           stdout and stderr are written here when the process exits
#
# Returns one object per task (Name, ExitCode, LogPath). A nonzero exit means nothing to this
# helper: every caller keeps its own error message, so a failing step still says which one it was.
#
# Windows PowerShell 5.1 has no ForEach-Object -Parallel, and Start-Process -PassThru does not fill
# in ExitCode reliably, so this uses System.Diagnostics.Process directly - the same approach as
# tools/run-window-tests.ps1.

function ConvertTo-CommandLine {
    param([string[]]$Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($argument -eq '') { '""' }
        elseif ($argument -match '[\s"]') { '"' + ($argument -replace '"', '\"') + '"' }
        else { $argument }
    }
    return ($quoted -join ' ')
}

function Invoke-ProcessPool {
    param(
        [Parameter(Mandatory = $true)][object[]]$Tasks,
        [int]$Throttle = 1
    )
    if ($Throttle -lt 1) { $Throttle = 1 }

    # The logs are read back with Get-Content and Select-String, which in Windows PowerShell only
    # recognise UTF-8 when it starts with a BOM; the child output itself is decoded by .NET using the
    # console codepage, so writing the string back as UTF-8 preserves non-ASCII compiler messages.
    $logEncoding = New-Object System.Text.UTF8Encoding($true)
    function Write-TaskLog([string]$path, [string]$text) {
        $dir = Split-Path -Parent $path
        if ($dir -and -not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($path, $text, $logEncoding)
    }

    $pending = [System.Collections.Generic.List[object]]::new()
    foreach ($task in $Tasks) { $pending.Add($task) }
    $running = [System.Collections.Generic.List[object]]::new()
    $results = [System.Collections.Generic.List[object]]::new()

    while ($pending.Count -gt 0 -or $running.Count -gt 0) {
        while ($running.Count -lt $Throttle -and $pending.Count -gt 0) {
            $task = $pending[0]
            $pending.RemoveAt(0)

            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $task.FilePath
            $startInfo.Arguments = if ($task.RawArguments) {
                $task.RawArguments
            } else {
                ConvertTo-CommandLine $task.Arguments
            }
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            if ($task.WorkingDirectory) { $startInfo.WorkingDirectory = $task.WorkingDirectory }

            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            try {
                [void]$process.Start()
            } catch {
                if ($task.LogPath) { Write-TaskLog $task.LogPath $_.Exception.Message }
                $results.Add([pscustomobject]@{ Name = $task.Name; ExitCode = -1; LogPath = $task.LogPath })
                continue
            }
            $running.Add([pscustomobject]@{
                Task = $task
                Process = $process
                # Read while the child runs: a full stdout pipe would block it, and a failing
                # compile can print more than the pipe buffer.
                Stdout = $process.StandardOutput.ReadToEndAsync()
                Stderr = $process.StandardError.ReadToEndAsync()
            })
        }

        # Wait for one child to finish, then collect every child that has.
        while ($true) {
            $done = @($running | Where-Object { $_.Process.HasExited })
            if ($done.Count -gt 0) { break }
            Start-Sleep -Milliseconds 25
        }

        foreach ($job in $done) {
            [void]$running.Remove($job)
            $job.Process.WaitForExit()
            $stdout = $job.Stdout.GetAwaiter().GetResult()
            $stderr = $job.Stderr.GetAwaiter().GetResult()
            $exitCode = $job.Process.ExitCode
            $job.Process.Dispose()

            if ($job.Task.LogPath) {
                Write-TaskLog $job.Task.LogPath (([string]$stdout) + [string]$stderr)
            }
            $results.Add([pscustomobject]@{
                Name = $job.Task.Name
                ExitCode = $exitCode
                LogPath = $job.Task.LogPath
            })
        }
    }

    return $results.ToArray()
}
