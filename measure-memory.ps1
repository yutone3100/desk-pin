param(
    [string]$ExePath = "$PSScriptRoot\artifacts\publish\DeskPin.exe",
    [ValidateRange(30, 3600)]
    [int]$DurationSeconds = 600,
    [ValidateRange(1, 60)]
    [int]$SampleIntervalSeconds = 5,
    [ValidateRange(30, 300)]
    [int]$WarmupSeconds = 60,
    [ValidateRange(1, 1024)]
    [int]$MaxPrivateWorkingSetMiB = 80,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$script:lastPrivateWorkingSetBytes = $null
$existingIds = @(Get-Process -Name DeskPin -ErrorAction SilentlyContinue |
    Where-Object { $_.HandleCount -gt 0 -or -not [string]::IsNullOrWhiteSpace($_.Path) } |
    ForEach-Object Id)
if ($existingIds.Count -gt 0) {
    throw 'DeskPin is already running. Exit it from the tray before measuring.'
}

function Get-PrivateWorkingSetBytes([int]$ProcessId) {
    $lastError = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $idCounter = Get-Counter '\Process(DeskPin*)\ID Process'
            $idSample = $idCounter.CounterSamples |
                Where-Object { [int64]$_.CookedValue -eq $ProcessId -and $_.Status -eq 0 } |
                Select-Object -First 1
            if ($null -eq $idSample) {
                throw 'The DeskPin performance counter instance was not found.'
            }

            $instanceMatch = [regex]::Match(
                $idSample.Path,
                '\\process\((?<instance>[^)]+)\)\\id process$',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            $counterInstance = $instanceMatch.Groups['instance'].Value
            if ([string]::IsNullOrWhiteSpace($counterInstance)) {
                throw 'The DeskPin performance counter instance path could not be parsed.'
            }

            $workingSetCounter = Get-Counter "\Process($counterInstance)\Working Set - Private"
            $workingSetSample = $workingSetCounter.CounterSamples | Select-Object -First 1
            if ($null -eq $workingSetSample -or $workingSetSample.Status -ne 0) {
                throw 'The DeskPin Private Working Set sample is invalid.'
            }

            $script:lastPrivateWorkingSetBytes = [int64]$workingSetSample.CookedValue
            return $script:lastPrivateWorkingSetBytes
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds 200
        }
    }

    if ($null -ne $script:lastPrivateWorkingSetBytes) {
        Write-Warning "Private Working Set is temporarily unavailable; reusing the previous valid sample: $($lastError.Exception.Message)"
        return $script:lastPrivateWorkingSetBytes
    }

    Write-Warning "Private Working Set is unavailable; using total Working Set: $($lastError.Exception.Message)"
    return (Get-Process -Id $ProcessId).WorkingSet64
}

$launcher = Start-Process -FilePath $resolvedExe -ArgumentList '--background' -WindowStyle Hidden -PassThru
$process = $null
$samples = [System.Collections.Generic.List[object]]::new()

try {
    Start-Sleep -Seconds 3
    $newProcesses = @(Get-Process -Name DeskPin -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $existingIds -and $_.WorkingSet64 -gt 1MB })
    $process = if ($newProcesses.Count -gt 0) {
        $newProcesses | Sort-Object WorkingSet64 | Select-Object -Last 1
    }
    elseif (-not $launcher.HasExited) {
        $launcher
    }
    else {
        throw 'DeskPin exited before measurement started.'
    }

    $startedAt = Get-Date
    $previousCpu = $process.TotalProcessorTime.TotalSeconds
    $previousSampleAt = $startedAt
    while ($true) {
        $now = Get-Date
        $elapsed = ($now - $startedAt).TotalSeconds
        if ($samples.Count -gt 0 -and $elapsed -ge $DurationSeconds) {
            break
        }

        $currentProcess = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($null -eq $currentProcess) {
            $currentProcess = Get-Process -Name DeskPin -ErrorAction SilentlyContinue |
                Where-Object { $_.Id -notin $existingIds -and $_.WorkingSet64 -gt 1MB } |
                Sort-Object WorkingSet64 |
                Select-Object -Last 1
        }

        if ($null -eq $currentProcess) {
            throw 'DeskPin exited before measurement completed.'
        }

        $process = $currentProcess
        $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
        $sampleElapsed = [math]::Max(($now - $previousSampleAt).TotalSeconds, 0.001)
        $cpuPercent = (($cpuSeconds - $previousCpu) / $sampleElapsed / [Environment]::ProcessorCount) * 100
        $privateWorkingSet = Get-PrivateWorkingSetBytes $process.Id
        $samples.Add([pscustomobject]@{
            Seconds = [math]::Round($elapsed, 1)
            PrivateWorkingSetMiB = [math]::Round($privateWorkingSet / 1MB, 2)
            WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateBytesMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            Handles = $process.HandleCount
            Threads = $process.Threads.Count
            CpuPercent = [math]::Round($cpuPercent, 3)
        })
        $previousCpu = $cpuSeconds
        $previousSampleAt = $now

        $sleepSeconds = [math]::Min($SampleIntervalSeconds, $DurationSeconds - $elapsed)
        if ($sleepSeconds -gt 0) {
            Start-Sleep -Milliseconds ([int]($sleepSeconds * 1000))
        }
    }
}
finally {
    if ($null -ne $process) {
        $remaining = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($null -ne $remaining -and $remaining.Path -eq $resolvedExe) {
            Stop-Process -Id $remaining.Id -ErrorAction Stop
        }
    }
}

$samples | Format-Table -AutoSize
if ($OutputPath) {
    $samples | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
}

$steadySamples = @($samples | Where-Object Seconds -ge $WarmupSeconds)
if ($steadySamples.Count -eq 0) {
    throw "Duration must be greater than or equal to the $WarmupSeconds second warmup."
}

$firstSteady = $steadySamples[0]
$lastSteady = $steadySamples[-1]
$averageCpu = ($steadySamples | Measure-Object CpuPercent -Average).Average
$summary = [pscustomobject]@{
    FinalPrivateWorkingSetMiB = $lastSteady.PrivateWorkingSetMiB
    PrivateBytesGrowthMiB = [math]::Round($lastSteady.PrivateBytesMiB - $firstSteady.PrivateBytesMiB, 2)
    HandleGrowth = $lastSteady.Handles - $firstSteady.Handles
    AverageIdleCpuPercent = [math]::Round($averageCpu, 3)
}
$summary | Format-List

if ($lastSteady.PrivateWorkingSetMiB -gt $MaxPrivateWorkingSetMiB) {
    throw "Idle Private Working Set is $($lastSteady.PrivateWorkingSetMiB) MiB; limit is $MaxPrivateWorkingSetMiB MiB."
}

if ($DurationSeconds -ge 600) {
    if ($summary.PrivateBytesGrowthMiB -gt 5) {
        throw "Private Bytes grew by more than 5 MiB: $($summary.PrivateBytesGrowthMiB) MiB."
    }

    if ($summary.HandleGrowth -gt 10) {
        throw "Handle count grew by more than 10: $($summary.HandleGrowth)."
    }

    if ($summary.AverageIdleCpuPercent -gt 0.2) {
        throw "Average idle CPU exceeded 0.2%: $($summary.AverageIdleCpuPercent)%."
    }
}
