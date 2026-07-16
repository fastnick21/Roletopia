# 🎭 Roletopia

Roletopia is a **host-only Among Us mod** inspired by TownOfUs.

## 🎯 Goals

- Only the host needs to install the mod.
- Mod distribution is Steam-only for hosts.
- Vanilla players on mobile and PS5 can join host lobbies.
- Uses a custom Roletopia plugin system for installation/role loading.

## 📥 Installation Guide

### Step 1: Prerequisites

Before installing Roletopia, ensure you have:
- ✅ Among Us (Steam version)
- ✅ .NET Framework 4.7.2 or higher
- ✅ Administrator privileges on your computer
- ✅ At least 500MB free disk space

### Step 2: Download Roletopia Files

Download the two main components:

#### 🎮 Option A: Quick Install (Recommended)
1. Go to the [Releases page](https://github.com/fastnick21/Roletopia/releases)
2. Download the latest **Roletopia-Installer.exe**
3. Keep the bundled `roletopia/` folder next to the installer `.exe`
4. Run the installer and follow the on-screen instructions
5. The installer auto-detects Among Us and installs to `Among Us\Mods\Roletopia\` (or asks you to select the folder if not found)

#### 🔧 Option B: Manual Install

**Download Component 1: Game Code**
- File: `roletopia-game-code.zip`
- Contains the core mod engine and role systems
- Extract to: `%APPDATA%\Among Us\Mods\Roletopia\`

**Download Component 2: Plugin System**
- File: `roletopia-plugin-system.zip`
- Contains the plugin loader and core-roles plugin
- Extract to: `%APPDATA%\Among Us\Mods\Roletopia\plugins\`

### Step 3: Verify Installation

1. Launch Among Us
2. Look for the Roletopia logo in the main menu
3. Click **Mods** → **Roletopia** to configure settings
4. Enable roles you want to use

### Step 4: Create a Roletopia Lobby

1. Click **Online** → **Create Game**
2. In the mod settings, select **Enable Roletopia**
3. Choose your desired roles and game settings
4. Share the code with friends!

### For Vanilla Players (No Installation Needed!)

Vanilla players on **mobile and PS5** can simply:
1. Join a Roletopia lobby using a code shared by the host
2. Play normally - all role logic happens on the host's computer
3. No installation or downloads required! 🎉

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Mod not appearing in menu | Verify .NET Framework 4.7.2+ is installed |
| Among Us won't launch | Try running as Administrator |
| Plugin system error | Delete the plugins folder and reinstall |
| Friends can't join | Ensure your firewall isn't blocking Among Us |

## 🕵️ Roles

### 👨‍💼 Crewmate Roles
These roles work together with other crewmates to find the impostor.

- **🔫 Sheriff** - Can shoot suspected impostors during the game. Eliminates the target instantly.
- **👻 Medium** - Can communicate with dead players during meetings to gather information and clues.
- **🤐 Snitch** - Can see the names of all impostors **after completing all their tasks** (not in meetings).
- **🔧 Engineer** - Can repair sabotages and access vents (but cannot move through them like impostors).
- **🛡️ Guardian** - Can protect other players from being killed by impostors for a limited time.

### ⚖️ Neutral Roles
These roles have their own unique win conditions, separate from crewmates and impostors.

- **🔥 Arsonist** - Wins by dousing all players in gasoline and igniting them for the ultimate victory.
- **🎭 Jester** - Wins by getting voted out during a meeting (opposite of normal gameplay).
- **💻 Hacker** - Can access admin, surveillance, and other systems remotely; wins with crewmates.

### 😈 Impostor Roles
These roles work as impostors with their own special abilities.

- **🥷 Ninja** - Can teleport short distances and leave no trace of kills for stealth gameplay.
- **🗡️ Assassin** - Can perform instant kills and mark targets for assassination from a distance.
- **🐉 Dragon** - Can fly over obstacles and has enhanced movement abilities compared to regular impostors.

## 📁 File Structure

```
roletopia/
├── mod.json                           # Main mod manifest and compatibility contract
├── roles.json                         # Role catalog and configurations
├── game-code/                         # Game code component
│   ├── core-engine.dll               # Main game logic engine
│   ├── role-system.dll               # Role behavior and abilities
│   └── networking.dll                # Server-client communication
└── plugins/                           # Plugin system component
    ├── plugin-loader.dll             # Plugin manager and loader
    └── core-roles.plugin.json        # Default roles plugin manifest
```

## 🔌 Plugin System Architecture

The plugin system allows custom roles and features to be added without modifying the core game code:

1. **Host loads mod** → Plugin system initializes
2. **Plugin manager scans** → Finds all `.plugin.json` files
3. **Plugins activate** → Role behaviors applied server-side
4. **Vanilla clients join** → No modifications needed on their end

## 🤝 Contributing

We welcome contributions! Feel free to:
- Report bugs via [Issues](https://github.com/fastnick21/Roletopia/issues)
- Suggest new roles or features
- Submit pull requests with improvements
- Create custom plugins using our plugin template

## 📜 License

Please see the LICENSE file for details.
