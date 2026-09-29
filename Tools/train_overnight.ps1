# Unattended two-stage training run against the headless training builds.
#
#   powershell -ExecutionPolicy Bypass -File Tools\train_overnight.ps1 -Tag 0929 -Hours 8
#
# Stage 1 spars one learner against the scripted brain in Training1v1; stage 2 initialises
# from it and trains the ten-way free-for-all in Training10Way. Both players must be built
# first (Builds/Train1v1 and Builds/Train10Way, one scene each, Windows 64, Mono), and the
# Unity Editor must be closed so the arenas get the CPU. The script refuses to start on a
# build older than the code, or with this project open in the Editor.
#
# The shipped Assets/ML-Agents/Models/PoRumbleBoxer.onnx is never touched. Pick the
# replacement from the checkpoints on how often matches finish, not on reward - see
# DOCS/ML_AGENTS.md.
param(
    [string]$Tag = (Get-Date -Format "MMdd"),
    [double]$Hours = 8,
    # Training1v1 holds four arenas per player, so six players are 24 learners. It used to be
    # eight players of one learner each, and one-learner processes are bound on the Python
    # round trip, not the simulation: every decision was a batch of one.
    [int]$SparEnvs = 6,
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

# The run drives prebuilt players, so a code or prefab change made since the last build is
# silently not in the run at all - it trains yesterday's agent and reports nothing wrong.
function Assert-BuildsCurrent {
    $sources = @(Get-ChildItem -Recurse -File Assets\Scripts -Filter *.cs) +
        @(Get-Item Assets\Prefabs\Boxer.prefab, Assets\Scenes\Training1v1.unity, Assets\Scenes\Training10Way.unity)
    $newest = ($sources | Sort-Object LastWriteTime -Descending | Select-Object -First 1)

    foreach ($exe in @("Builds\Train1v1\PoRumbleTrain.exe", "Builds\Train10Way\PoRumbleTrain.exe")) {
        if (-not (Test-Path $exe)) {
            throw "$exe is missing. Build Training1v1 and Training10Way first (see DOCS/ML_AGENTS.md)."
        }

        # The Data folder, not the exe: Unity rewrites the managed assemblies on every build
        # but can leave an unchanged player exe with its old timestamp.
        $built = (Get-Item ($exe -replace 'PoRumbleTrain\.exe$', 'PoRumbleTrain_Data\Managed\PoRumble.Views.dll')).LastWriteTime
        if ($built -lt $newest.LastWriteTime) {
            throw "$exe was built $built, before $($newest.Name) changed at $($newest.LastWriteTime). Rebuild it."
        }
    }
}

# The standing rule for a run this long: the Editor is closed so the arenas get the CPU.
# Other projects' Editors are left alone; only one holding this project stops the run.
function Assert-EditorClosed {
    $project = (Get-Location).Path
    $editors = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$project*" }

    if ($editors) {
        throw "The Unity Editor has this project open (PID $($editors.ProcessId -join ', ')). Save and close it first."
    }
}

# A run already copied into _preserved is shown twice by TensorBoard, which scans all of
# results/. The top-level copy goes only when the preserved one holds every file it does.
function Remove-PreservedDuplicates {
    Get-ChildItem results -Directory | Where-Object { $_.Name -ne "_preserved" } | ForEach-Object {
        $kept = Join-Path "results\_preserved" $_.Name
        if (-not (Test-Path $kept)) {
            return
        }

        $here = @(Get-ChildItem -Recurse -File $_.FullName).Count
        $there = @(Get-ChildItem -Recurse -File $kept).Count
        if ($there -ge $here) {
            Remove-Item -Recurse -Force $_.FullName
            Write-Log "Pruned results\$($_.Name); it is preserved under results\_preserved."
        }
    }
}

Assert-EditorClosed
Assert-BuildsCurrent
Remove-PreservedDuplicates
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
