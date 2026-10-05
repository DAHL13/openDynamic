#Requires -Version 5.1
<#
.SYNOPSIS
    Script de mediciÃ³n de rendimiento real en Release (ReadyToRun) para la auditorÃ­a v2.0.0 de openDynamic.
.DESCRIPTION
    Ejecuta las mediciones empÃ­ricas requeridas en la SecciÃ³n A5 de la auditorÃ­a v2.0.0:
    1. Arranque en frÃ­o (3 repeticiones) midiendo tiempo hasta ventana lista / servicios inicializados.
    2. Reposo (Idle) durante 5 minutos (muestreo cada 60s) + verificaciÃ³n de 0 sockets de red.
    3. ReproducciÃ³n de mÃºsica activa (GSMTC) + visualizador de espectro WASAPI durante 1 minuto.
    4. Widget de hardware visible (EnableHardwareMonitoring=true, EnableGpuMonitoring=true) durante 1 minuto.
    5. SesiÃ³n continua alternando estados (Oculto -> Compacto -> Expandido -> Volumen -> Oculto).
    Restaura Ã­ntegramente %AppData%\openDynamic\settings.json al finalizar.
#>
param(
    # Etiqueta que se guarda en el JSON de resultados (p. ej. "antes" / "despues")
    [string]$Label = "medicion"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishExe = Join-Path $RepoRoot "publish\OpenDynamic.App.exe"
$AppDataDir = Join-Path $env:APPDATA "openDynamic"
$SettingsPath = Join-Path $AppDataDir "settings.json"
$SettingsBackupPath = Join-Path $AppDataDir "settings.json.audit-backup"
$LocalLogDir = Join-Path $env:LOCALAPPDATA "openDynamic\logs"
$ResultsPath = Join-Path $PSScriptRoot ("resultados-rendimiento-v2-{0}.json" -f $Label)

if (-not (Test-Path $PublishExe)) {
    throw "No se encontrÃ³ el binario publicado en $PublishExe. Ejecuta dotnet publish primero."
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NativeTestHelper
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public const byte VK_VOLUME_UP = 0xAF;
    public const byte VK_VOLUME_DOWN = 0xAE;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public static void TapVolumeUp()
    {
        keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
        keybd_event(VK_VOLUME_UP, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void TapVolumeDown()
    {
        keybd_event(VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_VOLUME_DOWN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
"@

function Stop-OpenDynamic {
    Get-Process -Name "OpenDynamic.App" -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

function New-TestWavFile {
    param([string]$Path, [int]$DurationSeconds = 5)
    $sampleRate = 44100
    $channels = 1
    $bitsPerSample = 16
    $numSamples = $sampleRate * $DurationSeconds
    $dataSize = $numSamples * $channels * ($bitsPerSample / 8)
    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
        $bw.Write([int32](36 + $dataSize))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
        $bw.Write([int32]16)
        $bw.Write([int16]1)
        $bw.Write([int16]$channels)
        $bw.Write([int32]$sampleRate)
        $bw.Write([int32]($sampleRate * $channels * ($bitsPerSample / 8)))
        $bw.Write([int16]($channels * ($bitsPerSample / 8)))
        $bw.Write([int16]$bitsPerSample)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
        $bw.Write([int32]$dataSize)
        for ($i = 0; $i -lt $numSamples; $i++) {
            $t = [double]$i / $sampleRate
            $val = [Math]::Sin(2.0 * [Math]::PI * 440.0 * $t) * 800 + [Math]::Sin(2.0 * [Math]::PI * 880.0 * $t) * 600
            $bw.Write([int16]$val)
        }
    } finally {
        $bw.Close()
        $fs.Close()
    }
}

function Measure-ProcessSeries {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$Samples,
        [int]$IntervalSeconds,
        [string]$ScenarioName,
        [scriptblock]$OnEachInterval = $null
    )
    $logicalCores = [Environment]::ProcessorCount
    $prevCpu = $Process.TotalProcessorTime.TotalSeconds
    $rows = @()

    for ($i = 1; $i -le $Samples; $i++) {
        if ($null -ne $OnEachInterval) {
            & $OnEachInterval $i
        }
        Start-Sleep -Seconds $IntervalSeconds
        $Process.Refresh()
        $currCpu = $Process.TotalProcessorTime.TotalSeconds
        $cpuDelta = $currCpu - $prevCpu
        $cpuPercent = [math]::Round(($cpuDelta / ($IntervalSeconds * $logicalCores)) * 100, 3)
        $prevCpu = $currCpu

        $tcpConns = @(Get-NetTCPConnection -OwningProcess $Process.Id -ErrorAction SilentlyContinue).Count
        $udpEndpoints = @(Get-NetUDPEndpoint -OwningProcess $Process.Id -ErrorAction SilentlyContinue).Count

        $row = [PSCustomObject]@{
            Scenario    = $ScenarioName
            Sample      = $i
            Elapsed_s   = $i * $IntervalSeconds
            WS_MB       = [math]::Round($Process.WorkingSet64 / 1MB, 2)
            Priv_MB     = [math]::Round($Process.PrivateMemorySize64 / 1MB, 2)
            CPU_Total_s = [math]::Round($currCpu, 3)
            CPU_Avg_Pct = $cpuPercent
            Handles     = $Process.HandleCount
            Threads     = $Process.Threads.Count
            TCP_Conns   = $tcpConns
            UDP_Conns   = $udpEndpoints
        }
        $rows += $row
        Write-Host ("[{0}] Sample {1}/{2}: WS={3} MB | Priv={4} MB | CPU={5}% | Handles={6} | Threads={7} | TCP={8}" -f `
            $ScenarioName, $i, $Samples, $row.WS_MB, $row.Priv_MB, $row.CPU_Avg_Pct, $row.Handles, $row.Threads, $row.TCP_Conns)
    }
    return $rows
}

$hadSettings = Test-Path $SettingsPath
if ($hadSettings) {
    Copy-Item $SettingsPath $SettingsBackupPath -Force
}

$results = [ordered]@{
    Timestamp = (Get-Date).ToString("o")
    LogicalCores = [Environment]::ProcessorCount
    ColdStart = @()
    Idle5Min = @()
    ActiveMusicAndVisualizer = @()
    HardwareWidgetVisible = @()
    ContinuousSession = @()
}

try {
    Stop-OpenDynamic

    # -------------------------------------------------------------------------
    # 1. Arranque en frÃ­o (3 repeticiones)
    # -------------------------------------------------------------------------
    Write-Host "=== 1. MediciÃ³n de Arranque en FrÃ­o (3 repeticiones) ==="
    if (Test-Path $SettingsPath) { Remove-Item $SettingsPath -Force }

    for ($rep = 1; $rep -le 3; $rep++) {
        Stop-OpenDynamic
        $proc = Start-Process -FilePath $PublishExe -PassThru
        Start-Sleep -Seconds 4
        $proc.Refresh()

        $todayLog = Join-Path $LocalLogDir ("openDynamic-{0}.log" -f (Get-Date).ToString("yyyyMMdd"))
        $readyMsFromStart = $null
        $readyMsFromLogStart = $null
        if (Test-Path $todayLog) {
            $lines = Get-Content $todayLog -Tail 100
            $startLine = $lines | Where-Object { $_ -match "openDynamic logging initialized" } | Select-Object -Last 1
            $readyLine = $lines | Where-Object { $_ -match "App startup: performing initial ambient clock reveal|Global hotkey registered successfully" } | Select-Object -Last 1
            if ($startLine -match "^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})" -and $readyLine -match "^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})") {
                $tStartLog = [datetime]::ParseExact(($startLine -replace "^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*", '$1'), "yyyy-MM-dd HH:mm:ss.fff", $null)
                $tReadyLog = [datetime]::ParseExact(($readyLine -replace "^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*", '$1'), "yyyy-MM-dd HH:mm:ss.fff", $null)
                $readyMsFromLogStart = [math]::Round(($tReadyLog - $tStartLog).TotalMilliseconds, 1)
                $readyMsFromStart = [math]::Round(($tReadyLog - $proc.StartTime).TotalMilliseconds, 1)
            }
        }

        $csRow = [PSCustomObject]@{
            Repetition          = $rep
            ProcessToReady_ms   = $readyMsFromStart
            AppInitToReady_ms   = $readyMsFromLogStart
            Initial_WS_MB       = [math]::Round($proc.WorkingSet64 / 1MB, 2)
            Initial_Priv_MB     = [math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
            Initial_Handles     = $proc.HandleCount
            Initial_Threads     = $proc.Threads.Count
        }
        $results.ColdStart += $csRow
        Write-Host ("ColdStart #{0}: Process->Ready={1} ms | AppInit->Ready={2} ms | WS={3} MB | Priv={4} MB | Handles={5}" -f `
            $rep, $csRow.ProcessToReady_ms, $csRow.AppInitToReady_ms, $csRow.Initial_WS_MB, $csRow.Initial_Priv_MB, $csRow.Initial_Handles)
    }

    # -------------------------------------------------------------------------
    # 2. Reposo (Idle) durante 5 minutos (5 muestras x 60s)
    # -------------------------------------------------------------------------
    [NativeTestHelper]::SetCursorPos(200, 500)
    Start-Sleep -Seconds 3
    $proc = Get-Process -Name "OpenDynamic.App"
    Write-Host "=== 2. MediciÃ³n en Reposo (Idle) durante 5 minutos (muestreo cada 60s) ==="
    $results.Idle5Min = Measure-ProcessSeries -Process $proc -Samples 5 -IntervalSeconds 60 -ScenarioName "Idle_5min"

    # -------------------------------------------------------------------------
    # 3 y 4. MÃºsica activa (GSMTC) + Visualizador de espectro WASAPI (2 muestras x 30s)
    # -------------------------------------------------------------------------
    Write-Host "=== 3/4. MediciÃ³n con MÃºsica Activa (GSMTC) + Visualizador de Espectro WASAPI ==="
    $wavPath = Join-Path $env:TEMP "opendynamic_audit_tone.wav"
    New-TestWavFile -Path $wavPath -DurationSeconds 10

    $null = [Windows.Media.Playback.MediaPlayer, Windows.Media, ContentType = WindowsRuntime]
    $null = [Windows.Media.Core.MediaSource, Windows.Media, ContentType = WindowsRuntime]
    $null = [Windows.Media.SystemMediaTransportControls, Windows.Media, ContentType = WindowsRuntime]
    $null = [Windows.Media.MediaPlaybackType, Windows.Media, ContentType = WindowsRuntime]

    $mediaPlayer = [Windows.Media.Playback.MediaPlayer]::new()
    $mediaPlayer.IsLoopingEnabled = $true
    $mediaPlayer.CommandManager.IsEnabled = $true
    $smtc = $mediaPlayer.SystemMediaTransportControls
    $smtc.IsEnabled = $true
    $smtc.IsPlayEnabled = $true
    $smtc.IsPauseEnabled = $true
    $du = $smtc.DisplayUpdater
    $du.Type = [Windows.Media.MediaPlaybackType]::Music
    $du.MusicProperties.Title = "Audit Spectrum Track"
    $du.MusicProperties.Artist = "openDynamic Benchmark"
    $du.Update()
    $mediaPlayer.Source = [Windows.Media.Core.MediaSource]::CreateFromUri([uri]::new($wavPath))
    $mediaPlayer.Play()

    $soundPlayer = New-Object System.Media.SoundPlayer($wavPath)
    $soundPlayer.PlayLooping()
    Start-Sleep -Seconds 3
    try {
        $results.ActiveMusicAndVisualizer = Measure-ProcessSeries -Process $proc -Samples 2 -IntervalSeconds 30 -ScenarioName "Music_And_Visualizer" -OnEachInterval {
            param($idx)
            if ($idx -eq 1) {
                [NativeTestHelper]::TapVolumeUp()
            } else {
                # Expandir cÃ¡psula en la segunda muestra para medir vista expandida + espectro de 24 barras
                [NativeTestHelper]::SetCursorPos(960, 12)
            }
        }
    } finally {
        [NativeTestHelper]::SetCursorPos(200, 500)
        $soundPlayer.Stop()
        $soundPlayer.Dispose()
        $mediaPlayer.Pause()
        $mediaPlayer.Dispose()
        Remove-Item $wavPath -Force -ErrorAction SilentlyContinue
    }

    # -------------------------------------------------------------------------
    # 5. Widget de Hardware visible (2 muestras x 30s)
    # -------------------------------------------------------------------------
    Write-Host "=== 5. MediciÃ³n con Widget de Hardware Activo y Visible ==="
    Stop-OpenDynamic
    New-Item -ItemType Directory -Path $AppDataDir -Force | Out-Null
    $hwSettings = @{
        SchemaVersion = 14
        EnableHardwareMonitoring = $true
        EnableGpuMonitoring = $true
        HardwareSamplingIntervalSeconds = 2.0
        EnableAmbientClock = $true
    } | ConvertTo-Json
    Set-Content -Path $SettingsPath -Value $hwSettings -Encoding UTF8

    $proc = Start-Process -FilePath $PublishExe -PassThru
    Start-Sleep -Seconds 5
    $proc.Refresh()
    $results.HardwareWidgetVisible = Measure-ProcessSeries -Process $proc -Samples 2 -IntervalSeconds 30 -ScenarioName "Hardware_Widget" -OnEachInterval {
        param($idx)
        if ($idx -eq 2) {
            [NativeTestHelper]::SetCursorPos(960, 12)
        }
    }
    [NativeTestHelper]::SetCursorPos(200, 500)

    # -------------------------------------------------------------------------
    # 6. SesiÃ³n continua alternando estados (Oculto -> Compacto -> Expandido -> Volumen -> Oculto)
    # -------------------------------------------------------------------------
    Write-Host "=== 6. MediciÃ³n de SesiÃ³n Continua Alternando Estados (4 muestras x 30s = 120s intensivos) ==="
    $results.ContinuousSession = Measure-ProcessSeries -Process $proc -Samples 4 -IntervalSeconds 30 -ScenarioName "Continuous_MultiState" -OnEachInterval {
        param($idx)
        [NativeTestHelper]::SetCursorPos(960, 8)
        Start-Sleep -Seconds 2
        [NativeTestHelper]::TapVolumeUp()
        Start-Sleep -Seconds 1
        [NativeTestHelper]::TapVolumeDown()
        Start-Sleep -Seconds 2
        [NativeTestHelper]::SetCursorPos(200, 500)
    }

    Stop-OpenDynamic
    $results | ConvertTo-Json -Depth 6 | Set-Content -Path $ResultsPath -Encoding UTF8
    Write-Host "Mediciones completadas y guardadas en $ResultsPath"
}
finally {
    Stop-OpenDynamic
    if ($hadSettings -and (Test-Path $SettingsBackupPath)) {
        Copy-Item $SettingsBackupPath $SettingsPath -Force
        Remove-Item $SettingsBackupPath -Force -ErrorAction SilentlyContinue
    } elseif (-not $hadSettings -and (Test-Path $SettingsPath)) {
        Remove-Item $SettingsPath -Force -ErrorAction SilentlyContinue
    }
}
