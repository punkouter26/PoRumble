# Unattended two-stage training run against the headless training builds.
#
#   powershell -ExecutionPolicy Bypass -File Tools\train_overnight.ps1 -Tag 0929 -Hours 8
#
# Stage 1 spars one learner against the scripted brain in Training1v1; stage 2 initialises
# from it and trains the ten-way free-for-all in Training10Way. Both players must be built
# first (Builds/Train1v1 and Builds/Train10Way, one scene each, Windows 64, Mono), and the
# Unity Editor should be closed so the arenas get the CPU.
#
# The shipped Assets/ML-Agents/Models/PoRumbleBoxer.onnx is never touched. Pick the
# replacement from the checkpoints on how often matches finish, not on reward - see
# DOCS/ML_AGENTS.md.
param(
    [string]$Tag = (Get-Date -Format "MMdd"),
    [double]$Hours = 8,
    [int]$SparEnvs = 8,
    [int]$FfaEnvs = 6,
    [int]$TimeScale = 20
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$learn = ".venv\Scripts\mlagents-learn.exe"
$deadline = (Get-Date).AddHours($Hours)
$sparId = "pr_spar_$Tag"
$ffaId = "pr_ffa_$Tag"
New-Item -ItemType Directory -Force results | Out-Null
$log = "results\overnight_$Tag.log"

function Write-Log([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}" -f (Get-Date), $message
    Add-Content -Path $log -Value $line
    Write-Host $line
}

function Assert-TensorBoard {
    if (Test-NetConnection -ComputerName localhost -Port 6006 -InformationLevel Quiet -WarningAction SilentlyContinue) {
        return
    }

    Start-Process -WindowStyle Hidden .venv\Scripts\tensorboard.exe -ArgumentList "--logdir results --port 6006"
    Start-Sleep 15

    if (-not (Test-NetConnection -ComputerName localhost -Port 6006 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
        throw "TensorBoard did not come up on port 6006; not starting a run without it."
    }
}

# Runs one stage and returns true when it finished on its own. Killed at the deadline
# otherwise - the periodic checkpoints (each exported as .onnx) are what survive that.
function Invoke-Stage([string]$name, [string[]]$arguments) {
    Write-Log "$name starting: $($arguments -join ' ')"
    $out = "results\$name.stdout.log"
    $proc = Start-Process -FilePath $learn -ArgumentList $arguments -RedirectStandardOutput $out `
        -RedirectStandardError "$out.err" -WindowStyle Hidden -PassThru

    # Touching the handle now keeps it open; without this, a process started with redirected
    # output reports an empty ExitCode once it has gone, and a clean finish reads as a failure.
    $null = $proc.Handle

    while (-not $proc.HasExited) {
        if ((Get-Date) -gt $deadline) {
            Write-Log "$name hit the deadline; stopping it. The last checkpoint stands."
            # The whole tree: the trainer's per-arena worker processes outlive a plain kill.
            & taskkill.exe /T /F /PID $proc.Id | Out-Null
            Get-Process PoRumbleTrain -ErrorAction SilentlyContinue | Stop-Process -Force
            return $false
        }

        Start-Sleep 30
    }

    Write-Log "$name exited with code $($proc.ExitCode)"
    return $proc.ExitCode -eq 0
}

Assert-TensorBoard
Write-Log "Overnight run $Tag, deadline $deadline"

$common = @("--no-graphics", "--time-scale=$TimeScale", "--base-port=5105")

$sparDone = Invoke-Stage $sparId (@(
    "Assets/Config/Training/porumble_1v1_spar.yaml", "--run-id=$sparId",
    "--env=Builds/Train1v1/PoRumbleTrain.exe", "--num-envs=$SparEnvs") + $common)

if (-not (Test-Path "results\$sparId\PoRumbleBoxer")) {
    Write-Log "Stage 1 left no checkpoint; stage 2 not started."
    exit 1
}

if (-not $sparDone) {
    Write-Log "Stage 1 did not finish cleanly; stage 2 starts from its last checkpoint."
}

Invoke-Stage $ffaId (@(
    "Assets/Config/Training/porumble_10way_ffa.yaml", "--run-id=$ffaId", "--initialize-from=$sparId",
    "--env=Builds/Train10Way/PoRumbleTrain.exe", "--num-envs=$FfaEnvs") + $common) | Out-Null

New-Item -ItemType Directory -Force results\_preserved | Out-Null
foreach ($run in @($sparId, $ffaId)) {
    if (Test-Path "results\$run") {
        Copy-Item -Recurse -Force "results\$run" "results\_preserved\$run"
    }
}

Write-Log "Done. Runs preserved under results\_preserved. The Unity Editor can be reopened."
