Windows x64 **0.1.4-temperature-preview** (file version 0.1.4.0). This is an unsigned preview; macOS stable **v1.0.6** remains unchanged.

- Extract the entire `thermal-kun-windows-0.1.4-temperature-preview-x64.zip` and run `ThermalKun/ThermalKun.exe`. The .NET runtime is included.
- CPU/GPU load, memory use, temperature history and peak remain available. Windows 0.1.4 removes the unsupported memory-pressure label; Windows thermal status is also omitted.
- Optional CPU temperature uses bundled LibreHardwareMonitor 0.9.6 and PawnIO 2.2.0. Driver installation requires your confirmation, and the separate reader needs Windows administrator confirmation each launch. Cancelling keeps ordinary CPU/GPU/memory monitoring available.
- Startup is off by default. Updates preserve settings stored separately from the extracted app folder.
- Verification: all 508 payload-manifest entries, all 52 original source files, DLL/PDB identity and all 16 compiled-source hashes matched. 194 core checks, 12 temperature-boundary checks and live Windows named-pipe checks passed. The existing verified ZIP is retained without a rebuild.

[English setup](https://github.com/Kohei-SAWADA/thermal_kun/blob/windows-v0.1.4-temperature-preview/windows-dev/START-HERE.md) · [日本語の導入案内](https://github.com/Kohei-SAWADA/thermal_kun/blob/windows-v0.1.4-temperature-preview/windows-dev/START-HERE.ja.md) · [Verification and limits](https://github.com/Kohei-SAWADA/thermal_kun/blob/windows-v0.1.4-temperature-preview/windows-dev/VERIFICATION.md)

The standalone source ZIP is byte-identical to `ThermalKun/sources/SOURCE-Windows.zip` inside the binary ZIP. The original build source is [commit `5627dcc`](https://github.com/Kohei-SAWADA/thermal_kun/commit/5627dccdb48f5e658317ad14c27a12593b4cb81f); repository download instructions were updated for publication, while compiled source is unchanged. SHA-256 files and sanitized build/source metadata are attached.

Other PCs, Windows 10/ARM64, other DPI settings, multiple monitors, physical sleep/resume, external temperature calibration, login-cycle startup and SmartScreen reputation remain unverified. Automated checks do not validate native UI or live sensors; prior GUI/sensor checks on the verification PC are documented separately.

---

Windows x64版 **0.1.4-temperature-preview**（ファイル版0.1.4.0）を未署名プレリリースとして公開します。macOS安定版 **v1.0.6** はそのままです。

- `thermal-kun-windows-0.1.4-temperature-preview-x64.zip` を全体展開し、`ThermalKun/ThermalKun.exe` を起動します。.NETランタイムを同梱しています。
- CPU/GPU使用率、メモリ使用量、温度履歴とピークに対応。0.1.4では未対応のメモリ圧力ラベルを削除し、Windowsの熱状態も表示しません。
- 任意のCPU温度取得は同梱LibreHardwareMonitor 0.9.6とPawnIO 2.2.0を使用します。ドライバー導入には本人の確認が必要で、別プロセスの温度取得には起動ごとにWindowsの管理者確認が必要です。キャンセルしても通常のCPU/GPU/メモリ表示は利用できます。
- ログイン時起動の初期値はoff。更新時はアプリを終了してZIP全体を置き換えます。設定は別の場所に保存され、保持されます。
- 配布マニフェスト全508項目、元のソース全52ファイル、DLL/PDBの対応とコンパイル済みソースの全16ハッシュが一致。194コア・12温度境界・Windows実名前付きパイプのチェックも成功しました。再ビルド不要のため検証済みZIPを保持しています。

独立したソースZIPは配布ZIP内の `ThermalKun/sources/SOURCE-Windows.zip` とバイト単位で同一です。GitHub公開に伴う変更はリポジトリのダウンロード案内で、コンパイル対象のソースは変えていません。チェックサムと個人環境のパスを除いた検証メタデータを添付しています。

他PC、Windows 10/ARM64、別DPI、複数画面、実スリープ復帰、外部機器での温度校正、実ログイン、自動起動のログイン周期とSmartScreen評価は未検証です。今回の自動チェックと過去の実画面・実センサー確認は検証記録で区別しています。
