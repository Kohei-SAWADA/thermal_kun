# thermal kun Windows 0.1.4-temperature-preview

確認日: 2026-10-11。[English](START-HERE.md)

## ダウンロード・起動・更新

1. [x64 ZIP](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-temperature-preview-x64.zip) と [SHA-256ファイル](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-temperature-preview-x64.zip.sha256) を取得します。現行ファイルは `thermal-kun-windows-0.1.4-temperature-preview-x64.zip`（93,206,008 bytes）です。
2. PowerShellで `Get-FileHash .\thermal-kun-windows-0.1.4-temperature-preview-x64.zip -Algorithm SHA256` を実行し、`0c7dd14b3a5ea2d24bac6b2e3b57a8ca46d49632e3609d677f268e56c38d07b6` と照合します。
3. ZIP全体を展開して `ThermalKun/ThermalKun.exe` を起動します。.NETランタイム同梱の未署名Windows x64プレビューです。
4. CPU温度を使う場合は設定の「CPU温度を有効にする / 再接続」を選びます。画面は通常権限で、温度取得は同梱LibreHardwareMonitor 0.9.6とPawnIO 2.2.0を使う別プロセスです。初回のドライバー導入には本人の確認が必要で、対応ドライバーが導入済みなら再導入は不要です。温度補助プロセスはアプリ起動ごとにWindowsの管理者確認が必要です。キャンセルしてもCPU/GPU使用率とメモリは利用できます。別のHTTPサーバー設定は不要です。

更新は設定またはトレイのQuitで終了してから、同じ場所にZIP全体を展開します。設定は `%LOCALAPPDATA%\ThermalKun\temperature-settings.json` に保存され、保持されます。ログイン時起動の初期値はoffです。別の場所へ移す場合は先に自動起動をoffにし、終了・移動後、必要なら再びonにします。アプリフォルダーの削除ではPawnIOは削除されません。

## 操作と0.1.4の変更

詳細／コンパクトはCtrl+C、設定はCtrl+,、非表示はEscapeです。再表示はトレイのShow・ダブルクリック・同じEXEの再実行、終了は設定またはトレイのQuitです。Dark／Light／System、1／2／5／10秒の更新間隔に対応します。

0.1.4ではMEMORY横のメモリ圧力の「Unsupported」を表示しません。メモリ使用量のGiB値とバーは表示します。Windows版ではTHERMAL STATUSも省略し、温度やメモリ使用率から状態を推定しません。欠測・古い温度はUnavailableになります。

## 最新ソースと整合性

対応ソースは [`thermal-kun-windows-0.1.4-source.zip`](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-source.zip) と [ルートのWindows/](../Windows/) です。独立したソースZIPは配布ZIP内の `ThermalKun/sources/SOURCE-Windows.zip` とバイト単位で同一です。ビルド時ソース全52ファイルが元のローカルソースと完全一致し、commit `5627dccdb48f5e658317ad14c27a12593b4cb81f` とはGitの改行正規化を除いて同じ内容です。配布物の全508マニフェスト項目、DLLとPDBの対応、全16ソースチェックサムを確認しました。194コア・12温度境界・Windows実名前付きパイプのチェックも今回再実行して成功しました。再ビルドは不要だったため、現在運用中と一致するZIPを保持しています。

[検証記録](VERIFICATION.md)、[最新版メタデータ](../LATEST_WINDOWS_BUILD.json)、[ビルド手順](../Windows/README.md)を参照してください。.NET 10 SDKと、ソース記載の固定版依存ファイルを使います。他PC、ARM64、Windows 10、別DPI、複数画面、実スリープ復帰、外部測定器での温度校正、実ログインは未検証です。この整合性確認ではGUIの再起動・ドライバー導入・利用者設定変更を行っていません。過去の0.1.4実画面検証は `Windows/docs/VERIFICATION.md` に区別して保存しています。

このリリースの配布対象はWindows x64のみです。

GitHub公開に伴いリポジトリのダウンロード案内を更新しています。配布ソースZIPは元のビルド時スナップショットを保持し、コンパイル対象ソースは変えていません。
