# Multi Image Canvas

ゲーム・クリエイティブ作業中に参考画像をオーバーレイ表示するための Windows ネイティブ画像ビュアー。
.NET 8 / WinForms 製。

## ダウンロード

正式リリース初版: **v1.0.0**

GitHub Releases から用途に合わせて取得してください。

- **インストーラー版**: `MultiImageCanvas-1.0.0-Setup.exe`
  - インストール先、スタートメニュー/デスクトップ追加、画像・キャンバス・セッションファイルの関連付け候補追加を選べます。
  - 既定のインストール先は `C:\Program Files\MultiImageCanvas` です。インストール時に管理者権限の確認が表示されます。
- **ポータブルZIP版**: `MultiImageCanvas-1.0.0-portable-win-x64.zip`
  - ZIPを展開して `MultiImageCanvas.exe` を起動します。
  - アプリ本体は展開先から起動できますが、設定・自動保存セッションの既定保存先は `%APPDATA%\MultiImageCanvas` です。
  - 設定も含めて持ち運びたい場合は、環境変数 `MIC_SESSION_DIR` で保存先を任意フォルダに指定してください。

どちらも Windows x64 向けの自己完結ビルドです。通常は別途 .NET Runtime のインストールは不要です。

## 主な機能

### 🎮 ゲーム・作業用最前面オーバーレイ（本アプリのコア機能）
ゲームや制作作業を中断することなく、画面最前面に画像を重ね合わせて表示できるオーバーレイ機能です。
- **フォーカスを奪わない仕様**: オーバーレイ起動中もゲームや別アプリのアクティブ状態を維持し、操作を一切妨げません。
- **クリック透過モード**: 画像部分のマウス操作（クリック、ドラッグなど）を無効化し、下のウィンドウに貫通させることができます。
- **不透明度（透過率）の調整**: `0%`（完全透明）から `100%`（不透明）までスライダーで微調整可能。
- **グローバルホットキー（別アプリ操作中も動作可能）**:
  - `Ctrl + Alt + H`: オーバーレイの表示 / 非表示を瞬時に切り替え
  - `Ctrl + Alt + T`: クリック透過モードの有効化 / 無効化をトグル
  - `Ctrl + Alt + PageUp / PageDown`: オーバーレイの不透明度（透過率）を段階的に増減
- **オーバーレイのアニメーション**: 登場・退場時に「ブロック」「フェード」「スライド」「ワイプ」などのお好みのトランジション効果を設定可能。
- **モダンな外枠**: DWM（Desktop Window Manager）によるWindows 11準拠の美しい角丸およびグラデーション境界線で画像を綺麗に引き締めます。
> ⚠ フルスクリーン**排他**モードのゲーム上にはオーバーレイ表示できません（OSの仕様）。
> ゲーム側を「ボーダレスウィンドウ（仮想フルスクリーン）」等に設定してご利用ください。

### 🎨 無限キャンバスと高度な画像編集
画像を配置して自由にレイアウトを作成できる編集空間です。
- **自由自在な操作**: 複数画像の配置、パン（中ボタン / 右ボタンドラッグ(空白部) / Space+ドラッグ）、およびホイールによる滑らかなズーム（2%〜1600%）。
- **配置・変形**: 比率固定リサイズ、トリミング（クロップ）、自由回転（90°近傍スナップ、Shiftキーで15°刻みスナップ）、左右・上下反転（Ctrl+H / Ctrl+Shift+H）、重ね順（前面/背面）の制御。
- **吸い付き（スナップ）**: 画像の端や中心、グリッド線への自動スナップ機能（Altキーで一時無効化可能）。
- **Undo / Redo (履歴200件)**: `Ctrl + Z` および `Ctrl + Y` による操作履歴の復元。
- **アニメーションGIF・豊富な形式対応**: アニメーションGIFの再生、WebP等の最新フォーマットへの対応、PNG形式でのエクスポート（Ctrl+E、透過背景・回転反映）。

### 📂 ビュアーモードとレイアウト管理
通常の編集ツールとしてだけでなく、軽量な画像ビューアーとしても活用できます。
- **閲覧専用ビューアーモード**: エクスプローラー等から画像を直接開いた際、ツリーや設定などの編集用UIをすべて隠したシンプルな単一画像ビューアーとして起動します。上部の「キャンバスにて編集」ボタンから一瞬で編集レイアウトへ移行できます。
- **マルチタブ管理**: ブラウザのように複数のキャンバスタブを管理（`Ctrl + T` 新規作成 / `Ctrl + W` 閉じる / `Ctrl + Tab` 切り替え / ドラッグによる並び替え）。
- **前回セッションの自動復元**: アプリ終了時の状態（全タブ、画像の配置、ズーム、ウィンドウ位置など）を自動保存し、次回起動時に完璧に復元します。
- **レイアウトの保存と読み込み**: 配置状態を `.micl` (JSON) 形式で保存し（Ctrl+S / Ctrl+O）、いつでも再開できます。

### 🔒 共有用にエクスポート（プライバシー重視・完全オフライン）
他ユーザーへ作成したレイアウトパッケージ（`.mics`）を渡す際、個人情報が漏れないよう防御する独自セキュリティ機能を搭載しています。
- **メタデータ自動除去**: EXIF、GPS位置情報、撮影機器データなどの不要な情報をピクセルから再エンコードすることで完全に消去します。
- **余白カット**: 画像をトリミング（クロップ）した際、見えなくなっている隠しエリアの画像データはエクスポートファイルに含まれません。
- **非表示レイヤーの除外**: 非表示（Invisible）にしている画像はパッケージに含まれません。
- **完全匿名化**: PC名、元のフォルダパス、ファイル名は一切保存されず、画像は匿名化された連番ファイル名にリネームされます。
- **ローカル完結**: 一切のデータを外部サーバー等へ送信しません。

### ⚙ 快適なユーザーインターフェース (UI)
- **Windows 11 Fluent スタイル**: 角丸ウィンドウ、余計な白残りのないモダンなスクロールバー（カスタム描画）。
- **4つのUIテーマ**: ダーク、ライト、ミッドナイト（漆黒）、ハイコントラスト。
- **マルチパネル**: ファイルツリー（OneDrive・PC・ネットワーク対応）、レイヤーリスト、サムネイル一覧（3列グリッド）を即座に切り替え。
- **コントロールメニュー**: 画像を左クリック選択すると、右クリックメニューと同内容のリストが画像の横にフローティング表示。
- **ポータブル＆ファイル関連付け**: レジストリを汚さず動作可能。拡張子とのひもづけ設定機能（設定 → ファイル関連付け）も提供。

## 対応ファイル形式

| 経路 | 形式 |
|---|---|
| GDI+ | PNG / JPEG / BMP / GIF(アニメ再生対応) / TIFF / ICO |
| WIC (OSコーデック) | **WebP** / HEIC / HEIF / AVIF / JXR / DDS ※ |

※ WebPはWindows 11標準。HEIC/AVIFは Microsoft Store の拡張機能インストール環境で表示可能。

## ビルド

必要: .NET 8 SDK

```
cd src
dotnet build                 # 開発ビルド
build_portable_exe.bat       # 配布用単一EXE (自己完結 約63MB)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
                             # ランタイム依存版 (約0.3MB、要 .NET 8 Desktop Runtime)
```

## テスト

```
dotnet test tests\MultiImageCanvas.Tests.csproj   # ユニットテスト (モデル/Undo/シリアライズ)
tools\ui_test.ps1     # 起動+ウィンドウキャプチャ (PrintWindow方式)
tools\pan_test.ps1    # 中ボタンパンのE2Eテスト (PostMessage方式)
tools\perf_test.ps1   # 4K画像での性能計測
tools\make_test_assets.ps1  # テスト画像+セッション生成
```

## 構成

```
src/
  Program.cs        エントリポイント
  MainForm.cs       メインウィンドウ (リボン/タブ/セッション/ホットキー)
  MainForm.Tree.cs  エクスプローラーツリー・パス欄
  CanvasSurface.cs  キャンバス描画・入力 (パン/ズーム/変形/スナップ)
  CanvasModel.cs    ドキュメントモデル・レイアウトJSON入出力
  UndoRedo.cs       コマンドパターンUndo/Redo
  ImageDecoder.cs   GDI+/WICデコード・サムネイル・プレースホルダ
  OverlayForm.cs    オーバーレイウィンドウ
  SidePanels.cs     レイヤーパネル・サムネイルビュー
  Theme.cs          テーマ定義
  UiControls.cs     カスタムコントロール・ヘルプ
  KnownFolders.cs   既知フォルダ取得
  SessionStore.cs   セッション永続化 (%APPDATA%\MultiImageCanvas)
tests/              ユニットテスト
tools/              検証スクリプト
```

## データ保存先

- セッション: `%APPDATA%\MultiImageCanvas\session.json`（終了時＋自動保存。間隔は設定で変更可）
  - 環境変数 `MIC_SESSION_DIR` で保存先を上書き可能（ポータブル運用・テスト隔離用）
- キー割り当ての変更・テーマ・各種設定もセッションに保存
- レイアウトファイル: 任意の場所に `.micl`（JSON）

## ライセンス / License

### ソースコードのライセンス
本プロジェクトのソースコードは MIT License で公開しています。詳細は [LICENSE](./LICENSE) を参照してください。

This project is licensed under the MIT License. See [LICENSE](./LICENSE) for details.

### ブランド・素材について
ソースコードは MIT License で利用できますが、アプリ名（「Multi Image Canvas」）、ロゴ、アイコン、宣伝画像、スクリーンショット等のブランド素材は、明示がない限り自由利用の対象ではありません。公式版と誤認される形での名称・ロゴの使用は避けてください。

The application name, logo, icon, promotional images, screenshots, and other brand assets are not automatically licensed for reuse unless explicitly stated otherwise. Please do not use them in a way that suggests an official or endorsed fork.

### ユーザーコンテンツについて
本アプリは、ユーザーが読み込んだ画像・作成したレイアウト・共有ファイルに対して権利を主張しません。画像込み共有を行う場合、共有者自身が画像の利用権・配布権を確認し、自己の責任において行ってください。

This application does not claim any rights to images, layouts, or files created or imported by users. Users are responsible for ensuring that they have the right to use and share any images included in exported layout packages.

## その他の権利・ポリシー

- プライバシーポリシー（ネットワーク通信なし、共有ファイルの仕様等）については [PRIVACY.md](./PRIVACY.md) を参照してください。
- 貢献方法やPull Requestのルールについては [CONTRIBUTING.md](./CONTRIBUTING.md) を参照してください。
- セキュリティ脆弱性の報告方法については [SECURITY.md](./SECURITY.md) を参照してください。
- 第三者コンポーネントの表示は [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) を参照してください。
