# Multi Image Canvas

ゲーム・クリエイティブ作業中に参考画像をオーバーレイ表示するための Windows ネイティブ画像ビュアー。
.NET 8 / WinForms 製。

## ダウンロード

最新正式版: **v1.0.3**

GitHub Releases から用途に合わせて取得してください。

- **インストーラー版**: `MultiImageCanvas-1.0.3-Setup.exe`
  - インストール先、スタートメニュー/デスクトップ追加、画像・キャンバス・セッションファイルの関連付け候補追加を選べます。
  - 既定のインストール先は `C:\Program Files\MultiImageCanvas` です。インストール時に管理者権限の確認が表示されます。
- **ポータブルZIP版**: `MultiImageCanvas-1.0.3-portable-win-x64.zip`
  - ZIPを展開して `MultiImageCanvas.exe` を起動します。
  - アプリ本体は展開先から起動できますが、設定・自動保存セッションの既定保存先は `%APPDATA%\MultiImageCanvas` です。
  - 設定も含めて持ち運びたい場合は、環境変数 `MIC_SESSION_DIR` で保存先を任意フォルダに指定してください。

どちらも Windows x64 向けの自己完結ビルドです。通常は別途 .NET Runtime のインストールは不要です。

## 主な機能

### 最前面オーバーレイ表示
他アプリケーションの操作を妨げずに、画像を最前面にオーバーレイ表示する機能です。
- **フォーカス維持**: オーバーレイ起動中も他のウィンドウやゲームのアクティブ状態を維持します。
- **クリック透過モード**: 画像部分のマウス操作（クリック、ドラッグ）を透過させ、背後のウィンドウを操作可能です。また、透明なピクセル部分は設定に関わらず常にクリックが透過されます。
- **不透明度（透過率）調整**: 0% から 100% の間で不透明度を調整可能です。
- **外枠の表示切替**: オーバーレイウィンドウの境界線（枠）の表示・非表示を切り替え可能です。
- **キャンバス別の表示位置**: オーバーレイの位置をキャンバスごとに自動保存し、次回表示時に復元します。初回だけキャンバス右下へ少しずらして表示します。
- **画面キャプチャ除外**: Windowsのキャプチャ保護APIを利用し、OBS等の対応キャプチャソフトへオーバーレイが映り込まないようにします（OS・キャプチャ方式によっては保証されません）。
- **グローバルホットキー**: 別アプリの操作中も以下のショートカットが利用できます。
  - `Ctrl + Alt + H`: オーバーレイ表示 / 非表示
  - `Ctrl + Alt + T`: クリック透過モードの切替
  - `Ctrl + Alt + PageUp / PageDown`: 不透明度の増減
- **アニメーション**: 表示・非表示時のトランジション（ブロック、フェード、スライド、ワイプ等）を設定可能です。
> ⚠ フルスクリーン排他モードのアプリケーション上には表示できません。対象のアプリケーションをボーダレスウィンドウ等に設定してご利用ください。

### キャンバス機能・画像編集
複数画像を自由に配置できるキャンバス機能です。
- **基本操作**: パン（中ボタンドラッグ等）、マウスホイールによるズーム（2%〜1600%）。
- **配置・変形**: 比率固定リサイズ、トリミング、回転（15°・90°スナップ）、左右・上下反転、重ね順の変更。
- **スナップ**: 画像の端・中心、グリッドへのスナップ配置（Altキーで一時無効化可能）。
- **Undo / Redo**: 操作履歴の復元（履歴上限200件）。
- **対応フォーマット**: アニメーションGIFの再生、WebP等の形式に対応。PNG形式での画像エクスポート。

### ビュアーモード・レイアウト管理
通常の画像ビューアーとしての利用や、作業状態の保存・復元が可能です。
- **ビュアーモード**: 画像ファイルを直接開いた際、編集UIを非表示にした単一画像ビューアーとして起動します。上部ボタンから通常の編集画面へ移行できます。
- **圧縮ファイル閲覧**: ZIP / RAR / 7Z / CBZ / CBR / CB7 内の画像をディスクへ展開せずに表示できます。フォルダツリー、検索、選択フォルダ内のページ送りに対応します。
- **タブ管理**: 複数のキャンバスをタブで管理します（新規作成、閉じる、並び替え）。
- **セッションの自動復元**: アプリ終了時のタブ構成、ウィンドウのサイズや位置、ズーム状態などを記憶し、次回起動時に維持・復元します。
- **レイアウトの保存/読込**: キャンバスの状態を `.micl` (JSON) 形式で保存および読み込み可能です。
- **Windows スナップレイアウト対応**: Windows 11 のスナップ機能に対応し、ウィンドウ配置を容易に行えます。
- **起動の高速化**: サムネイル生成をバックグラウンドで行うことで、アプリ起動速度を向上させています。

### プライバシーに配慮した共有機能
レイアウト情報をパッケージ化（`.mics`）して他者と共有する機能を備えています。
- **メタデータ除去**: EXIF、GPS、撮影機器などのメタデータを再エンコード処理によって消去します。
- **余白・非表示レイヤーの除外**: トリミングされた隠し領域や、非表示設定の画像データは出力ファイルから除外されます。
- **匿名化**: PC名、フォルダパス、元ファイル名を含めず、画像名を連番に変換します。
- **ローカル処理**: セッションや共有ファイルはローカルで処理され、テレメトリや自動アップロードは行いません。URL入力またはブラウザからのD&Dで外部画像を読み込む場合のみ、その画像URLへHTTP(S)リクエストを送信します。

### ユーザーインターフェース (UI)
- **テーマ設定**: ダーク、ライト、ミッドナイト、ハイコントラストの4つのテーマを搭載。
- **マルチパネル**: フォルダツリー、レイヤーリスト、サムネイル一覧の表示パネル。
- **既定のアプリ設定**: アプリの登録情報を整え、Windowsの「既定のアプリ」設定画面を直接開けます。最終的な既定アプリの選択はWindows上で行います。

## 対応ファイル形式

| 経路 | 形式 |
|---|---|
| GDI+ | PNG / JPEG / BMP / GIF(アニメ再生対応) / TIFF / ICO |
| WIC (OSコーデック) | **WebP** / HEIC / HEIF / AVIF / JXR / DDS ※ |
| 圧縮ファイル | ZIP / RAR / 7Z / CBZ / CBR / CB7（内部の対応画像を直接閲覧） |

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
- キー割り当ての変更・テーマ・各種設定: `%APPDATA%\MultiImageCanvas\settings.json`（設定画面の適用/OKで保存）
- オーバーレイのキャンバス別位置は自動保存セッションのみに保存し、共有エクスポートや `.micl` には含めません。
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
