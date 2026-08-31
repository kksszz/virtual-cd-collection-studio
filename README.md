# zip.mp3 Player and Manager Plus

Third-party assets and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

ZIP.MP3アルバムと一般的な音声ファイルを、まとめて管理・再生するWindows向け音楽プレイヤーです。

## 主な機能

- ZIP / ZIP.MP3内のMP3を展開管理せずに一覧化・再生
- 圧縮ZIP.MP3を無圧縮ZIP.MP3へ変換
- CBR/VBR MP3、WAV、FLAC、M4Aの再生（VBR MP3の時間指定頭出しに対応）
- アルバム画像、歌詞、LRC同期表示と自動スクロール
- アルバム／曲のお気に入り、検索、再生履歴・利用統計
- 再生履歴ダッシュボード（アーティスト・アルバム・曲のランキング）
- 3Dカバーフロー、CDケースの開閉・CD取り出し、画像パーツの手動割り当て
- タグの一括編集、表形式コピー＆貼り付け、任意バックアップ
- 原音忠実モード、イコライザー、低音量向け補正、ビジュアライザー
- 日本語／英語表示
- 設定・履歴・歌詞・追加画像などのバックアップと復元

## 動作環境

- Windows 10 / 11
- .NET 10 SDK（ソースからビルドする場合）

## ダウンロードと起動

[GitHub Releases](https://github.com/kksszz/zip-mp3-player-and-manager-plus/releases) の
Windows x64用ZIPを展開し、`ZipMp3Player.exe` を起動してください。
配布版には.NET実行環境を同梱しています。更新前に起動中の旧版を終了してください。
設定・履歴は既存のデータ保存先を引き続き使用します。

## 3Dカバーフローの操作

- 左ボタンドラッグ：回転
- 中央ボタン（ホイール押し込み）ドラッグ：上下左右へ移動
- R：移動位置を中央へ戻す
- ホイール：通常表示ではアルバム切り替え、全画面ではズーム
- F11：全画面切り替え、Esc：全画面を閉じる
- C：ケース開閉、D：開いたケースからCDを取り出す／戻す

Back・Spineの画像がない場合は無地で表示します。
「Spine付きBack」などの割り当てはアルバム画像のパーツ設定から指定できます。

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

AIギターリメイク・カラオケ機能は現行の配布版から切り離しています。
AIモデルやその実行環境は同梱していません。

## 開発時の追加テスト

手元の音源を使う任意テストでは、次の環境変数を設定できます。

- `ZIPMP3PLAYER_TEST_ARCHIVE`
- `ZIPMP3PLAYER_TEST_M4A`
- `ZIPMP3PLAYER_TEST_FLAC`

## ライセンス

現時点ではライセンスを指定していません。
