param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$ProductDir,
  [Parameter(Mandatory=$true)][string]$ProbeDir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50133
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
  $probePluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssistA3NativeProbe'
  New-Item -ItemType Directory -Force $productPluginDir|Out-Null
  New-Item -ItemType Directory -Force $probePluginDir|Out-Null

  Copy-Item (Join-Path $ProductDir 'Ymm4VoiceQualityAssist.dll') $productPluginDir
  Copy-Item (Join-Path $ProbeDir 'Ymm4VoiceQualityAssistA3NativeProbe.dll') $probePluginDir

  $env:VQA_A3_NATIVE_OUTPUT=$OutputDir
  $env:VQA_A3_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (
    Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
  ) -WorkingDirectory $Ymm4Dir -PassThru

  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(150)

    while([DateTime]::UtcNow-lt$limit -and -not$process.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native A3 result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'a3-prosody-observation.json'
    if(Test-Path $observation){
      Write-Output '--- A3 prosody observation ---'
      Get-Content $observation
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requestsPath
    }

    $rejected=(
      $r.schema-ne'vqa.a3.prosody-product-native-smoke.v1' -or
      $r.status-ne'PASS_A3_PROSODY_PRODUCT_NATIVE_SMOKE' -or
      $r.host-ne'4.56.1.0 Lite' -or
      $r.sourceHead-ne$env:GITHUB_SHA -or
      $null-ne$r.error
    )

    if($rejected){
      throw 'Native A3 result rejected'
    }

    $required=@(
      'timeline_resolved',
      'fake_engine_registered',
      'product_effect_type_discovered',
      'prosody_voice_added',
      'baseline_shape',
      'light_rise_applied',
      'source_unchanged_after_rise',
      'light_fall_applied',
      'none_restores_baseline',
      'hold_applied',
      'disable_restores_baseline',
      'reenable_reapplies_hold',
      'persisted_source_unchanged',
      'prosody_setting_stays_hold',
      'reload_voice_added',
      'reload_initial_hold_applied',
      'reload_project_a_saved',
      'reload_source_survives',
      'reload_prosody_exact',
      'reload_fixture_speaker_rebound',
      'reload_hold_reapplied'
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

    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $observed=@($requests|Where-Object {
      $_.method-eq'OBSERVE' -and $_.path-eq'/synthesis-result'
    })

    foreach($kind in @('light-rise','light-fall','hold')){
      if(@($observed|Where-Object {$_.kind-eq$kind}).Count-lt1){
        throw "No $kind synthesis observed"
      }
    }

    if(@($observed|Where-Object {$_.kind-eq'baseline'}).Count-lt2){
      throw 'Expected baseline restore syntheses were not observed'
    }

    @{
      schema='vqa.a3.prosody-product-native-smoke-e2e.v1'
      synthesis_observations=$observed.Count
      light_rise_count=@($observed|Where-Object {$_.kind-eq'light-rise'}).Count
      light_fall_count=@($observed|Where-Object {$_.kind-eq'light-fall'}).Count
      hold_count=@($observed|Where-Object {$_.kind-eq'hold'}).Count
      baseline_count=@($observed|Where-Object {$_.kind-eq'baseline'}).Count
    }|ConvertTo-Json|Set-Content (
      Join-Path $OutputDir 'e2e.json'
    )

    Write-Output 'PASS_A3_PROSODY_PRODUCT_NATIVE_SMOKE_E2E'
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

  Remove-Item Env:VQA_A3_NATIVE_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:VQA_A3_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
