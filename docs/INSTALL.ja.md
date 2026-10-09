<p align="right">
  <a href="INSTALL.md">English</a> |
  <strong>日本語</strong>
</p>

# thermal kunのインストール

thermal kun 1.0.6（build 7）は、macOSネイティブのデスクトップ監視アプリです。対象OSはmacOS 13以降に設定していますが、動作確認はApple SiliconのMacに限ります。Releaseの `arm64` ZIPはApple Silicon用で、Intel Mac用ではありません。

[GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/tag/v1.0.6) から [thermal-kun-macOS-arm64-v1.0.6.zip](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.6/thermal-kun-macOS-arm64-v1.0.6.zip) をダウンロードしてください。同じReleaseに [SHA-256チェックサム](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.6/thermal-kun-macOS-arm64-v1.0.6.zip.sha256) もあります。

## ZIPからインストール

1. FinderでZIPをダブルクリックして解凍します。
2. 展開先のフォルダを開き、`thermal kun.app` を `/Applications` に移動します。
3. アプリケーションからアプリを開きます。
4. メニューバーの温度計アイコンを確認します。パネルが非表示の場合は `Show` を選びます。
5. 上部のアプリ名付近をドラッグして配置し、`Settings…` で表示や更新間隔を選びます。

アプリはアドホック署名付きで、Appleの公証は受けていません。監視処理は利用者の権限でローカル実行し、管理者権限や特権ヘルパーは不要です。

## 初回起動でmacOSに止められる場合

開発元やアプリを確認できないという警告が出た場合は、まず想定した入手元のコピーであることを確認してください。一度アプリを開こうとした後、**システム設定 → プライバシーとセキュリティ** にあるthermal kunの項目で **このまま開く** を選びます。次の確認画面を読み、起動する場合は **開く** を選びます。別の警告が出る場合は [Appleの公式案内](https://support.apple.com/ja-jp/102445) に従ってください。

## ログイン時の自動起動

`/Applications` のアプリを起動してから、`Settings` の `Launch at Login` を有効にします。新規インストール時に一度だけ有効化し、既存のオフ選択は保持します。macOSの承認が必要な場合は、アプリ内の `Open Login Items Settings` から設定を開いて承認します。

この機能を有効にする前に、安定した保存先へ配置してください。実際のログアウト／再ログインによる自動起動確認は未実施です。

## 更新

1. メニューバーの `Quit thermal kun` で終了します。
2. [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/latest) から新しいZIPをダウンロードして解凍します。
3. `/Applications/thermal kun.app` を新しいアプリで置き換えます。
4. アプリケーションから再び開きます。

設定とパネルの配置はアプリ本体とは別に保存するため、置き換えても保持されます。ログイン時起動を使う場合は、同じ保存先を使ってください。

## ソースからビルド

Swift 6以降と、macOS SDKを含むXcode Command Line Toolsが必要です。アプリを終了し、ソースのディレクトリで次を実行します。

```sh
./Scripts/build.sh
./Scripts/run.sh
```

現在のMacのアーキテクチャ向けに `build/thermal kun.app` を生成します。継続利用では `/Applications` へコピーしてください。モデルの検証は `./Scripts/check.sh` で実行できます。

操作と測定値の意味は [日本語README](../README.ja.md)、ローカルZIPの作成は [リリース手順](RELEASING.md) を参照してください。
