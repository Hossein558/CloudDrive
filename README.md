# CloudDrive

> Mount Google Drive (and other cloud storage) as a local drive letter on Windows using **CBFS Connect 2024 .NET Edition**

[![.NET](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com)
[![CBFS Connect](https://img.shields.io/badge/CBFS%20Connect-v24.0.9258-blue)](https://www.callback.com/cbfsconnect)

## Overview

CloudDrive is a C# application that mounts your Google Drive as a local drive (`Z:`) in Windows File Explorer — just like OneDrive or Google Drive's own desktop client, but built from scratch using the CBFS Connect 2024 SDK.

```
[Windows Explorer] → [CBFS Connect Driver] → [CloudDrive.exe] → [Google Drive API v3]
```

## Features (Roadmap)

| Phase | Feature | Status |
|---|---|---|
| 1 | Project setup, CBFS integration, models & interfaces | ✅ Done |
| 2 | Google Drive OAuth 2.0 auth + list files in Z: | 🔄 Next |
| 3 | Read/download files from Z: | ⬜ Planned |
| 4 | Write/upload/delete/rename files via Z: | ⬜ Planned |
| 5 | System Tray UI, auto-start, caching | ⬜ Planned |
| 6 | OneDrive, Dropbox, S3, ArvanCloud support | ⬜ Future |
| 7 | Linux support via FUSE | ⬜ Future |

## Architecture

```
CloudDrive/
├── src/
│   ├── CloudDrive.Core/          # Core library
│   │   ├── VirtualDriveManager.cs     # CBFS event bridge
│   │   ├── CloudProviders/
│   │   │   ├── ICloudProvider.cs      # Multi-cloud interface
│   │   │   └── GoogleDriveProvider.cs # Google Drive implementation
│   │   ├── Auth/
│   │   │   └── GoogleAuthManager.cs   # OAuth 2.0
│   │   └── Models/
│   │       ├── CloudFileItem.cs
│   │       └── DriveConfig.cs
│   └── CloudDrive.App/           # Console application entry point
└── tests/
    └── CloudDrive.Tests/
```

## Requirements

- Windows 10/11 (x64)
- .NET 8.0 SDK
- Administrator privileges (for CBFS driver installation)
- Google Cloud project with Drive API enabled
- `credentials.json` from Google Cloud Console (OAuth 2.0 Desktop App)

## Getting Started

### 1. Google Cloud Setup
1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create a new project → Enable **Google Drive API**
3. Credentials → Create **OAuth 2.0 Client ID** (Desktop Application)
4. Download `credentials.json` → place it next to `CloudDrive.App.exe`

### 2. Build & Run
```bash
dotnet build
dotnet run --project src/CloudDrive.App
```

### 3. First Run
- Browser opens for Google sign-in
- Token is saved locally (no sign-in needed next time)
- Drive `Z:` appears in Windows Explorer 🎉

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `callback.CBFSConnect` | 24.0.9258 | Virtual filesystem driver |
| `Google.Apis.Drive.v3` | latest | Google Drive API client |
| `Google.Apis.Auth` | latest | OAuth 2.0 |
| `Serilog` | 4.x | Structured logging |
