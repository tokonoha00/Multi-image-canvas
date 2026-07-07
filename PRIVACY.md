# Privacy Policy

Multi Image Canvas is designed with a strong focus on privacy and offline execution.

## Data Collection and Network Usage
- **No Network Transmission**: This application runs entirely offline. It does not collect, transmit, or share any user data, images, or layout files to external servers.
- **Local Access Only**: The application only accesses local files. It performs HTTP(S) requests ONLY when a user explicitly requests to load an image from a remote URL.

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

Multi Image Canvas は、プライバシーの保護とオフライン動作を第一に考えて設計されています。

## データ収集とネットワーク利用について
- **ネットワーク送信なし**: 本アプリは完全にオフラインで動作します。ユーザーデータ、配置された画像、レイアウト情報等を外部サーバーへ送信・共有することは一切ありません。
- **ローカル完結**: ユーザーが外部のURLから画像を読み込むように明示的に指定した場合を除き、ネットワーク通信を行うことはありません。

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
