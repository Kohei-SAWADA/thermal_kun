# thermal kun

<p align="right">
  <a href="README.md">English</a> |
  <strong>日本語</strong>
</p>

**MacのCPU温度をリアルタイムで確認。**

CPU温度・CPU/GPU使用率・メモリ・最近の温度履歴を、コンパクトなネイティブのデスクトップパネルで確認できます。

**[macOS版をダウンロード（Apple Silicon）](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.4/thermal-kun-macOS-arm64-v1.0.4.zip)** · [導入ガイド](docs/INSTALL.ja.md) · [デモ](#デモ)

macOS 13以降。配布アプリはApple Silicon専用で、Intel Macは非対応です。アドホック署名・未公証です。

## デモ

[Macでの実操作デモを見る（MP4）](assets/promo/thermal-kun-demo.mp4)

![thermal kunの実測値と表示切替の実操作](assets/promo/thermal-kun-demo.gif)

M3 Mac上の実際の1.0.4アプリを収録しています。`SMC · E-ZONE MEAN`は取得できる効率領域3センサーの平均で、CPU最高温度ではありません。Thermal StateはOSの熱状態で、スロットリング率ではありません。値は収録時の負荷を反映します。

## 主な機能

- CPU温度・CPU/GPU使用率・使用メモリとメモリ圧力の実測表示。
- compact/detail、最近の履歴、Dark/Light/Systemの外観。
- 移動・サイズ変更できるパネルと、ローカルの読み取り専用計測。

[詳しい機能](#機能) · [必要環境](#必要環境) · [計測の意味](#データソースと表示の意味)

<p align="center">
  <img src="assets/thermal-kun-thumbnail.png" alt="thermal kun — CPU temperature and system telemetry" width="100%">
</p>

<p align="center">
  <img src="assets/thermal-kun-icon.png" alt="thermal kun thermometer icon" width="128">
</p>




CPU温度とシステムの熱状態を、デスクトップでひと目確認するための小さなmacOSアプリです。Swift／SwiftUI／AppKitで実装した正方形の常駐パネルに、CPU温度、CPU／GPU使用率、メモリを表示します。アプリ内の表示はすべて英語です。

温度計をモチーフにしたロゴと、**チャコール／パープル／ライムグリーン** の配色を使っています。パネルを自由に移動して、Usage Kunの下など好きな場所に並べられます。不透明な背景、細い罫線、直角のフレーム、等幅の数字で、近未来のコックピットのような計器に仕上げています。

## ダウンロード

現在のバージョンは **1.0.4（build 5）** です。Apple Silicon（`arm64`）用アプリを [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/tag/v1.0.4) からダウンロードできます。

- [thermal kunのmacOS版をダウンロード](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.4/thermal-kun-macOS-arm64-v1.0.4.zip)
- [SHA-256チェックサム](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.4/thermal-kun-macOS-arm64-v1.0.4.zip.sha256)

導入は [インストールガイド](docs/INSTALL.ja.md)、ソースビルドとZIPの作成は [リリース手順](docs/RELEASING.md) を参照してください。

## スクリーンショット

<p align="center">
  <img src="assets/screenshots/detail-dark.png" alt="thermal kunのダーク詳細表示。CPU温度、熱状態、使用率、履歴を表示。" width="420">
  <br><em>詳細表示 — 温度履歴とハードウェアの情報を確認できます。</em>
</p>

<p align="center">
  <img src="assets/screenshots/compact-dark.png" alt="thermal kunのダークコンパクト表示。CPU温度、熱状態、CPU使用率、メモリを表示。" width="350">
  <br><em>コンパクト表示 — 作業中に確認したい情報を優先します。</em>
</p>

<p align="center">
  <img src="assets/screenshots/detail-light.png" alt="thermal kunのライト詳細表示。明るい背景にパープルとライムのアクセント。" width="420">
  <br><em>ライト表示 — 明るいデスクトップでも読みやすく。</em>
</p>

<p align="center">
  <img src="assets/screenshots/settings.png" alt="thermal kunの設定画面。表示、更新間隔、自動起動、アニメーション、測定元を設定・確認。" width="520">
  <br><em>設定 — 表示や更新間隔を選び、測定元を確認できます。</em>
</p>

表示値は撮影時点のものです。取得できる項目やセンサーはMacの機種とmacOSによって異なります。

## 機能

- CPU温度、`Thermal State`、CPU／GPU使用率、メモリ使用量・プレッシャーを表示
- 最大180サンプルの温度グラフと、監視開始後の最高温度・リセット
- ドラッグ移動と、縦横1:1を保つサイズ変更。位置・サイズを保存し、画面構成変更時に補正
- コンパクト／詳細表示、`Dark`／`Light`／`System` の切り替え
- 通常はデスクトップの層に表示し、`Always on Top` で前面表示へ切り替え
- メニューバーの `Show`／`Hide`、`Settings…`、`Quit thermal kun` と、任意の `Launch at Login`
- 更新間隔は1／2／5／10秒から選択（初期値2秒）。短いアニメーションは無効化でき、システムの「視差効果を減らす」に対応

## 必要環境

- macOS 13以降を対象に設定しています。確認環境はApple SiliconのMacで、他機種・他OSでの動作は未確認です。
- ソースビルドにはSwift 6以降と、macOS SDKを含むXcode Command Line Toolsが必要です。
- ビルドは現在のMacのアーキテクチャ向けです。Universal Binaryは生成しません。
- Windows／Linux版はありません。管理者権限や特権ヘルパーは使用しません。

## すぐ試す

### Release ZIPからインストール

1. [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/latest) からZIPをダウンロードし、Finderで解凍します。
2. 展開先の `thermal kun.app` を `/Applications` に移動します。
3. アプリを開きます。パネルが非表示の場合はメニューバーの温度計アイコンから `Show` を選びます。
4. 必要な場合だけ、`Settings` の `Launch at Login` を有効にします。

アプリはアドホック署名付きで、Appleによる公証は行っていません。macOSで開発元の確認に関する警告が出る場合の手順は [初回起動の案内](docs/INSTALL.ja.md#初回起動でmacosに止められる場合) を参照してください。

### ソースからビルド

ソースのディレクトリで次を実行します。外部パッケージのインストールは不要です。

```sh
./Scripts/build.sh
./Scripts/run.sh
```

`build/thermal kun.app` が生成されます。Finderで開いても起動できます。

継続して使う場合はFinderでアプリを `/Applications` にコピーし、開発用のアプリを終了して、コピー先を起動してください。`Launch at Login` は初期状態では無効です。安定した保存先から起動した後、`Settings` で有効にします。macOSで承認が必要な場合は `Open Login Items Settings` を選びます。

### パネルの操作

1. 上部のアプリ名付近をドラッグして移動します。右下をドラッグすると正方形のままサイズを変えられます。
2. 上部の矢印ボタンでコンパクト／詳細表示を切り替えます。詳細表示は必要に応じてスクロールできます。
3. グラフにポインタを重ねると記録値を確認できます。`RESET` は最高温度だけをリセットします。
4. 上部のスライダーアイコン、またはメニューバーの `Settings…` から設定を開きます。配置を戻すには `Center Panel on Screen` を選びます。

Usage Kunには自動追従しません。それぞれのパネルを手動で配置できます。背景の不透明度は固定で、変更する設定はありません。

### 更新・検証

更新前にメニューバーの `Quit thermal kun` で終了し、新しい `.app` に置き換えて起動します。設定とパネル位置はアプリとは別に保存するため、置き換えても保持されます。ソースから更新する場合は、ソースを更新して `./Scripts/build.sh` を実行してください。詳細は [更新手順](docs/INSTALL.ja.md#更新) を参照してください。

モデル・履歴・画面位置復元の軽量チェック:

```sh
./Scripts/check.sh
```

実際の取得元と測定値をJSONで確認するには、ビルド後に次を実行します。約2秒間隔の2回目の測定を出力します。

```sh
"build/thermal kun.app/Contents/MacOS/ThermalKun" --diagnose
```

## データソースと表示の意味

| 表示 | 取得元・意味 |
| --- | --- |
| `CPU TEMP` | AppleSMCから読み取れた、機種に対応するCPU温度センサーの算術平均です。対象キー、個別値、集計方法はツールチップと `Settings` → `Measurement Sources` → `Current Sources` で確認できます。 |
| `THERMAL STATUS` | 公開APIの `ProcessInfo.thermalState` による `Nominal`／`Fair`／`Serious`／`Critical`。システムの熱状態を表し、性能低下率ではありません。 |
| `CPU LOAD`／`GPU LOAD` | CPUはMachのCPU時間カウンターの差分から算出します。GPUはドライバーが公開するデバイス使用率で、複数GPUでは取得できた値の最大を表示します。 |
| `MEMORY`／`MEMORY PRESSURE` | Mach VMカウンターによる使用メモリの推定値（GiB）と、カーネルの離散的な圧力状態です。Activity Monitorとは算出方法により差が出る場合があります。使用率から圧力を推測しません。 |

CPU温度はCPU全体の最高温度を保証するものではありません。確認したM3環境では、読み取れた効率コア領域の3センサー平均を `SMC · E-ZONE MEAN` と明示します。センサー領域とCPUコアは必ずしも一対一ではなく、他のアプリがパッケージ温度や最高値を使う場合、表示値は一致しません。

取得に対応しない項目は `Unsupported`、一時的に取得できない項目は `Unavailable` と表示します。失敗値を0や正常状態へ置き換えません。履歴の欠測部分は線を途切れさせ、更新停止・遅延時は `STALE · UPDATES PAUSED` を表示します。スリープ復帰後は監視を再開します。最高温度は起動後、または直前のリセット後の実測値で、アプリ再起動時にリセットされます。

温度と熱状態は別の指標です。`Thermal State` はOSが報告する熱状態で、実際の処理性能を測定した値ではありません。温度の高さだけでサーマルスロットリング発生を断定しません。

## プライバシー・制限

取得処理はローカルで完結します。テレメトリー、クラウド同期、分析SDKは使用しません。監視専用で、電源設定や熱管理設定の変更は実装していません。データ取得はutility優先度の直列バックグラウンドキューで行い、履歴を180サンプルに制限しています。

温度取得には非公開・未文書化のAppleSMCドライバーインターフェースを読み取り専用で使用します。GPUドライバーの統計とメモリプレッシャーのsysctlもOS・機種に依存するため、将来のmacOSでは取得できなくなる可能性があります。未知のセンサーを推測して代用せず、取得できる項目の監視を続けます。実機のログアウト／再ログインによる自動起動確認は未実施です。

## 開発・ライセンス

インストールは [日本語ガイド](docs/INSTALL.ja.md)／[English guide](docs/INSTALL.md)、ビルドとリリースは [docs/RELEASING.md](docs/RELEASING.md)、変更履歴は [CHANGELOG.md](CHANGELOG.md) を参照してください。

MITライセンスです。詳細は [LICENSE](LICENSE) を参照してください。AppleSMCの構造とセンサーキーの調査には [Stats](https://github.com/exelban/stats) を参考にしており、必要な著作権表示と条件は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) に保持しています。Apple公式資料: [ProcessInfo.thermalState](https://developer.apple.com/documentation/foundation/processinfo/thermalstate-swift.property)、[SMAppService](https://developer.apple.com/documentation/servicemanagement/smappservice)。
