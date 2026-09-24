param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$ProductDir,
  [Parameter(Mandatory=$true)][string]$ProbeDir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

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
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native B3 result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'b3-import-observation.json'
    if(Test-Path $observation){
      Write-Output '--- B3 import observation ---'
      Get-Content $observation
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
      'redo_regenerates_same_corrected_audio'
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

    Write-Output 'PASS_B3_IMPORT_PRODUCT_NATIVE_SMOKE_E2E'
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
}
