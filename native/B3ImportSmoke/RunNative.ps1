param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$ProductDir,
  [Parameter(Mandatory=$true)][string]$ProbeDir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class VqaB3Win32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-VqaB3Windows {
  $script:rows=@()
  $callback=[VqaB3Win32+EnumWindowsProc]{
    param([IntPtr]$hWnd,[IntPtr]$lParam)
    if([VqaB3Win32]::IsWindowVisible($hWnd)){
      $sb=New-Object System.Text.StringBuilder 1024
      [void][VqaB3Win32]::GetWindowText($hWnd,$sb,$sb.Capacity)
      $title=$sb.ToString()
      if(-not [string]::IsNullOrWhiteSpace($title)){
        $script:rows += [pscustomobject]@{Handle=$hWnd;Title=$title}
      }
    }
    return $true
  }
  [void][VqaB3Win32]::EnumWindows($callback,[IntPtr]::Zero)
  return $script:rows
}

$port=50134
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @(
  $serverScript,
  '--port',$port,
  '--output',$OutputDir
) -PassThru -WindowStyle Hidden

try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)

  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }

  if(-not(Test-Path $ready)){
    throw 'Fake VOICEVOX server did not start'
  }

  $productPluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssist'
  $probePluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssistB3NativeProbe'

  New-Item -ItemType Directory -Force $productPluginDir|Out-Null
  New-Item -ItemType Directory -Force $probePluginDir|Out-Null

  Copy-Item (Join-Path $ProductDir 'Ymm4VoiceQualityAssist.dll') $productPluginDir
  Copy-Item (Join-Path $ProbeDir 'Ymm4VoiceQualityAssistB3NativeProbe.dll') $probePluginDir

  $env:VQA_B3_NATIVE_OUTPUT=$OutputDir
  $env:VQA_B3_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (
    Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
  ) -WorkingDirectory $Ymm4Dir -PassThru

  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(180)

    while([DateTime]::UtcNow-lt$limit -and -not$process.HasExited -and -not(Test-Path $result)){
      foreach($w in (Get-VqaB3Windows)){
        "$([DateTime]::UtcNow.ToString('O')) title=$($w.Title)" |
          Add-Content (Join-Path $OutputDir 'host-windows.txt')

        if($w.Title -like '*Check for updates*' -or
           $w.Title -like '*About YukkuriMovieMaker*'){
          [void][VqaB3Win32]::PostMessage(
            $w.Handle,
            0x0010,
            [IntPtr]::Zero,
            [IntPtr]::Zero
          )
        }
        elseif($w.Title -eq 'Confirm'){
          [void][VqaB3Win32]::PostMessage(
            $w.Handle,
            0x0100,
            [IntPtr]0x0D,
            [IntPtr]::Zero
          )
          [void][VqaB3Win32]::PostMessage(
            $w.Handle,
            0x0101,
            [IntPtr]0x0D,
            [IntPtr]::Zero
          )
        }
      }

      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native B3 result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $hostWindows=Join-Path $OutputDir 'host-windows.txt'
    if(Test-Path $hostWindows){
      Write-Output '--- host windows ---'
      Get-Content $hostWindows
    }

    $observation=Join-Path $OutputDir 'b3-import-observation.json'
    if(Test-Path $observation){
      Write-Output '--- B3 import observation ---'
      Get-Content $observation
    }

    $migrationObservation=Join-Path $OutputDir 'migration-observation.json'
    if(Test-Path $migrationObservation){
      Write-Output '--- legacy migration observation ---'
      Get-Content $migrationObservation
    }

    $typedUiObservation=Join-Path $OutputDir 'typed-settings-ui-observation.json'
    if(Test-Path $typedUiObservation){
      Write-Output '--- typed settings UI observation ---'
      Get-Content $typedUiObservation
    }

    $forcedBoundaryObservation=Join-Path $OutputDir 'forced-boundary-input-observation.json'
    if(Test-Path $forcedBoundaryObservation){
      Write-Output '--- forced boundary input observation ---'
      Get-Content $forcedBoundaryObservation
    }

    $forcedBoundarySaveObservation=Join-Path $OutputDir 'forced-boundary-save-observation.json'
    if(Test-Path $forcedBoundarySaveObservation){
      Write-Output '--- forced boundary save observation ---'
      Get-Content $forcedBoundarySaveObservation
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requestsPath
    }

    $rejected=(
      $r.schema-ne'vqa.b3.import-product-native-smoke.v1' -or
      $r.status-ne'PASS_B3_IMPORT_PRODUCT_NATIVE_SMOKE' -or
      $r.host-ne'4.56.1.0 Lite' -or
      $r.sourceHead-ne$env:GITHUB_SHA -or
      $null-ne$r.error
    )

    if($rejected){
      throw 'Native B3 result rejected'
    }

    $required=@(
      'timeline_resolved',
      'undo_manager_resolved',
      'fake_engine_registered',
      'selected_voice_added',
      'stale_voice_added',
      'baseline_files_created',
      'review_export_built',
      'correction_package_valid',
      'import_plan_built',
      'selected_exact_session',
      'changed_voice_is_stale',
      'stale_selection_rejected',
      'exact_selection_prepared',
      'journal_committed',
      'import_recorded_once',
      'durable_source_applied',
      'assist_effect_created',
      'assist_new_write_uses_audio_effects',
      'assist_new_write_skips_legacy_collection',
      'track_a_regenerated_corrected_audio',
      'stale_voice_not_applied',
      'undo_restores_durable_source',
      'undo_regenerates_baseline_audio',
      'redo_restores_durable_source',
      'redo_regenerates_same_corrected_audio',
      'migration_voice_added',
      'migration_legacy_seeded',
      'migration_prepared',
      'migration_committed_to_audio',
      'migration_settings_exact',
      'migration_recorded_once',
      'migration_undo_restores_legacy',
      'migration_redo_restores_audio',
      'typed_ui_voice_selected',
      'typed_ui_audio_effect_selected',
      'typed_ui_helper_editor_visible',
      'typed_ui_prosody_editor_visible',
      'typed_ui_raw_json_hidden',
      'typed_ui_initial_helper_rule_visible',
      'typed_ui_helper_edit_applied',
      'typed_ui_helper_undo',
      'typed_ui_helper_redo',
      'typed_ui_boundary_token_visible',
      'forced_boundary_input_voice_added',
      'forced_boundary_input_effect_added',
      'forced_boundary_token_not_automatic',
      'forced_boundary_normalization_prepared',
      'forced_boundary_normalization_committed',
      'forced_boundary_normalization_recorded_once',
      'forced_boundary_normalization_undo',
      'forced_boundary_normalization_redo',
      'forced_boundary_persistence_fixture_ready',
      'forced_boundary_persistence_saved'
    )

    if($r.requirements.Count-ne$required.Count){
      throw "Wrong requirement count: $($r.requirements.Count)"
    }

    foreach($id in $required){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){
        throw "Missing/failed requirement: $id"
      }
    }

    if(-not(Test-Path $requestsPath)){
      throw 'Fake server request log missing'
    }

    $requests=@(
      Get-Content $requestsPath|
        ForEach-Object { $_|ConvertFrom-Json }
    )

    $observed=@(
      $requests|Where-Object {
        $_.method-eq'OBSERVE' -and
        $_.path-eq'/synthesis-result'
      }
    )

    $corrected=@(
      $observed|Where-Object {
        $_.kind-eq'b3-corrected'
      }
    )

    $baseline=@(
      $observed|Where-Object {
        $_.kind-eq'baseline'
      }
    )

    if($corrected.Count-lt2){
      throw "Expected apply and redo corrected synthesis; got $($corrected.Count)"
    }

    if($baseline.Count-lt3){
      throw "Expected initial and undo baseline syntheses; got $($baseline.Count)"
    }

    foreach($entry in $corrected){
      if(
        $entry.pause_zero-cne$true -or
        $entry.helper_zero-cne$true -or
        $entry.hold-cne$true -or
        [int]$entry.wav_length-ne6644
      ){
        throw 'Corrected synthesis observation missing one or more A1/A2/A3 conditions'
      }
    }

    @{
      schema='vqa.b3.import-product-native-smoke-e2e.v1'
      synthesis_observations=$observed.Count
      corrected_count=$corrected.Count
      baseline_count=$baseline.Count
      final_corrected_wav_length=[int]$corrected[-1].wav_length
    }|ConvertTo-Json|Set-Content (
      Join-Path $OutputDir 'e2e.json'
    )

    $project=Join-Path $OutputDir 'phase4-boundary-persistence.ymmp'
    if(-not(Test-Path $project)){
      throw 'Phase 4 persistence project missing'
    }

    # End the full B3 host before proving real restart/reload persistence.
    if(-not$process.HasExited){
      Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
      Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    $reloadResult=Join-Path $OutputDir 'reload-result.json'
    Remove-Item $reloadResult -Force -ErrorAction SilentlyContinue

    $env:VQA_B3_RELOAD_PROJECT=$project

    $reloadProcess=Start-Process (
      Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
    ) -WorkingDirectory $Ymm4Dir -PassThru

    try {
      $reloadLimit=[DateTime]::UtcNow.AddSeconds(90)

      while(
        [DateTime]::UtcNow-lt$reloadLimit -and
        -not$reloadProcess.HasExited -and
        -not(Test-Path $reloadResult)
      ){
        foreach($w in (Get-VqaB3Windows)){
          "$([DateTime]::UtcNow.ToString('O')) reload-title=$($w.Title)" |
            Add-Content (Join-Path $OutputDir 'host-windows.txt')

          if($w.Title -like '*Check for updates*' -or
             $w.Title -like '*About YukkuriMovieMaker*'){
            [void][VqaB3Win32]::PostMessage(
              $w.Handle,
              0x0010,
              [IntPtr]::Zero,
              [IntPtr]::Zero
            )
          }
          elseif($w.Title -eq 'Confirm'){
            [void][VqaB3Win32]::PostMessage(
              $w.Handle,
              0x0100,
              [IntPtr]0x0D,
              [IntPtr]::Zero
            )
            [void][VqaB3Win32]::PostMessage(
              $w.Handle,
              0x0101,
              [IntPtr]0x0D,
              [IntPtr]::Zero
            )
          }
        }

        Start-Sleep -Milliseconds 350
      }

      if(-not(Test-Path $reloadResult)){
        throw 'No Phase 4 reload result'
      }

      $reload=Get-Content -Raw $reloadResult|ConvertFrom-Json

      Write-Output '--- forced boundary restart/reload result ---'
      Get-Content $reloadResult

      $reloadRejected=(
        $reload.schema-ne'vqa.b3.forced-boundary-reload.v1' -or
        $reload.status-ne'PASS_B3_FORCED_BOUNDARY_RELOAD' -or
        $reload.host-ne'4.56.1.0 Lite' -or
        $reload.sourceHead-ne$env:GITHUB_SHA -or
        $null-ne$reload.error -or
        $reload.serif-ne'え<w0>ええ' -or
        $reload.boundaryInputToken-ne'||' -or
        $reload.effectEnabled-cne$false -or
        $reload.markerPositions.Count-ne1 -or
        [int]$reload.markerPositions[0]-ne1
      )

      if($reloadRejected){
        throw 'Phase 4 restart/reload result rejected'
      }
    }
    finally {
      if(-not$reloadProcess.HasExited){
        Stop-Process -Id $reloadProcess.Id -Force -ErrorAction SilentlyContinue
      }

      Remove-Item Env:VQA_B3_RELOAD_PROJECT -ErrorAction SilentlyContinue
    }

    Write-Output 'PASS_B3_IMPORT_PRODUCT_NATIVE_SMOKE_E2E'
    Write-Output 'PASS_B3_FORCED_BOUNDARY_RESTART_RELOAD_E2E'
  }
  finally {
    if(-not$process.HasExited){
      Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
  }
}
finally {
  if(-not$server.HasExited){
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
  }

  Remove-Item Env:VQA_B3_NATIVE_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:VQA_B3_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
  Remove-Item Env:VQA_B3_RELOAD_PROJECT -ErrorAction SilentlyContinue
}
