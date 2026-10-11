# Windows 0.1.4-temperature-preview source/distribution verification

Audit date: 2026-10-11 (JST). This audit ran on this Windows PC; prior build and GUI records remain preserved separately in `../Windows/docs/VERIFICATION.md`.

| Check performed in this audit | Result |
|---|---|
| Current Drive ZIP CRC and all 508 manifest file sizes/SHA-256 values | PASS |
| Independent authenticated Drive download SHA-256 versus synced G: copy | PASS — `0c7dd14b3a5ea2d24bac6b2e3b57a8ca46d49632e3609d677f268e56c38d07b6` |
| ZIP EXE, application/Core DLLs and PDBs versus currently installed files | Byte-identical |
| Nested source ZIP CRC; all 52 paths versus latest clean local source | Byte-identical; no missing or extra paths |
| Application/Core DLL CodeView GUID + stamp versus portable PDB | PASS for both assemblies |
| Portable PDB checksums versus supplied source | PASS: all 16 non-generated source documents |
| Source project / package version | 0.1.4-temperature-preview; file version 0.1.4.0 |
| Core checks rerun from extracted source | PASS: 194 |
| Temperature boundary checks rerun | PASS: 12 |
| Windows named-pipe regression rerun | PASS: live framing, disconnect, disposal and unrelated I/O failure |

The original build snapshot exactly matches the clean local source used for the 0.1.4 audit. All 52 files match source commit `5627dccdb48f5e658317ad14c27a12593b4cb81f` after Git line-ending normalization; the Git blobs and build snapshot are not byte-identical where CRLF was normalized to LF. Source/archive identity and compiled-source checksums establish the source relationship; a fresh application rebuild was unnecessary and was not performed. The existing ZIP was not repacked or replaced, and the installed application was not changed. Only test/check tooling was built for this audit.

Current ZIP: `thermal-kun-windows-0.1.4-temperature-preview-x64.zip`, 93,206,008 bytes. Standalone source ZIP: `thermal-kun-windows-0.1.4-source.zip`, 153,281 bytes, SHA-256 `ae04e801e9610267678f0cf952fafa177c64125c4fa8ccf49ee26065575b269a`. The standalone source ZIP preserves the original 52-file snapshot exactly. The repository retains the same compiled source; its download documentation now points to GitHub. `SOURCE-MANIFEST-0.1.4.json` records the original 52 build-snapshot hashes; `AUDIT-2026-10-11-0.1.4.json`, `PDB-SOURCE-0.1.4.json`, and `CHECKS-0.1.4.txt` hold sanitized audit evidence. GitHub publication only changes download documentation in the source tree, not the compiled source or release ZIP.

No GUI/helper launch, driver installation, startup registration, settings modification, OS restart/logout or GitHub publication occurred during this audit. Current automated tests do not validate live sensors or GUI behavior. Prior 0.1.4 records describe GUI, real CPU-temperature display and normal exit on this PC; they were not rerun here. Other PCs, Windows 10/ARM64, multiple monitors, other DPI settings, physical sleep/resume, temperature calibration, login-cycle startup and SmartScreen reputation remain unverified. NuGet vulnerability audit was not part of this source/distribution check.

日本語: 最新ソース全52ファイル、配布ZIP全508項目、DLL/PDBと全16ソースハッシュが一致しました。194コア・12温度境界・Windows実名前付きパイプのチェックも再実行して成功しました。再ビルド不要のため運用中と同じ0.1.4 ZIPを保持しています。今回の自動検証と過去の実画面・実センサー確認は区別しています。
