# 開発用テスト

リポジトリのルートから実行してください。Windows本体は `src/windows/ZipMp3Player.csproj`、共通のケース・画像・CD取り込みテストは `tests/WindowsCaseTests` にあります。

## Windowsの主要な回帰テスト

```powershell
dotnet build tests/WindowsCaseTests -c Release
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --cd-import
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --accuraterip
dotnet run --project tests/WindowsCaseTests -c Release --no-build -- --booklet-print artifacts/booklet-print
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

AccurateRip実機診断は `--accuraterip-device S: 6` のようにドライブと確認済みの補正値（ステレオサンプル単位）を指定します。CD識別情報を公式サーバーへ照会し、登録があれば全曲をメモリー上へ読み取り、各ブロックの二重読み取り一致とv1/v2チェックサムを確認します。音声ファイル・アプリ設定・ライブラリーは保存／変更しません。上の例の補正値はすべてのドライブには適用できません。通信不能・未登録時は読み取りを開始せず、照合状態を表示します。

ドライブ補正の公式一覧取得を確認する診断は `--drive-offset-lookup "hp HLDS | DVDROM DUD1N | MDM2 | "` です。一覧を取得して完全一致検索するだけで、ドライブの読み取りや設定変更は行いません。`--accuraterip` は一覧の解析・一致条件・除外条件も検証し、`--cd-import-ui` は自動設定・保存済み設定の保護・通信失敗・画面幅をテスト用設定で確認します。

`--booklet-print` はプリンターへ送信しない検証です。ページ範囲・用紙に収める／mm幅指定・余白・印刷可能領域・読み込み失敗・設定入力を確認し、指定フォルダーへPNGとテスト用XPSを生成します。物理印刷やPDFプリンターのダイアログは呼びません。

## その他のテスト

各 `*DevHarness` もこのフォルダーにまとめています。例：

```powershell
dotnet run --project tests/GaplessDevHarness -c Release
dotnet run --project tests/KeyboardDevHarness -c Release
dotnet run --project tests/LyricsDevHarness -c Release
dotnet run --project tests/BackupDevHarness -c Release
```

AiDevHarnessは配布版から切り離した旧AI機能の開発資料です。現行版の通常ビルド・回帰テストには含めません。Android専用テストは引き続き `android-player/tests` と `android-player/app/src/androidTest` にあります。
