---
title: Install
nav_order: 3
---


# Installation

Swift Migrator ships as downloadable binaries for:

- Windows (x64)
- macOS (Intel & ARM)
- Linux (x64)

All builds are generated automatically on each GitHub release.


### Downloading Releases
**Download the appropriate ZIP for your operating system** from GitHub Releases:**[GitHub Releases](https://github.com/onas1/swift-migrator/releases)**

>Example structure:
```
migrator-win-x64.zip
migrator-linux-x64.tar.gz
migrator-macos-arm64.tar.gz
```

**Extract the archive** into the root folder of your project — this is where `.env` or `migrator.json` should reside, and where the `migrations/` folder will be generated.  

>Example 

#### Windows

```
YourProject/
├─ .env
├─ migrations/
├─ migrator.exe
```

#### Linux / macOS

```
YourProject/
├─ .env
├─ migrations/
├─ migrator
```

3. **Make the binary executable** (Linux/macOS only):

```bash
chmod +x migrator
```

Run the tool from the project root:

```bash
./migrator apply
```

The tool automatically reads configuration from `.env` or `migrator.json` in the current folder. No additional environment setup is required.

---