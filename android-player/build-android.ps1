$ErrorActionPreference = 'Stop'
$taskJdk = if (Test-Path (Join-Path $PSScriptRoot '.tools\jdk\jdk-17.0.20.1+1\bin\javac.exe')) {
    Join-Path $PSScriptRoot '.tools\jdk\jdk-17.0.20.1+1'
} else { $env:JAVA_HOME }
$taskSdk = if (Test-Path (Join-Path $PSScriptRoot '.tools\android-sdk\platforms\android-35\android.jar')) {
    Join-Path $PSScriptRoot '.tools\android-sdk'
} else { $env:ANDROID_HOME }
if (!$taskJdk -or !(Test-Path (Join-Path $taskJdk 'bin\javac.exe')) -or !$taskSdk -or !(Test-Path (Join-Path $taskSdk 'platforms\android-35\android.jar'))) {
    throw 'JDK 17とAndroid SDK Platform 35が見つかりません。.toolsへ配置するかJAVA_HOMEとANDROID_HOMEを設定してください。'
}
$previousJava = $env:JAVA_HOME
$previousSdk = $env:ANDROID_HOME
try {
    $env:JAVA_HOME = $taskJdk
    $env:ANDROID_HOME = $taskSdk
    $tasks = @(if ($args.Count) { $args } else { @(':app:assembleDebug', ':app:lintDebug') })
    & (Join-Path $PSScriptRoot 'gradlew.bat') --console=plain @tasks
    if ($LASTEXITCODE -ne 0) { throw "Android build failed ($LASTEXITCODE)" }
} finally {
    $env:JAVA_HOME = $previousJava
    $env:ANDROID_HOME = $previousSdk
}
