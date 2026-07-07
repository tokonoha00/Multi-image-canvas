# Third-party notices

Multi Image Canvas uses and/or bundles the following third-party components.

## .NET

- Runtime/framework: .NET 8
- Copyright: .NET Foundation and contributors
- License: MIT
- https://github.com/dotnet/runtime

The installer and portable ZIP are published as self-contained Windows x64 builds, so the required .NET runtime files are included in the generated application package.

## Inno Setup

- Used to build the Windows installer
- Copyright: Jordan Russell and contributors
- License: Inno Setup License / BSD-style license
- https://jrsoftware.org/isinfo.php

The generated installer is produced with Inno Setup. The app itself does not require Inno Setup to run.

## Test-only dependencies

The test project references xUnit and Microsoft.NET.Test.Sdk. These are used only for development and are not required to run the distributed app.

## Notes

- Bootstrap Icons and Google Material Symbols were used as visual references during development for paint-tool icons, but the shipped toolbar icons are drawn in the app code and are not redistributed as external icon files.
- Image codecs for WebP/HEIC/AVIF depend on Windows Imaging Component support and any codecs installed on the user's Windows environment.
