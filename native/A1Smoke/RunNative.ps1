param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$ProductDir,
  [Parameter(Mandatory=$true)][string]$ProbeDir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50130
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
  $probePluginDir=Join-Path $Ymm4Dir 'user/plugin/Ymm4VoiceQualityAssistNativeProbe'
  New-Item -ItemType Directory -Force $productPluginDir|Out-Null
  New-Item -ItemType Directory -Force $probePluginDir|Out-Null

  Copy-Item (Join-Path $ProductDir 'Ymm4VoiceQualityAssist.dll') $productPluginDir
  Copy-Item (Join-Path $ProbeDir 'Ymm4VoiceQualityAssistNativeProbe.dll') $probePluginDir

  $env:VQA_A1_NATIVE_OUTPUT=$OutputDir
  $env:VQA_A1_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (
    Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
  ) -WorkingDirectory $Ymm4Dir -PassThru

  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(150)

    while([DateTime]::UtcNow -lt $limit -and -not $process.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native product result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'observation.json'
    if(Test-Path $observation){
      Write-Output '--- product observation ---'
      Get-Content $observation
    }

    $requests=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requests){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requests
    }

    $resultRejected = (
      $r.schema -ne 'vqa.a1.product-native-smoke.v1' -or
      $r.status -ne 'PASS_A1_PRODUCT_NATIVE_SMOKE' -or
      $r.host -ne '4.56.1.0 Lite' -or
      $r.sourceHead -ne $env:GITHUB_SHA -or
      $null -ne $r.error
    )
    if($resultRejected){
      throw 'Native product result rejected'
    }

    $required=@(
      'timeline_resolved',
      'fake_engine_registered',
      'voice_added_to_timeline',
      'baseline_file_exists',
      'product_effect_type_discovered',
      'product_tool_registered',
      'product_runtime_plugin_registered',
      'baseline_wav_shape',
      'initial_correction_applied',
      'disable_restores_baseline',
      'reenable_restores_same_corrected_wav',
      'marker_removal_restores_baseline',
      'marker_restore_reapplies_correction',
      'incompatible_hatsuon_restores_baseline',
      'compatible_hatsuon_reapplies_same_corrected_wav'
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

    Write-Output 'PASS_A1_PRODUCT_NATIVE_SMOKE_E2E'
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

  Remove-Item Env:VQA_A1_NATIVE_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:VQA_A1_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
