<p align="right">
  <strong>English</strong> |
  <a href="INSTALL.ja.md">日本語</a>
</p>

# Install thermal kun

thermal kun 1.0.4 (build 5) is a native macOS desktop monitor. The deployment target is macOS 13 or later, but testing has been limited to an Apple Silicon Mac. The release `arm64` ZIP is for Apple Silicon, not Intel Macs.

Download [thermal-kun-macOS-arm64-v1.0.4.zip](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.4/thermal-kun-macOS-arm64-v1.0.4.zip) from [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/tag/v1.0.4). A [SHA-256 checksum](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/v1.0.4/thermal-kun-macOS-arm64-v1.0.4.zip.sha256) is available alongside the ZIP.

## Install from the ZIP

1. Double-click the ZIP in Finder to extract it.
2. Open the extracted folder and move `thermal kun.app` to `/Applications`.
3. Open the app from Applications.
4. Look for the thermometer icon in the menu bar. If the panel is hidden, choose `Show`.
5. Drag the app-name area to position the panel. Use `Settings…` to change appearance and update interval.

The app uses an ad-hoc signature and has not been notarized by Apple. Monitoring runs locally as your user and requires no administrator access or privileged helper.

## If macOS blocks the first launch

If macOS cannot verify the developer or check the app, first confirm that the copy came from the expected source. After trying to open it, go to **System Settings → Privacy & Security**, find the entry for thermal kun, and select **Open Anyway**. Review the next prompt and choose **Open** if you intend to proceed. For other alerts, follow [Apple's guidance on opening apps safely](https://support.apple.com/en-us/102445).

## Optional: Launch at Login

Open the copy installed in `/Applications`, then enable `Launch at Login` in `Settings`. It is off by default. If macOS requires approval, choose `Open Login Items Settings` in the app and approve the item there.

Use a stable installation location before enabling this option. Automatic startup has not been verified through an actual logout/login cycle.

## Updates

1. Choose `Quit thermal kun` from the menu bar.
2. Download and extract the new ZIP from [GitHub Releases](https://github.com/Kohei-SAWADA/thermal_kun/releases/latest).
3. Replace the old `/Applications/thermal kun.app` with the new app.
4. Reopen it from Applications.

Settings and saved panel placement are stored separately from the app bundle and remain when replacing the app. Keep the same installation location when using Launch at Login.

## Build from source

Source builds require Swift 6 or later and Xcode Command Line Tools with the macOS SDK. Run these commands in the source directory after quitting the app:

```sh
./Scripts/build.sh
./Scripts/run.sh
```

The result is `build/thermal kun.app`, built for the current Mac architecture. Copy it to `/Applications` for ongoing use. Model checks are available with `./Scripts/check.sh`.

See the [README](../README.md) for controls and measurement meanings, or the [release guide (Japanese)](RELEASING.md) for local ZIP preparation.
