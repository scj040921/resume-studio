param([string]$Sdk = $env:ANDROID_HOME, [string]$Output = '', [string]$SigningStore = '', [string]$SigningAlias = 'resume-studio', [string]$VersionName = '', [int]$VersionCode = 0)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Sdk){$Sdk=Join-Path $env:LOCALAPPDATA 'Android\Sdk'}
$jdk=if($env:JAVA_HOME){$env:JAVA_HOME}else{'C:\Program Files\Java\jdk-21'}
$java=Join-Path $jdk 'bin\java.exe'; $javac=Join-Path $jdk 'bin\javac.exe'
$tools=Join-Path $Sdk 'build-tools\36.0.0'; $platform=Join-Path $Sdk 'platforms\android-35\android.jar'
foreach($required in @($javac,$platform,(Join-Path $tools 'aapt2.exe'),(Join-Path $tools 'lib\d8.jar'))){if(!(Test-Path -LiteralPath $required)){throw "Required Android build tool missing: $required"}}
$build=Join-Path $root 'artifacts\android-build'; $source=Join-Path $root 'client-android'
# Only generated output is replaced; dependency caches and signing stores remain separate.
foreach($name in @('classes','dex','generated','r-java','assets','ocr')){
    $generated=[IO.Path]::GetFullPath((Join-Path $build $name));$boundary=[IO.Path]::GetFullPath($build)+[IO.Path]::DirectorySeparatorChar
    if(!$generated.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Generated Android path escaped the build directory'}
    if(Test-Path -LiteralPath $generated){if((Get-Item -LiteralPath $generated).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Generated output must not be a link'};Remove-Item -LiteralPath $generated -Recurse -Force}
}
foreach($dir in @($build,"$build\classes","$build\dex","$build\generated\com\resumestudio","$build\assets")){New-Item -ItemType Directory -Force $dir|Out-Null}
$dependency=Join-Path $root 'artifacts\android-dependencies';New-Item -ItemType Directory -Force $dependency|Out-Null
$pdfAar=Join-Path $dependency 'pdfbox-android.aar'
if(!(Test-Path -LiteralPath $pdfAar)){Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/com/tom-roush/pdfbox-android/2.0.27.0/pdfbox-android-2.0.27.0.aar' -OutFile $pdfAar -TimeoutSec 90}
if((Get-FileHash -LiteralPath $pdfAar -Algorithm SHA256).Hash -ne '30277F879CFD571DB2A137582C95516A0D4EA6778E945519BC58CA93D57D88C7'){throw 'PDFBox dependency checksum mismatch'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($pdfAar,(Join-Path $dependency 'pdfbox'),$true)
$pdfJar=Join-Path $dependency 'pdfbox\classes.jar'
$manifestPath=Join-Path $build 'AndroidManifest.xml'
$manifestText=[IO.File]::ReadAllText((Join-Path $source 'AndroidManifest.xml'))
if($VersionName){$manifestText=[regex]::Replace($manifestText,'android:versionName="[^"]+"',('android:versionName="'+[Security.SecurityElement]::Escape($VersionName)+'"'))}
if($VersionCode -gt 0){$manifestText=[regex]::Replace($manifestText,'android:versionCode="\d+"',('android:versionCode="'+$VersionCode+'"'))}
[IO.File]::WriteAllText($manifestPath,$manifestText,[Text.UTF8Encoding]::new($false))
# Generate Android-compatible API calls without forking or changing desktop business behavior.
$shared=@('Json','Store','ResumeHistory','SyncStore','SyncReview','CalendarText','BackupCatalog','ResumeDocx','ResumeWord','ResumeLayout','WorkspaceTools','Templates','CompanySources','CompanyJobs','Jobs','JobWorkflow','JobCapture','JobPageText','ResumeAdvisor','ResumeImport','InterviewStudy')
foreach($name in $shared){
    $text=[IO.File]::ReadAllText((Join-Path $root "backend\src\main\java\com\resumestudio\$name.java"))
    $text=$text.Replace('import java.net.http.*;','').Replace('Map.of(', 'MobileCompat.map(').Replace('Set.of(', 'MobileCompat.set(').Replace('List.of(', 'Arrays.asList(').Replace('.toList()', '.collect(java.util.stream.Collectors.toList())').Replace('.isBlank()', '.trim().isEmpty()').Replace('HexFormat.of().formatHex(', 'MobileCompat.hex(').Replace('Files.readString(', 'MobileCompat.readString(').Replace('Files.writeString(', 'MobileCompat.writeString(').Replace('Path.of(', 'Paths.get(').Replace('remaining.findAny().isEmpty()', '!remaining.findAny().isPresent()')
    $text=[regex]::Replace($text,'(\w+)\.readNBytes\(([^)]+)\)','MobileCompat.readNBytes($1,$2)')
    $text=$text.Replace('map.put(key, value(depth+1));','if(map.containsKey(key)) throw new IllegalArgumentException("JSON has duplicate keys"); map.put(key, value(depth+1));')
    if($name -in @('JobCapture','JobPageText')){$text=[regex]::Replace($text,'MobileCompat\.map\(([^;\r\n]+?)\)\.entrySet\(\)','MobileCompat.<String,String>map($1).entrySet()')}
    $text=$text.Replace('return MobileCompat.map("嵌入式"','return MobileCompat.<String,String>map("嵌入式"')
    $text=$text.Replace('URLEncoder.encode(keyword,java.nio.charset.StandardCharsets.UTF_8)','URLEncoder.encode(keyword,"UTF-8")').Replace('URLEncoder.encode(s,StandardCharsets.UTF_8)','URLEncoder.encode(s,"UTF-8")')
    $text=$text.Replace('URLDecoder.decode(kv[0],java.nio.charset.StandardCharsets.UTF_8)','URLDecoder.decode(kv[0],"UTF-8")')
    if($name -eq 'CompanyJobs'){
        $start=$text.IndexOf('    private static final class Network');$end=$text.IndexOf('    private final ExecutorService',$start);$text=$text.Remove($start,$end-$start)
        $start=$text.IndexOf('    private static String request(');$end=$text.IndexOf('    static String webpage(',$start)
        $text=$text.Remove($start,$end-$start).Insert($start,"    private static String request(URI url,String payload) throws Exception { checkPublic(url); return MobileHttp.publicRequest(url,payload,16_000_000); }`n")
        $text=$text.Replace('CompletableFuture.supplyAsync(()->query(s,keyword,city,page),workers).completeOnTimeout(result(Arrays.asList(),0,"error","本轮官网查询超过30秒；可单独选择该公司重试。",page),30,TimeUnit.SECONDS)','MobileCompat.timeout(CompletableFuture.supplyAsync(()->query(s,keyword,city,page),workers),result(Arrays.asList(),0,"error","本轮官网查询超过30秒；可单独选择该公司重试。",page))')
        $text=$text.Replace('private static String encode(String s) {','private static String encode(String s) throws Exception {')
    }
    if($name -eq 'Jobs'){
        $start=$text.IndexOf('    private static final class Network');$end=$text.IndexOf('    private record Cached',$start);$text=$text.Remove($start,$end-$start)
        $start=$text.IndexOf('    private Map<String,Object> fetch(');$end=$text.IndexOf('    public synchronized Map<String,Object> search(',$start)
        $text=$text.Remove($start,$end-$start).Insert($start,"    private Map<String,Object> fetch(String url) throws Exception { return Json.object(Json.parse(MobileHttp.publicRequest(URI.create(url),null,4_000_000))); }`n")
    }
    [IO.File]::WriteAllText("$build\generated\com\resumestudio\$name.java",$text,[Text.UTF8Encoding]::new($false))
}
# The desktop validator is platform independent; exclude its JDK HTTP server shell.
$server=[IO.File]::ReadAllText((Join-Path $root 'backend\src\main\java\com\resumestudio\Server.java'))
$start=$server.IndexOf('    static void validate(');$end=$server.IndexOf('    private static Map<String,String> query(',$start)
$validator='package com.resumestudio; import java.util.*; import java.net.*; public final class Server {'+$server.Substring($start,$end-$start)+'}'
$validator=$validator.Replace('Set.of(', 'MobileCompat.set(').Replace('List.of(', 'Arrays.asList(').Replace('.isBlank()', '.trim().isEmpty()')
[IO.File]::WriteAllText("$build\generated\com\resumestudio\Server.java",$validator,[Text.UTF8Encoding]::new($false))
# Carry the exact desktop rendering styles, template catalog and examples into the APK.
Copy-Item -Path "$source\assets\*" -Destination "$build\assets" -Recurse -Force
# Bundle only the Fluent UI Web Components button and GSAP used by the WebView.
# Node is a build-time dependency; no network assets are requested by the installed app.
if(!(Get-Command npm -ErrorAction SilentlyContinue)){throw 'Node.js and npm are required to bundle the offline Android UI assets'}
Push-Location $source
try{
    & npm ci --registry=https://registry.npmjs.org --no-audit --no-fund
    if($LASTEXITCODE){throw 'Android UI npm dependencies could not be restored'}
    & npm run build:vendor
    if($LASTEXITCODE){throw 'Android Fluent UI and GSAP bundle failed'}
}finally{Pop-Location}
if(!(Test-Path -LiteralPath "$build\assets\vendor.js")){throw 'Android offline UI bundle was not generated'}
# Always use the tracked desktop verification key; *.pem intentionally excludes local secrets.
$updateKey=Join-Path $root 'client-winui\Assets\UpdatePublicKey.pem'
$keyText=[IO.File]::ReadAllText($updateKey)
$rsa=[Security.Cryptography.RSA]::Create()
try{$rsa.ImportFromPem($keyText);if($rsa.KeySize -lt 2048){throw 'Update public key is too short'}}finally{$rsa.Dispose()}
Copy-Item -LiteralPath $updateKey -Destination "$build\assets\UpdatePublicKey.pem" -Force

Copy-Item -Path "$dependency\pdfbox\assets\*" -Destination "$build\assets" -Recurse -Force
$capture=[IO.File]::ReadAllText((Join-Path $root 'client-winui\MainWindow.PlatformCapture.cs'))
$captureScript=[regex]::Match($capture,'(?s)PlatformJobsScript="""\s*(.*?)\s*""";').Groups[1].Value
if(!$captureScript){throw 'Desktop platform extraction script missing'}
[IO.File]::WriteAllText("$build\assets\capture.js",$captureScript,[Text.UTF8Encoding]::new($false))
$renderer=[IO.File]::ReadAllText((Join-Path $root 'client-winui\Services\ResumeRenderer.cs'))
$style=[regex]::Match($renderer,'(?s)<style>(.*?)</style>').Groups[1].Value
$style=$style.Replace('{{color}}','var(--accent)').Replace('{{size}}pt','var(--size)').Replace('{{size}}','var(--size)').Replace('{{line}}','var(--line)').Replace("'{{font}}'",'var(--family)').Replace('https://resumestudio.assets/Fonts/','https://app.resumestudio.local/fonts/')
# Android retains its existing layout controls; resolve new Windows-only settings to compatible defaults.
$style=$style.Replace('{{vertical}}','14').Replace('{{horizontal}}','16').Replace('{{gap}}','4.8').Replace('{{alignment}}','')
if($style.Contains('{{')){throw 'Android resume CSS contains unresolved desktop layout settings'}
[IO.File]::WriteAllText("$build\assets\resume.css",$style,[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force "$build\assets\fonts"|Out-Null
Copy-Item -LiteralPath "$root\client-winui\Assets\Fonts\MiSans-Regular.ttf" -Destination "$build\assets\fonts" -Force
Copy-Item -LiteralPath "$root\client-winui\Assets\Fonts\MiSans-License.pdf" -Destination "$build\assets\fonts" -Force
foreach($notice in @('LICENSE','THIRD_PARTY_NOTICES.md','RUNTIME_NOTICES.md')){Copy-Item -LiteralPath "$root\$notice" -Destination "$build\assets" -Force}
Copy-Item -LiteralPath "$root\ThirdParty" -Destination "$build\assets" -Recurse -Force
$ocr=& (Join-Path $PSScriptRoot 'setup-android-ocr.ps1') -BuildDirectory $build -AssetsDirectory "$build/assets" -ManifestPath $manifestPath -Aapt2 "$tools/aapt2.exe"
$resourceScratch=Join-Path ([IO.Path]::GetTempPath()) ('rs-app-res-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resourceScratch|Out-Null
try{
    Copy-Item -Path "$source/res" -Destination $resourceScratch -Recurse -Force
    & "$tools/aapt2.exe" compile --dir (Join-Path $resourceScratch 'res') -o "$build/resources.zip"
    if($LASTEXITCODE){throw 'Resource compilation failed'}
}finally{
    $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $fullScratch=[IO.Path]::GetFullPath($resourceScratch)
    if($fullScratch.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($fullScratch) -match '^rs-app-res-[0-9a-f]{32}$'){Remove-Item -LiteralPath $fullScratch -Recurse -Force -ErrorAction SilentlyContinue}
}
$linkScratch=Join-Path ([IO.Path]::GetTempPath()) ('rs-apk-link-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $linkScratch|Out-Null
try{
    # aapt2's asset-directory reader has the same Windows Unicode-path limitation.
    Copy-Item -Path "$build/assets" -Destination $linkScratch -Recurse -Force
    $linkArguments=@('link','-o',"$build/unsigned.apk",'-I',$platform,'--manifest',$manifestPath,'-A',(Join-Path $linkScratch 'assets'),'--java',"$build/r-java",'--auto-add-overlay')
    foreach($resource in @($ocr.resources)+@("$build/resources.zip")){$linkArguments+=@('-R',$resource)}
    & "$tools/aapt2.exe" @linkArguments
    if($LASTEXITCODE){throw 'APK resource link failed'}
}finally{
    $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $fullScratch=[IO.Path]::GetFullPath($linkScratch)
    if($fullScratch.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($fullScratch) -match '^rs-apk-link-[0-9a-f]{32}$'){Remove-Item -LiteralPath $fullScratch -Recurse -Force -ErrorAction SilentlyContinue}
}
$rFile=Join-Path $build 'r-java/com/resumestudio/mobile/R.java';$rSource=[IO.File]::ReadAllText($rFile)
foreach($package in $ocr.packages){if($package -eq 'com.resumestudio.mobile'){continue};$target=Join-Path $build ('r-java/'+$package.Replace('.','/')+'/R.java');New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null;[IO.File]::WriteAllText($target,$rSource.Replace('package com.resumestudio.mobile;','package '+$package+';'),[Text.UTF8Encoding]::new($false))}
$javaSources=@(Get-ChildItem "$source\src","$build\generated","$build\r-java" -Recurse -Filter '*.java'|ForEach-Object FullName)
$argsFile="$build\javac.args";[IO.File]::WriteAllLines($argsFile,@('-encoding','UTF-8','--release','17','-classpath',('"'+($platform+';'+$pdfJar+';'+($ocr.jars -join ';')).Replace('\','/')+'"'),'-d',('"'+"$build/classes".Replace('\','/')+'"'))+@($javaSources|ForEach-Object {'"'+$_.Replace('\','/')+'"'}),[Text.UTF8Encoding]::new($false))
& $javac "@$argsFile";if($LASTEXITCODE){throw 'Android Java compile failed'}
& (Join-Path $jdk 'bin\jar.exe') --create --file "$build\classes.jar" -C "$build\classes" .
& $java -cp "$tools\lib\d8.jar" com.android.tools.r8.D8 --min-api 26 --lib $platform --output "$build\dex" "$build\classes.jar" $pdfJar @($ocr.jars);if($LASTEXITCODE){throw 'Android DEX compile failed'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Windows aapt2 can emit backslashes in nested asset names. Android AssetManager
# treats those as literal characters; normalize BOTH local headers and central entries.
$raw=[IO.Compression.ZipFile]::OpenRead("$build\unsigned.apk")
$stream=[IO.File]::Open("$build\normalized.apk",[IO.FileMode]::Create)
$apk=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($entry in $raw.Entries){
        $name=$entry.FullName.Replace('\','/')
        $level=if($name -eq 'resources.arsc'){[IO.Compression.CompressionLevel]::NoCompression}else{[IO.Compression.CompressionLevel]::Optimal}
        $target=$apk.CreateEntry($name,$level);$inputStream=$entry.Open();$outputStream=$target.Open()
        try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
    }
    foreach($library in $ocr.native){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($apk,$library.path,$library.name)|Out-Null}
    foreach($dex in Get-ChildItem "$build\dex" -Filter '*.dex'){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($apk,$dex.FullName,$dex.Name)|Out-Null}
}finally{$apk.Dispose();$raw.Dispose();$stream.Dispose()}
$verificationApk=[IO.Compression.ZipFile]::OpenRead("$build\normalized.apk")
try{
    $keyEntry=$verificationApk.GetEntry('assets/UpdatePublicKey.pem')
    if(!$keyEntry){throw 'APK is missing the update verification key'}
    $reader=[IO.StreamReader]::new($keyEntry.Open())
    try{if($reader.ReadToEnd() -cne $keyText){throw 'APK update key differs from the canonical desktop key'}}finally{$reader.Dispose()}
}finally{$verificationApk.Dispose()}
& "$tools\zipalign.exe" -f 4 "$build\normalized.apk" "$build\aligned.apk";if($LASTEXITCODE){throw 'APK alignment failed'}
$temporaryStore=$false
if($env:RESUMESTUDIO_ANDROID_KEYSTORE_B64){$SigningStore=Join-Path $build 'release-signing.p12';[IO.File]::WriteAllBytes($SigningStore,[Convert]::FromBase64String($env:RESUMESTUDIO_ANDROID_KEYSTORE_B64));$temporaryStore=$true}
if(!$SigningStore){
    $SigningStore=Join-Path $env:USERPROFILE '.android\resume-studio-local.p12'
    if(!(Test-Path -LiteralPath $SigningStore)){New-Item -ItemType Directory -Force (Split-Path $SigningStore)|Out-Null;& (Join-Path $jdk 'bin\keytool.exe') -genkeypair -keystore $SigningStore -storepass android -keypass android -alias $SigningAlias -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Resume Studio Local, O=Resume Studio, C=CN';if($LASTEXITCODE){throw 'Local signing key generation failed'}}
    if(!$env:RESUMESTUDIO_ANDROID_STORE_PASS){$env:RESUMESTUDIO_ANDROID_STORE_PASS='android'}
}
if(!$env:RESUMESTUDIO_ANDROID_STORE_PASS){throw 'Set RESUMESTUDIO_ANDROID_STORE_PASS for the signing store'}
if(!$Output){[xml]$androidIdentity=$manifestText;$Output=Join-Path $root ('release/ResumeStudio-Android-v'+$androidIdentity.manifest.versionName+'.apk')}
New-Item -ItemType Directory -Force (Split-Path $Output)|Out-Null
try{
    & $java -jar "$tools\lib\apksigner.jar" sign --ks $SigningStore --ks-key-alias $SigningAlias --ks-pass env:RESUMESTUDIO_ANDROID_STORE_PASS --out $Output "$build\aligned.apk";if($LASTEXITCODE){throw 'APK signing failed'}
    & $java -jar "$tools\lib\apksigner.jar" verify --verbose $Output;if($LASTEXITCODE){throw 'APK signature verification failed'}
    Get-Item -LiteralPath $Output|Select-Object FullName,Length
    Get-FileHash -LiteralPath $Output -Algorithm SHA256
}finally{if($temporaryStore){Remove-Item -LiteralPath $SigningStore -Force -ErrorAction SilentlyContinue}}
