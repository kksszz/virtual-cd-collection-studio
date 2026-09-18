$ErrorActionPreference = 'Stop'
$taskJdk = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.tools\jdk') -Directory | Select-Object -First 1
if (!$taskJdk) { throw 'JDK 17を.tools\jdkに配置してください。' }
$output = Join-Path $PSScriptRoot '.tools\core-test-classes'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& (Join-Path $taskJdk.FullName 'bin\javac.exe') -encoding UTF-8 -d $output (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\library\AudioFormats.java') (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\archive\StoredZipIndex.java') (Join-Path $PSScriptRoot 'tests\StoredZipIndexTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed' }
& (Join-Path $taskJdk.FullName 'bin\java.exe') -cp $output StoredZipIndexTest @args
if ($LASTEXITCODE -ne 0) { throw 'Core test failed' }
& (Join-Path $taskJdk.FullName 'bin\javac.exe') -encoding UTF-8 -d $output (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\library\ThumbnailLayout.java') (Join-Path $PSScriptRoot 'tests\ThumbnailLayoutTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Thumbnail test compilation failed' }
& (Join-Path $taskJdk.FullName 'bin\java.exe') -cp $output ThumbnailLayoutTest
if ($LASTEXITCODE -ne 0) { throw 'Thumbnail test failed' }
& (Join-Path $taskJdk.FullName 'bin\javac.exe') -encoding UTF-8 -d $output (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\library\TrackLabel.java') (Join-Path $PSScriptRoot 'tests\TrackLabelTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Track label test compilation failed' }
& (Join-Path $taskJdk.FullName 'bin\java.exe') -cp $output TrackLabelTest
if ($LASTEXITCODE -ne 0) { throw 'Track label test failed' }
& (Join-Path $taskJdk.FullName 'bin\javac.exe') -encoding UTF-8 -d $output (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\library\LegacyId3Title.java') (Join-Path $PSScriptRoot 'tests\LegacyId3TitleTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Legacy ID3 test compilation failed' }
& (Join-Path $taskJdk.FullName 'bin\java.exe') -cp $output LegacyId3TitleTest
if ($LASTEXITCODE -ne 0) { throw 'Legacy ID3 test failed' }
& (Join-Path $taskJdk.FullName 'bin\javac.exe') -encoding UTF-8 -d $output (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\audio\SoundConfig.java') (Join-Path $PSScriptRoot 'app\src\main\java\jp\virtualcd\player\audio\SoundEngine.java') (Join-Path $PSScriptRoot 'tests\SoundEngineTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Sound test compilation failed' }
& (Join-Path $taskJdk.FullName 'bin\java.exe') -cp $output SoundEngineTest
if ($LASTEXITCODE -ne 0) { throw 'Sound test failed' }
