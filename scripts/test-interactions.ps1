param([string]$ApplicationPath='release/ResumeStudio/ResumeStudio.exe',[string]$OutputDirectory='',[switch]$Smoke)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/interactions-'+[guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$savedData=$env:RESUMESTUDIO_TEST_DATA;$savedOutput=$env:RESUMESTUDIO_TEST_OUTPUT
try{
    $env:RESUMESTUDIO_TEST_DATA=Join-Path $OutputDirectory 'isolated-data';$env:RESUMESTUDIO_TEST_OUTPUT=$OutputDirectory
    $application=[IO.Path]::GetFullPath($(if([IO.Path]::IsPathRooted($ApplicationPath)){$ApplicationPath}else{Join-Path $root $ApplicationPath}))
    $testArgument=if($Smoke){'--interaction-smoke-test'}else{'--interaction-test'}
    $process=Start-Process -FilePath $application -ArgumentList $testArgument -WindowStyle Hidden -PassThru
    $result=$null;$resultPath=Join-Path $OutputDirectory 'interaction-result.json'
    for($i=0;$i -lt 180;$i++){
        if(Test-Path -LiteralPath $resultPath){try{$result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json;break}catch{}}
        if($process.HasExited){throw "Native interaction test exited with code $($process.ExitCode)"}
        Start-Sleep -Seconds 1
    }
    if(!$result){throw 'Native interaction test timed out'}
    if($result.status -ne 'passed'){Write-Output ($result|ConvertTo-Json -Depth 6);throw 'Native interaction verification failed'}
    Write-Output "PASS: $($result.checks.Count) native interaction checks. Evidence: $OutputDirectory"
}finally{
    if($process -and !$process.HasExited){$process.CloseMainWindow()|Out-Null}
    $env:RESUMESTUDIO_TEST_DATA=$savedData;$env:RESUMESTUDIO_TEST_OUTPUT=$savedOutput
}
