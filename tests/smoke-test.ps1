param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exeName = (-join @(
    [char]0x684C,
    [char]0x9762,
    [char]0x5DE5,
    [char]0x5177
)) + '.exe'
$exe = Join-Path $root ('dist\' + $exeName)
$dataDir = Join-Path $env:APPDATA 'DeskToggle'
$settingsPath = Join-Path $dataDir 'settings.ini'
$markerPath = Join-Path $dataDir 'hidden.state'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'DeskToggle'

if ($Build) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw ('Executable not found: ' + $exe)
}

$exeSize = (Get-Item -LiteralPath $exe).Length
if ($exeSize -gt 200KB) {
    throw ('Executable is larger than expected: ' + $exeSize + ' bytes')
}

$running = Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($exe)) -ErrorAction SilentlyContinue
if ($null -ne $running) {
    throw 'DeskToggle is already running. Close it before running this test.'
}

if (-not ('DeskToggleSmokeNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class DeskToggleSmokeNative {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static IntPtr FindHiddenWindow(uint processId) {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == processId) {
                StringBuilder text = new StringBuilder(256);
                GetWindowText(hWnd, text, text.Capacity);
                if (text.ToString() == "DeskToggle.HiddenWindow") {
                    result = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static IntPtr FindAnyHiddenWindow() {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            StringBuilder text = new StringBuilder(256);
            GetWindowText(hWnd, text, text.Capacity);
            if (text.ToString() == "DeskToggle.HiddenWindow") {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
}

function Write-TestSettings([bool]$startWithWindows) {
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    $value = if ($startWithWindows) { '1' } else { '0' }
    $content = "HideTaskbar=0`r`nStartWithWindows=$value`r`nFirstRun=0`r`n"
    [System.IO.File]::WriteAllText($settingsPath, $content, (New-Object System.Text.UTF8Encoding($false)))
}

function Start-TestApp {
    $process = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            throw ('DeskToggle exited during startup (code ' + $process.ExitCode + ').')
        }
        $hidden = [DeskToggleSmokeNative]::FindHiddenWindow([uint32]$process.Id)
        if ($hidden -ne [IntPtr]::Zero) {
            return [pscustomobject]@{ Process = $process; HiddenWindow = $hidden }
        }
        Start-Sleep -Milliseconds 120
    }
    throw 'Timed out waiting for DeskToggle to initialize.'
}

function Get-StartupValue {
    $item = Get-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    $property = $item.PSObject.Properties[$runName]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Stop-TestApp($process) {
    $hidden = [DeskToggleSmokeNative]::FindHiddenWindow([uint32]$process.Id)
    if ($hidden -ne [IntPtr]::Zero) {
        [void][DeskToggleSmokeNative]::PostMessage($hidden, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    }

    $deadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            return
        }
        Start-Sleep -Milliseconds 120
    }

    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'DeskToggle did not exit cleanly after WM_CLOSE.'
}

$oldSettings = $null
$hadSettings = Test-Path -LiteralPath $settingsPath
if ($hadSettings) {
    $oldSettings = [System.IO.File]::ReadAllBytes($settingsPath)
}

$oldRunValue = Get-StartupValue
$hadRunValue = ($null -ne $oldRunValue)

$startedProcesses = @()
try {
    if (Test-Path -LiteralPath $markerPath) {
        throw 'A hidden.state marker exists. Run the app once or use --restore before testing.'
    }
    if ([DeskToggleSmokeNative]::FindAnyHiddenWindow() -ne [IntPtr]::Zero) {
        throw 'Another DeskToggle instance appears to be running.'
    }

    Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue

    Write-TestSettings $true
    $first = Start-TestApp
    $startedProcesses += $first.Process

    $registered = Get-StartupValue
    $expected = '"' + $exe + '"'
    if ($registered -ne $expected) {
        throw ('Startup registry value mismatch. Expected: ' + $expected + '  Actual: ' + $registered)
    }

    Stop-TestApp $first.Process

    Write-TestSettings $false
    $second = Start-TestApp
    $startedProcesses += $second.Process

    $registeredAfterDisable = Get-StartupValue
    if ($null -ne $registeredAfterDisable) {
        throw 'Startup registry value was not removed after disabling.'
    }

    Stop-TestApp $second.Process

    if (Test-Path -LiteralPath $markerPath) {
        throw 'hidden.state marker was left behind after a clean exit.'
    }

    Write-Host 'PASS: build artifact, single instance window, startup registry and clean exit.'
}
finally {
    foreach ($process in $startedProcesses) {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if ($hadSettings) {
        New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
        [System.IO.File]::WriteAllBytes($settingsPath, $oldSettings)
    } elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }

    if ($hadRunValue) {
        New-Item -Path $runKey -Force | Out-Null
        Set-ItemProperty -Path $runKey -Name $runName -Value $oldRunValue
    } else {
        Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $markerPath) {
        Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    }
}
