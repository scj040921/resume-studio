param([string]$Device='emulator-5554',[ValidateSet('strong','none','reject-wildcard')][string]$ETagMode='strong')
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$sdk=$env:ANDROID_HOME;$tools=Join-Path $sdk 'build-tools\36.0.0';$platform=Join-Path $sdk 'platforms\android-35\android.jar';$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:\Program Files\Java\jdk-21'}
$build=Join-Path $root 'artifacts\android-ui-tests';New-Item -ItemType Directory -Force "$build\classes","$build\dex","$build\assets"|Out-Null
New-Item -ItemType Directory -Force "$build\results"|Out-Null;Remove-Item -LiteralPath "$build\results\result.json","$build\results\screen.png","$build\results\resume.pdf","$build\results\resume-pdf.png" -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath "$root\tests\android\acceptance.js" -Destination "$build\assets" -Force
& "$jdk\bin\javac.exe" -encoding UTF-8 --release 17 -classpath $platform -d "$build\classes" "$root\tests\android\AcceptanceInstrumentation.java";if($LASTEXITCODE){throw 'Instrumentation compile failed'}
& "$jdk\bin\jar.exe" --create --file "$build\classes.jar" -C "$build\classes" .
& "$jdk\bin\java.exe" -cp "$tools\lib\d8.jar" com.android.tools.r8.D8 --min-api 26 --lib $platform --output "$build\dex" "$build\classes.jar";if($LASTEXITCODE){throw 'Instrumentation dex failed'}
& "$tools\aapt2.exe" link -o "$build\unsigned.apk" -I $platform --manifest "$root\tests\android\AndroidManifest.xml" -A "$build\assets";if($LASTEXITCODE){throw 'Instrumentation package failed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem;$apk=[IO.Compression.ZipFile]::Open("$build\unsigned.apk",[IO.Compression.ZipArchiveMode]::Update);try{foreach($dex in Get-ChildItem "$build\dex" -Filter '*.dex'){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($apk,$dex.FullName,$dex.Name)|Out-Null}}finally{$apk.Dispose()}
& "$tools\zipalign.exe" -f 4 "$build\unsigned.apk" "$build\aligned.apk"
& "$jdk\bin\java.exe" -jar "$tools\lib\apksigner.jar" sign --ks "$env:USERPROFILE\.android\resume-studio-local.p12" --ks-key-alias resume-studio --ks-pass pass:android --out "$build\tests.apk" "$build\aligned.apk";if($LASTEXITCODE){throw 'Instrumentation signing failed'}
& adb -s $Device install -r "$build\tests.apk";if($LASTEXITCODE){throw 'Instrumentation install failed'}
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$probe.Start();$davPort=$probe.LocalEndpoint.Port;$probe.Stop()
$davRoot=Join-Path $build ('dav-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Force $davRoot|Out-Null
$davProcess=Start-Process -FilePath (Get-Command python).Source -ArgumentList @(('"'+"$root\tests\android\dav_fixture.py"+'"'),'--port',"$davPort",'--root',('"'+$davRoot+'"'),'--etag-mode',$ETagMode) -PassThru -WindowStyle Hidden -RedirectStandardOutput "$davRoot\server.txt" -RedirectStandardError "$davRoot\error.txt"
try{
    & adb -s $Device reverse "tcp:$davPort" "tcp:$davPort"
    & adb -s $Device shell am instrument -w -e davPort "$davPort" com.resumestudio.mobile.tests/com.resumestudio.AcceptanceInstrumentation | Tee-Object -FilePath "$build\instrumentation.txt"
    if((Get-Content "$build\instrumentation.txt" -Raw) -match 'Process crashed|INSTRUMENTATION_CODE: 1' -or (Get-Content "$build\instrumentation.txt" -Raw) -notmatch 'INSTRUMENTATION_CODE: 0'){throw 'Android instrumentation did not complete successfully'}

if((Get-Content "$build\instrumentation.txt" -Raw) -match 'INSTRUMENTATION_RESULT: error=|INSTRUMENTATION_CODE: 1|Process crashed'){throw 'Production Android instrumentation failed'}
New-Item -ItemType Directory -Force "$build\results"|Out-Null
foreach($artifact in @('result.json','screen.png','resume.pdf','resume-pdf.png')){
    $temporary=Join-Path $build ('pulled-'+$artifact)
    & adb -s $Device pull ("/sdcard/Android/data/com.resumestudio.mobile/files/acceptance/"+$artifact) $temporary
    if($LASTEXITCODE -ne 0){throw "Could not retrieve Android acceptance artifact: $artifact"}
    Move-Item -LiteralPath $temporary -Destination (Join-Path "$build\results" $artifact) -Force
}
if(!(Test-Path "$build\results\result.json")){throw 'No Android acceptance report'}
$report=Get-Content "$build\results\result.json" -Raw|ConvertFrom-Json
if($report -is [string]){$report=$report|ConvertFrom-Json}
if($report.autoSyncFixture){
    $fixture=Join-Path $davRoot 'resume-studio.sync.json'
    $before=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    $readsBefore=@(Select-String -LiteralPath "$davRoot\error.txt" -Pattern '"GET ').Count
    & adb -s $Device shell input keyevent KEYCODE_HOME
    & adb -s $Device shell cmd jobscheduler run -f com.resumestudio.mobile 903
    $autoPassed=$false
    for($attempt=0;$attempt -lt 30;$attempt++){
        $readsNow=@(Select-String -LiteralPath "$davRoot\error.txt" -Pattern '"GET ').Count
        if($readsNow -gt $readsBefore){$autoPassed=$true;break}
        Start-Sleep -Seconds 1
    }
    if(!$autoPassed){throw 'Android background check did not read the cloud snapshot'}
    Start-Sleep -Seconds 3
    if((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ne $before){throw 'Background check changed cloud data without confirmation'}
    $report.passed+=@('background job waits for confirmation and leaves cloud unchanged');$report.count++
    [IO.File]::WriteAllText("$build\results\result.json",($report|ConvertTo-Json -Depth 20 -Compress),[Text.UTF8Encoding]::new($false))
}
if($report.error){throw $report.error};if(!$report.count){throw 'No checks passed'}
Write-Output "PASS: $($report.count) production APK UI checks"
}finally{& adb -s $Device reverse --remove "tcp:$davPort";if(!$davProcess.HasExited){Stop-Process -Id $davProcess.Id}}
