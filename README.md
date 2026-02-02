# 🎮 Save Manager for Playnite

**[English]** | [中文](README_CN.md)

A powerful **save backup plugin** designed for Playnite game library. Easily backup, restore, and manage PC game saves - never worry about losing your progress again!

## ✨ Features

- **⚡ Smart Path Detection**: Automatically detects and adapts save paths when game directory moves
- **📦 One-Click Backup**: Quick backup with optional notes (e.g., "Before Boss Fight", "Chapter 10 Complete")
- **↩️ Safe Restore**: Restore saves to any previous state with one click
- **🚫 Restore Exclusions**: Exclude specific files (e.g., global settings, read text logs, graphics settings) during restoration
- **📂 Portable Design**: Backup files can move with game directory when using relative paths
- **📤 Import/Export**: Import/export save path configurations and external ZIP backups
- **🌍 Global Management**: Export/import all configurations and backups for easy migration
- **🧹 Auto Cleanup**: Automatically removes empty folders when deleting the last backup

## 📸 Screenshots

### Main Interface
![Main Interface](doc/img/Main_en.png)

### Context Menu
![Context Menu](doc/img/Menu_en.png)

## 📖 Quick Start

### 1. Configure Save Paths

1. Right-click a game → **Save Manager** → **Save Management**
2. Click **"📁 Add Folder"** or **"📄 Add File"**
3. Select your game save location (usually `Documents\My Games\GameName` or `Save` folder in game directory)
   - *The plugin automatically detects and optimizes paths*

![Save Path Configuration](doc/img/Main_en.png)

### 2. Create Backup

- **Method A**: Click **"📦 Create Backup"** in the manager interface
- **Method B**: Right-click game in Playnite → **Save Manager** → **Quick Backup**

### 3. Restore Saves

1. In the manager interface, select a backup from the list
2. Click **"↩️ Restore"** button
3. Confirm to restore

Or simply use the context menu:
- Right-click game → **Save Manager** → **Restore Backup** → Select from up to 9 recent backups

### 4. Restore Exclusions (Advanced)

If you wish to **keep** certain local settings when restoring a save (e.g., resolution, key bindings, Visual Novel "read text" flags, global progress files):

1. Find the **"🚫 Restore Exclusions"** section at the bottom left of the manager interface
2. Click **"📁 Add Folder"** or **"📄 Add File"**
3. Select the files you want to protect (e.g., `config.ini`, `global.dat`, `system.sav`)
4. Any subsequent restoration will preserve these files, ensuring your current settings are not overwritten by the backup.

### 5. Other Features

- **Edit Notes**: Click **"✏️"** button to edit backup description
- **Import/Export Config**: Use **"📥/📤"** buttons to share configurations  
- **Global Management**: Go to **Playnite Settings** → **Extensions** → **Save Manager** to export/import all data or open the data folder

## 🛠️ Installation

### From Playnite Add-ons Browser (Recommended)
1. Open Playnite → Press `F9` or go to **Extensions** → **Addons Browser**
2. Search for **"Save Manager"**
3. Click **Install**
4. Restart Playnite

### Manual Installation
1. Download the latest `.pext` file from [Releases](../../releases)
2. Drag and drop into Playnite window or install via **Extensions** → **Install from File**
3. Restart Playnite

## 🌐 Localization

The plugin supports multiple languages:
- **English** (en_US)
- **简体中文** (zh_CN)

Language automatically switches based on Playnite settings.

## ⚙️ Settings

Go to **Playnite Settings** → **Extensions** → **Save Manager**:

- **Custom Backup Directory**: Set a custom location for backups (default: plugin data folder)
- **Auto Backup**: Automatically create backup when game stops
- **Max Backup Count**: Maximum backups per game (0 = unlimited)
- **Data Management**: Export/import full ZIP packages containing both configs and backup files

## 📁 File Structure

```
%AppData%\Playnite\ExtensionsData\SaveManager\
├── config.json       # All game save path configurations and backup records
├── settings.json     # Plugin global settings (auto-backup, etc.)
└── Backups\          # Backup data storage
    └── {GameId}\
        └── Backup_YYYYMMDD_HHMMSS.zip
```

## 🤝 Contributing

Contributions are welcome! Feel free to:
- Report bugs or suggest features in [Issues](../../issues)
- Submit pull requests
- Help translate to other languages

## 📄 License

This project is licensed under the MIT License.

---

*Made with ❤️ for Playnite Gamers*
