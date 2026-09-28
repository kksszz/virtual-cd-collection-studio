# CUE付きCD音声イメージ

- 対応: 単一のBINARY ISO/BINを参照するCUE、全トラックAUDIO、2352 bytes/sectorの44.1 kHz・16 bit・stereo PCM。
- 対象フォルダーをライブラリのスキャン対象にするとCUE単位で登録します。ISO/BINを重複登録しません。ファイルを開く操作では同名CUE付きISO/BINも選択できます。
- 曲を右クリックし「CUEの曲情報を取得…」からMusicBrainzの候補を確認して反映します。CD構成のみを照会し、音声やローカルパスは送信しません。候補がない場合や通信失敗時は変更しません。
- 取得情報はアプリデータのcue-metadataに保存し、アプリのバックアップ対象に含めます。元のISO/BIN/CUEへの書き込みはしません。元データが変わると保存済み情報を無効化します。
- 単独ISO、一般的なデータISO、SACD、DVD-Audio、複数FILE、混在データトラック、追加PREGAP/POSTGAP、プリエンファシス指定は対象外です。

検証: ThemeDevHarnessをZIPMP3PLAYER_CUE_TEST=1で実行。専用一時フォルダーで曲境界、シーク、メタデータ保存、識別キー、スキャン重複、異常CUE拒否を検証します。ZIPMP3PLAYER_CUE_SAMPLE_DIRECTORY指定時はGLAYの4枚57曲の読み取りも確認します。音声出力の聴感確認は別途必要です。
