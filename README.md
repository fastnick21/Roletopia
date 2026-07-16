# 🎭 Roletopia

Roletopia is a **host-only Among Us mod** inspired by TownOfUs.

## 🎯 Goals

- Only the host needs to install the mod.
- Mod distribution is Steam-only for hosts.
- Vanilla players on mobile and PS5 can join host lobbies.
- Uses a custom Roletopia plugin system for installation/role loading.
- Simple one-click installer for easy setup.

## 📥 Installation Guide

### Quick Start (Coming Soon!)

We're building a simple installer that will make setup a breeze:
1. Download `Roletopia-Installer.exe`
2. Run it
3. It automatically detects your Among Us folder and installs the mod
4. Done! 🎉

### Manual Installation (For Now)

Until the installer is ready, here's how to manually install:

1. **Extract Files**
   - Extract all mod files to: `%APPDATA%\Among Us\Mods\Roletopia\`
   - This folder may not exist yet - create it if needed

2. **Verify Installation**
   - Launch Among Us
   - Look for the Roletopia mod in the mods menu
   - Enable the roles you want to use

3. **Create a Roletopia Lobby**
   - Click **Online** → **Create Game**
   - Select Roletopia from available mods
   - Choose your desired roles and settings
   - Share the code with friends!

### For Vanilla Players (No Installation Needed!)

Vanilla players on **mobile and PS5** can simply:
1. Join a Roletopia lobby using a code shared by the host
2. Play normally - all role logic happens on the host's computer
3. No installation or downloads required! 🎉

### Prerequisites

Before installing Roletopia, ensure you have:
- ✅ Among Us (Steam version)
- ✅ .NET Framework 4.7.2 or higher
- ✅ Administrator privileges on your computer
- ✅ At least 500MB free disk space

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Mod not appearing in menu | Verify .NET Framework 4.7.2+ is installed |
| Among Us won't launch | Try running as Administrator |
| Plugin system error | Delete the mods folder and reinstall |
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

## 📁 Project Structure

```
Roletopia/
├── README.md                                    # This file
├── Roletopia.sln                               # Visual Studio Solution
├── src/                                        # Source code
│   ├── Roletopia.CoreEngine/                  # Main game logic
│   ├── Roletopia.RoleSystem/                  # Role behaviors and abilities
│   ├── Roletopia.Networking/                  # Server-client communication
│   └── Roletopia.PluginLoader/                # Plugin system manager
└── roletopia/                                  # Compiled output
    └── game-code/                              # DLL files and resources
```

## 🔌 Plugin System Architecture

The plugin system allows custom roles and features to be added without modifying the core game code:

1. **Host loads mod** → Plugin system initializes
2. **Plugin manager scans** → Finds all available plugins
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
