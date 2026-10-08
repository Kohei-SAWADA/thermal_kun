# ビルドとリリース

現在のバージョンは **1.0.4（build 5）** です。ソースは [Kohei-SAWADA/thermal_kun](https://github.com/Kohei-SAWADA/thermal_kun)、配布用ZIPは [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/tag/v1.0.4) で公開しています。この手順では、アプリと配布用ZIPの作成・検証方法を説明します。

利用者向けのインストール手順は [English](INSTALL.md)／[日本語](INSTALL.ja.md)、紹介ページは [English README](../README.md)／[日本語README](../README.ja.md) を参照してください。

## ビルド

Swift 6以降とmacOS SDKを含むXcode Command Line Toolsを用意し、ソースのディレクトリで実行します。確認環境はApple Silicon（M3）、macOS 26.6.2、Swift 6.3.3です。対象OSはmacOS 13以降に設定していますが、Intelを含む他機種・他OSでの動作は未確認です。

```sh
./Scripts/check.sh
./Scripts/build.sh
./Scripts/run.sh
```

ビルド前に、起動中のアプリを `Quit thermal kun` で終了してください。生成先は `build/thermal kun.app` です。現在のMacのアーキテクチャ向けにビルドし、アドホック署名と署名検証を行います。Developer ID署名・Appleの公証・Universal Binaryの生成は含みません。

`Packaging/Info.plist` の `CFBundleShortVersionString` と `CFBundleVersion`、READMEとCHANGELOGのバージョンを揃えてからパッケージを作成してください。

## 配布用ZIP

```sh
./Scripts/package-release.sh
```

`dist/` 以下にローカル配布用ZIPを生成します。正確なファイル名はスクリプトの出力を確認してください。生成後はZIPを別の場所に展開し、`thermal kun.app` が開けることと、同梱のライセンス表示を確認します。この操作だけでは外部公開・アップロードは行いません。

Apple Silicon用の現行ZIP名は `thermal-kun-macOS-arm64-v1.0.4.zip` です。ZIPと `.zip.sha256` ファイルを、対応するバージョンのGitHub Releaseへ添付します。配布用ZIPにはリソースフォークや拡張属性など、ローカル環境のメタデータを含めません。

ZIPには英語の `README.md` と日本語の `README.ja.md`、`docs/INSTALL.md` と `docs/INSTALL.ja.md`、ライセンス、公開用画像を同梱します。言語切り替えリンクと画像が、解凍先でも開けることを確認してください。

継続利用ではアプリを `/Applications` などの安定した保存先へ移し、そこから起動して `Launch at Login` を設定します。ログアウト／再ログインによる確認は、作業中のアプリを閉じるため、この開発では実施していません。

## 動作確認の範囲

`./Scripts/check.sh` はモデル、欠測値、温度履歴の上限、最高温度リセット、設定補正、画面位置復元を検証します。センサーの実測やGUI操作は、別途アプリを起動して確認してください。

- `--diagnose` で取得元、センサーの集計方法、`Unavailable`／`Unsupported` の扱いを確認する
- ドラッグ移動、正方形のサイズ変更、位置・サイズの再起動後復元を確認する
- 詳細／コンパクト、ダーク／ライト、Show／Hide、Settings、Always on Topを確認する
- スリープ復帰と画面構成変更、自動起動は、実施した範囲だけを検証済みとして記録する
- 未対応項目の表示を確認し、未取得値を0や正常状態として扱わない

CPU温度は読み取れたSMCセンサーの算術平均です。確認したM3環境では効率コア領域の3センサー平均であり、CPU全体の最高温度を検証したものではありません。`Thermal State` は `ProcessInfo.thermalState` が報告するOSの熱状態で、性能低下率ではありません。温度と熱状態は別々の指標として確認してください。

## 公開用の画像

起動中のアプリを終了した状態で、次を順に実行します。

```sh
./Scripts/build.sh
./Scripts/export-screenshots.sh
./Scripts/make-assets.sh
```

監視値が更新された実行中の画面を撮り直し、ダーク詳細、ダークコンパクト、ライト詳細と設定画面を用意します。画像を更新した後で `make-assets.sh` を実行し、サムネイルとソーシャルプレビューへ反映します。測定値を描き替えて実測として見せないでください。

| 画像 | 用途・サイズ |
| --- | --- |
| `assets/thermal-kun-thumbnail.png` | README先頭のサムネイル。1280 × 640 px。READMEでは幅100%で表示。 |
| `assets/thermal-kun-icon.png` | 温度計ロゴ。READMEでは幅128 pxで表示。 |
| `assets/thermal-kun-social-preview.png` | ソーシャルプレビュー用。1280 × 640 px。 |
| `assets/screenshots/detail-dark.png` | ダーク詳細表示の実測パネル画像。 |
| `assets/screenshots/compact-dark.png` | ダークコンパクト表示の実測パネル画像。 |
| `assets/screenshots/detail-light.png` | ライト詳細表示の実測パネル画像。 |
| `assets/screenshots/settings.png` | 実行中の設定画面。 |

公開用画像にはユーザー名、メール、他アプリの内容、個人のディレクトリ構成を含めないでください。

## 公開用ファイル

公開対象はソースコード、ビルドスクリプト、英日ドキュメント、ライセンス、公開用画像です。`Evidence/` は実機ログやローカル検証記録を置くためのディレクトリで、`.gitignore` により除外します。個人情報を含み得るログを、READMEから直接参照したり配布物へ同梱したりしないでください。`build/`、`dist/`、`.build/` もソース公開には含めません。公開準備コピーの `release-artifacts/` はローカル配布物の保管先で、ソース公開とは別に扱います。

公開前には、画像リンク、スクリプトの実行権限、バージョン、`LICENSE` と `THIRD_PARTY_NOTICES.md` の同梱を確認します。GitHub公開先とReleaseが実際に作成されるまでは、READMEへダウンロード先のリンクを追加しません。
