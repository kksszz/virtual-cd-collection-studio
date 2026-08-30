# zip.mp3 Player and Manager Plus

ZIP.MP3アルバムと一般的な音声ファイルを、まとめて管理・再生するWindows向け音楽プレイヤーです。

## 主な機能

- ZIP / ZIP.MP3内のMP3を展開管理せずに一覧化・再生
- 圧縮ZIP.MP3を無圧縮ZIP.MP3へ変換
- MP3、WAV、FLAC、M4Aの再生
- アルバム画像、歌詞、LRC同期表示と自動スクロール
- アルバム／曲のお気に入り、検索、再生履歴・利用統計
- タグの一括編集、表形式コピー＆貼り付け、任意バックアップ
- 原音忠実モード、イコライザー、低音量向け補正、ビジュアライザー
- 日本語／英語表示
- 設定・履歴・歌詞・追加画像などのバックアップと復元

## 動作環境

- Windows 10 / 11
- .NET 10 SDK（ソースからビルドする場合）

## ビルド

```powershell
dotnet restore
dotnet build -c Release
```

単一実行ファイルとして発行する例:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## データ保存

設定や管理データは、既定ではWindowsのローカルアプリデータ内にある `ZipMp3Player` フォルダへ保存します。音楽ファイル自体はバックアップ対象に含めません。

AI関連機能で利用する実行環境やモデルはリポジトリに含まれません。必要になった時点でアプリ側から準備します。

## 開発時の追加テスト

手元の音源を使う任意テストでは、次の環境変数を設定できます。

- `ZIPMP3PLAYER_TEST_ARCHIVE`
- `ZIPMP3PLAYER_TEST_M4A`
- `ZIPMP3PLAYER_TEST_FLAC`

## ライセンス

現時点ではライセンスを指定していません。
