param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$ProductDir,
  [Parameter(Mandatory=$true)][string]$ProbeDir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50131
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @(
  $serverScript,
  '--port',$port,
  '--output',$OutputDir
) -PassThru -WindowStyle Hidden

try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)

  while([DateTime]::UtcNow -lt $deadline -and -not(Test-Path $ready) -and -not $server.HasExited){
    Start-Sleep -Milliseconds 200
  }

  if(-not(Test-Path $ready)){
    throw 'Fake VOICEVOX server did not start'
  }

  $productPluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssist'
  $probePluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssistA2NativeProbe'
  New-Item -ItemType Directory -Force $productPluginDir|Out-Null
  New-Item -ItemType Directory -Force $probePluginDir|Out-Null

  Copy-Item (Join-Path $ProductDir 'Ymm4VoiceQualityAssist.dll') $productPluginDir
  Copy-Item (Join-Path $ProbeDir 'Ymm4VoiceQualityAssistA2NativeProbe.dll') $probePluginDir

  $env:VQA_A2_NATIVE_OUTPUT=$OutputDir
  $env:VQA_A2_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (
    Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
  ) -WorkingDirectory $Ymm4Dir -PassThru

  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(180)

    while([DateTime]::UtcNow -lt $limit -and -not $process.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native A2 result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'a2-helper-observation.json'
    if(Test-Path $observation){
      Write-Output '--- A2 helper observation ---'
      Get-Content $observation
    }

    $requests=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requests){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requests
    }

    $rejected=(
      $r.schema -ne 'vqa.a2.helper-product-native-smoke.v1' -or
      $r.status -ne 'PASS_A2_HELPER_PRODUCT_NATIVE_SMOKE' -or
      $r.host -ne '4.56.1.0 Lite' -or
      $r.sourceHead -ne $env:GITHUB_SHA -or
      $null -ne $r.error
    )

    if($rejected){
      throw 'Native A2 result rejected'
    }

    $required=@(
      'timeline_resolved',
      'fake_engine_registered',
      'product_effect_type_discovered',
      'vowel_voice_added',
      'vowel_baseline_shape',
      'vowel_helpers_applied',
      'vowel_persisted_source_unchanged',
      'shifted_anchor_reapplied',
      'shifted_source_not_rewritten',
      'ambiguous_anchor_restores_baseline',
      'original_source_reapplies',
      'consonant_voice_added',
      'consonant_helper_applied',
      'consonant_helper_vowel_preserved',
      'consonant_persisted_source_unchanged',
      'combined_voice_added',
      'combined_a1_a2_applied',
      'combined_persisted_source_unchanged'
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

    Write-Output 'PASS_A2_HELPER_PRODUCT_NATIVE_SMOKE_E2E'
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

  Remove-Item Env:VQA_A2_NATIVE_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:VQA_A2_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
