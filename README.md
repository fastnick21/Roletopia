# 🎭 Roletopia

Roletopia is a **host-only Among Us mod** inspired by TownOfUs.

## 🎯 Goals

- Only the host needs to install the mod.
- Mod distribution is Steam-only for hosts.
- Vanilla players on mobile and PS5 can join host lobbies.
- Uses a custom Roletopia plugin system for installation/role loading.

## 📖 Installation Guide

### For Hosts (Steam)

1. **Download Roletopia** from the Steam Workshop or [releases page](https://github.com/fastnick21/Roletopia/releases)
2. **Subscribe to the mod** in Steam Workshop (if available) or manually install to your Among Us mods folder
3. **Launch Among Us** - the mod will initialize automatically
4. **Create a lobby** and enable Roletopia in the mod settings
5. **Share your lobby link** with friends - vanilla players can join!

### For Vanilla Players

Simply join a Roletopia-enabled lobby hosted by a friend with the mod installed. No installation required! 🎉

### Requirements

- Among Us (Steam version for hosts)
- .NET Framework 4.7.2+
- Administrator permissions for initial installation

## 🕵️ Roles

### 👨‍💼 Crewmate Roles
These roles work together with other crewmates to find the impostor.

- **🔫 Sheriff** - Can shoot suspected impostors during the game
- **👻 Medium** - Can communicate with dead players to gather information
- **🤐 Snitch** - Reveals the names of impostors to all players at the end of each meeting
- **🔧 Engineer** - Can repair sabotages and access vents (but cannot move through them like impostors)
- **🛡️ Guardian** - Can protect other players from being killed by impostors

### ⚖️ Neutral Roles
These roles have their own unique win conditions, separate from crewmates and impostors.

- **🔥 Arsonist** - Wins by dousing all players in gasoline and igniting them
- **🎭 Jester** - Wins by getting voted out during a meeting
- **💻 Hacker** - Can access admin, surveillance, and other systems remotely; wins with crewmates

### 😈 Impostor Roles
These roles work as impostors with their own special abilities.

- **🥷 Ninja** - Can teleport short distances and leave no trace of kills
- **🗡️ Assassin** - Can perform instant kills and mark targets for assassination
- **🐉 Dragon** - Can fly over obstacles and has enhanced abilities compared to regular impostors

## 📁 Plugin-based Install Layout

- `/roletopia/mod.json`: main mod manifest and compatibility contract.
- `/roletopia/roles.json`: role catalog.
- `/roletopia/plugins/core-roles.plugin.json`: custom plugin manifest loaded by the host.

A Steam host installs Roletopia, then the host runtime loads `core-roles.plugin.json` through the custom plugin system and applies role behavior server-side so vanilla clients can still join.

## 🤝 Contributing

We welcome contributions! Feel free to:
- Report bugs via [Issues](https://github.com/fastnick21/Roletopia/issues)
- Suggest new roles or features
- Submit pull requests with improvements

## 📜 License

Please see the LICENSE file for details.
