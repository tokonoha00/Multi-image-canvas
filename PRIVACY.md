# Privacy Policy

Multi Image Canvas is designed with a strong focus on privacy and local processing.

## Data Collection and Network Usage
- **No Telemetry or Automatic Uploads**: The application does not collect analytics or automatically upload user data, images, sessions, or layout files.
- **Remote Image Loading**: The application performs HTTP(S) requests only when the user explicitly opens an image URL or drags remote image content from a browser. The request is sent to the URL host and may expose standard connection information such as the user's IP address to that host.
- **Local Processing**: Sessions, layouts, exports, and locally opened images are processed on the user's PC.

## Layout Sharing (.mics) and Privacy Features
When exporting layouts for sharing via "Export for Sharing..." (共有用にエクスポート...), the application automatically strips potentially sensitive data:
- **Metadata Stripping**: EXIF, GPS location, and camera/software model details are removed by re-encoding image pixels.
- **Crop Optimization**: Only the visible portions of cropped images are saved; hidden pixels are permanently discarded.
- **Hidden Layers Excluded**: Invisible layers/images are excluded from the exported package.
- **Anonymization**: Source folder paths, PC names, and original file names are not included. Images are renamed to anonymous sequential numbers.

## Security Measures for Importing Layouts
To protect your environment when importing `.mics` shared layouts, the application enforces strict local validation:
- **Zip Slip Prevention**: Path traversal attacks inside zip archives are blocked.
- **Zip Bomb Defense**: Maximum extraction size limits and entry counts are enforced.
- **Type Restriction**: Non-image file extensions inside packages are ignored during extraction.

---

### プライバシーポリシー（日本語）

Multi Image Canvas は、プライバシーの保護とローカル処理を第一に考えて設計されています。

## データ収集とネットワーク利用について
- **テレメトリ・自動アップロードなし**: 利用状況の収集や、ユーザーデータ、画像、セッション、レイアウトファイルの自動アップロードは行いません。
- **外部画像の読み込み**: ユーザーが画像URLを開くか、ブラウザから外部画像をD&Dした場合のみ、そのURLのホストへHTTP(S)リクエストを送信します。この通信では、通常のWebアクセスと同様に接続元IPアドレス等が画像配信元へ伝わる場合があります。
- **ローカル処理**: セッション、レイアウト、エクスポート、ローカル画像はユーザーのPC内で処理します。

## 共有用エクスポート（.mics）におけるプライバシー機能
「共有用にエクスポート...」機能を用いてレイアウトをエクスポートする際、以下の個人情報・機密情報が自動的に除去されます：
- **メタデータ（EXIF情報）の除去**: 位置情報や撮影機器、編集ソフトのメタデータは、画像をピクセルから再エンコードすることで完全に消去されます。
- **非表示エリアのカット**: 画像をトリミング（クロップ）した際、見えなくなっている余白ピクセルはデータに含まれません。
- **非表示レイヤーの除外**: 非表示（Invisible）設定にしている画像・レイヤーはエクスポートデータから除外されます。
- **匿名化処理**: 元画像のファイル名、フォルダパス、PC名などのローカル環境情報は一切含まれません。画像は匿名化された連番ファイル名にリネームされます。

## 共有データ読み込み時の安全対策
他者から受け取った `.mics` ファイルを展開する際、ローカル環境を保護するために以下のセキュリティチェックを行っています：
- **パス・トラバーサル対策（Zip Slip）**: 書庫内の不正な相対パス（`../` 等）によるファイル改ざん攻撃を拒否します。
- **圧縮爆弾（Zip Bomb）対策**: 展開後のデータサイズ制限およびファイル数上限を設定し、リソース枯渇攻撃を防ぎます。
- **拡張子制限**: パッケージ内に画像以外の不正ファイルが混入していても、展開されません。
