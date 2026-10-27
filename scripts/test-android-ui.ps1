param([string]$Device='emulator-5554',[ValidateSet('strong','none','reject-wildcard')][string]$ETagMode='strong',[string]$ApplicationPath='release\ResumeStudio-Android-v0.9.5-local.apk',[switch]$OnlineUpdate,[switch]$NotificationChecks,[switch]$AllowTestDowngrade,[switch]$AnimationOnly)
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$sdk=$env:ANDROID_HOME;$tools=Join-Path $sdk 'build-tools\36.0.0';$platform=Join-Path $sdk 'platforms\android-35\android.jar';$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:\Program Files\Java\jdk-21'}
$build=Join-Path $root 'artifacts\android-ui-tests';New-Item -ItemType Directory -Force "$build\classes","$build\dex","$build\assets"|Out-Null
New-Item -ItemType Directory -Force "$build\results"|Out-Null;Remove-Item -LiteralPath "$build\results\result.json","$build\results\screen.png","$build\results\resume.pdf","$build\results\resume-pdf.png" -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath "$root\tests\android\acceptance.js" -Destination "$build\assets" -Force
Copy-Item -LiteralPath "$root\tests\android\motion-acceptance.js" -Destination "$build\assets" -Force
Copy-Item -LiteralPath "$root\tests\android\fixtures\stable-channel.json" -Destination "$build\assets" -Force
if($env:RESUMESTUDIO_EXAM_DOCUMENT){Copy-Item -LiteralPath $env:RESUMESTUDIO_EXAM_DOCUMENT -Destination "$build\assets\private-exam.docx" -Force}else{Remove-Item -LiteralPath "$build\assets\private-exam.docx" -Force -ErrorAction SilentlyContinue}
& "$jdk\bin\javac.exe" -encoding UTF-8 --release 17 -classpath $platform -d "$build\classes" "$root\tests\android\AcceptanceInstrumentation.java";if($LASTEXITCODE){throw 'Instrumentation compile failed'}
& "$jdk\bin\jar.exe" --create --file "$build\classes.jar" -C "$build\classes" .
& "$jdk\bin\java.exe" -cp "$tools\lib\d8.jar" com.android.tools.r8.D8 --min-api 26 --lib $platform --output "$build\dex" "$build\classes.jar";if($LASTEXITCODE){throw 'Instrumentation dex failed'}
& "$tools\aapt2.exe" link -o "$build\unsigned.apk" -I $platform --manifest "$root\tests\android\AndroidManifest.xml" -A "$build\assets";if($LASTEXITCODE){throw 'Instrumentation package failed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem;$apk=[IO.Compression.ZipFile]::Open("$build\unsigned.apk",[IO.Compression.ZipArchiveMode]::Update);try{foreach($dex in Get-ChildItem "$build\dex" -Filter '*.dex'){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($apk,$dex.FullName,$dex.Name)|Out-Null}}finally{$apk.Dispose()}
& "$tools\zipalign.exe" -f 4 "$build\unsigned.apk" "$build\aligned.apk"
& "$jdk\bin\java.exe" -jar "$tools\lib\apksigner.jar" sign --ks "$env:USERPROFILE\.android\resume-studio-local.p12" --ks-key-alias resume-studio --ks-pass pass:android --out "$build\tests.apk" "$build\aligned.apk";if($LASTEXITCODE){throw 'Instrumentation signing failed'}
$productionApk=Join-Path $root $ApplicationPath
if($AllowTestDowngrade){& adb -s $Device install -r -d $productionApk}else{& adb -s $Device install -r $productionApk};if($LASTEXITCODE){throw 'Production Android APK install failed'}
& adb -s $Device install -r "$build\tests.apk";if($LASTEXITCODE){throw 'Instrumentation install failed'}
if($NotificationChecks -and [int](& adb -s $Device shell getprop ro.build.version.sdk) -ge 33){& adb -s $Device shell pm grant com.resumestudio.mobile android.permission.POST_NOTIFICATIONS;if($LASTEXITCODE){throw 'Could not grant the requested notification test permission'}}
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$probe.Start();$davPort=$probe.LocalEndpoint.Port;$probe.Stop()
$davRoot=Join-Path $build ('dav-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Force $davRoot|Out-Null
$davProcess=Start-Process -FilePath (Get-Command python).Source -ArgumentList @(('"'+"$root\tests\android\dav_fixture.py"+'"'),'--port',"$davPort",'--root',('"'+$davRoot+'"'),'--etag-mode',$ETagMode) -PassThru -WindowStyle Hidden -RedirectStandardOutput "$davRoot\server.txt" -RedirectStandardError "$davRoot\error.txt"
try{
    & adb -s $Device reverse "tcp:$davPort" "tcp:$davPort"
    & adb -s $Device shell am instrument -w -e davPort "$davPort" -e onlineUpdate "$($OnlineUpdate.IsPresent.ToString().ToLowerInvariant())" -e notificationChecks "$($NotificationChecks.IsPresent.ToString().ToLowerInvariant())" -e animationOnly "$($AnimationOnly.IsPresent.ToString().ToLowerInvariant())" com.resumestudio.mobile.tests/com.resumestudio.AcceptanceInstrumentation | Tee-Object -FilePath "$build\instrumentation.txt"
    if((Get-Content "$build\instrumentation.txt" -Raw) -match 'Process crashed|INSTRUMENTATION_CODE: 1' -or (Get-Content "$build\instrumentation.txt" -Raw) -notmatch 'INSTRUMENTATION_CODE: 0'){throw 'Android instrumentation did not complete successfully'}
if((Get-Content "$build\instrumentation.txt" -Raw) -match 'INSTRUMENTATION_RESULT: error=|INSTRUMENTATION_CODE: 1|Process crashed'){throw 'Production Android instrumentation failed'}
New-Item -ItemType Directory -Force "$build\results"|Out-Null
$reportFile=if($AnimationOnly){'motion-result.json'}else{'result.json'}
$artifacts=if($AnimationOnly){@('motion-result.json','motion-final.png')}else{@('result.json','screen.png','dark-settings.png','light-settings.png','bottom-navigation.png','browser-safe-area.png','resume.pdf','resume-pdf.png')}
foreach($artifact in $artifacts){
    $temporary=Join-Path $build ('pulled-'+$artifact)
    & adb -s $Device pull ("/sdcard/Android/data/com.resumestudio.mobile/files/acceptance/"+$artifact) $temporary
    if($LASTEXITCODE -ne 0){throw "Could not retrieve Android acceptance artifact: $artifact"}
    Move-Item -LiteralPath $temporary -Destination (Join-Path "$build\results" $artifact) -Force
}
if(!(Test-Path "$build\results\$reportFile")){throw 'No Android acceptance report'}
$report=Get-Content "$build\results\$reportFile" -Raw|ConvertFrom-Json
if($report -is [string]){$report=$report|ConvertFrom-Json}
if($report.autoSyncFixture){
    & adb -s $Device shell input keyevent KEYCODE_HOME
    & adb -s $Device shell cmd jobscheduler run -f com.resumestudio.mobile 903
    $fixture=Join-Path $davRoot 'resume-studio.sync.json';$autoPassed=$false
    for($attempt=0;$attempt -lt 60;$attempt++){
        if(Test-Path -LiteralPath $fixture){try{$envelope=Get-Content -LiteralPath $fixture -Raw|ConvertFrom-Json;$autoPassed=@($envelope.data.resumes|Where-Object {$_.title -eq '后台自动同步验收'}).Count -ge 1 -and @($envelope.extensions.interviewStudy.decks).Count -gt 0;if($autoPassed){break}}catch{}}
        Start-Sleep -Seconds 1
    }
    if(!$autoPassed){throw 'Android background periodic sync did not upload the resume and interview deck'}
    $report.passed+=@('background network job syncs resume and interview deck');$report.count++
    [IO.File]::WriteAllText("$build\results\result.json",($report|ConvertTo-Json -Depth 20 -Compress),[Text.UTF8Encoding]::new($false))
}
if($report.error){throw $report.error};if(!$report.count){throw 'No checks passed'}
Write-Output "PASS: $($report.count) production APK UI checks"
}finally{& adb -s $Device reverse --remove "tcp:$davPort";if(!$davProcess.HasExited){Stop-Process -Id $davProcess.Id}}
