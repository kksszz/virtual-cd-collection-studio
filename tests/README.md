# 開発用テスト

リポジトリのルートから実行してください。Windows本体は `src/windows/ZipMp3Player.csproj`、共通のケース・画像・CD取り込みテストは `tests/WindowsCaseTests` にあります。

## Windowsの主要な回帰テスト

```powershell
dotnet build tests/WindowsCaseTests -c Release
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --cd-import
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --cd-preview
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --orientation
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --perspective
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --disc-crop
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --library-scope
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --scan-resume
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --digipak
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --mobile-model
```

通常設定を使用するテストもあるため、実行前に `ZIPMP3PLAYER_DATA_DIR` をテスト専用フォルダーへ設定してください。生成した試験データとスクリーンショットは開発用であり、配布物には含めません。ドライブや画像フォルダーを引数として指定する実機テストは、内容と対象を確認してから実行してください。

## その他のテスト

各 `*DevHarness` もこのフォルダーにまとめています。例：

```powershell
dotnet run --project tests/GaplessDevHarness -c Release
dotnet run --project tests/KeyboardDevHarness -c Release
dotnet run --project tests/LyricsDevHarness -c Release
dotnet run --project tests/BackupDevHarness -c Release
```

AiDevHarnessは配布版から切り離した旧AI機能の開発資料です。現行版の通常ビルド・回帰テストには含めません。Android専用テストは引き続き `android-player/tests` と `android-player/app/src/androidTest` にあります。
