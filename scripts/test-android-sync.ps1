$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:\Program Files\Java\jdk-21'}
$build=Join-Path $root 'artifacts\android-core-tests';New-Item -ItemType Directory -Force "$build\classes","$build\desktop"|Out-Null
if(!(Test-Path "$root\artifacts\android-build\generated")){throw 'Run build-android.ps1 first'}
$sources=@(Get-ChildItem "$root\artifacts\android-build\generated" -Filter '*.java' -Recurse|ForEach-Object FullName)+@('MobileCompat','MobileHttp','MobileApi','MobileSync'|ForEach-Object {"$root\client-android\src\com\resumestudio\$_.java"})+@("$root\tests\android\MobileInterop.java")
& "$jdk\bin\javac.exe" -encoding UTF-8 --release 17 -d "$build\classes" @sources;if($LASTEXITCODE){throw 'Android sync core compile failed'}
$desktop=@(Get-ChildItem "$root\backend\src\main\java" -Filter '*.java' -Recurse|ForEach-Object FullName)
& "$jdk\bin\javac.exe" -encoding UTF-8 --release 21 --add-modules jdk.httpserver -d "$build\desktop" @desktop;if($LASTEXITCODE){throw 'Desktop sync backend compile failed'}
$dotnet=Join-Path $env:USERPROFILE '.dotnet\dotnet.exe';if(!(Test-Path $dotnet)){$dotnet=(Get-Command dotnet).Source}
& $dotnet run --project "$root\tests\AndroidSync.Tests\AndroidSync.Tests.csproj" -c Release -- $root
if($LASTEXITCODE){throw 'Android / Windows sync interoperability failed'}
& node (Join-Path $root 'tests/android/SyncDialog.Tests.js')
if($LASTEXITCODE){throw 'Android version comparison dialog failed'}
