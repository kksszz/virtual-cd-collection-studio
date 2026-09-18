$ErrorActionPreference = 'Stop'
$taskJdk = Join-Path $PSScriptRoot '.tools\jdk\jdk-17.0.20.1+1'
$taskSdk = Join-Path $PSScriptRoot '.tools\android-sdk'
$taskGradle = Join-Path $PSScriptRoot '.tools\gradle\gradle-8.13\bin\gradle.bat'
if (!(Test-Path $taskGradle) -or !(Test-Path "$taskJdk\bin\java.exe") -or !(Test-Path "$taskSdk\platforms\android-36\android.jar")) {
    throw 'プロジェクト専用のJDK・Gradle・Android SDKが見つかりません。READMEの開発環境を確認してください。'
}
$previousJava = $env:JAVA_HOME
$previousSdk = $env:ANDROID_HOME
$previousGradle = $env:GRADLE_USER_HOME
try {
    $env:JAVA_HOME = $taskJdk
    $env:ANDROID_HOME = $taskSdk
    $env:GRADLE_USER_HOME = Join-Path $PSScriptRoot '.tools\gradle-user-home'
    $tasks = @(if ($args.Count) { $args } else { @(':app:assembleDebug', ':app:lintDebug') })
    & $taskGradle -p $PSScriptRoot --console=plain @tasks
    if ($LASTEXITCODE -ne 0) { throw "Android build failed ($LASTEXITCODE)" }
} finally {
    $env:JAVA_HOME = $previousJava
    $env:ANDROID_HOME = $previousSdk
    $env:GRADLE_USER_HOME = $previousGradle
}
