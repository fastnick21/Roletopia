# Roletopia

Roletopia is a **host-only Among Us mod** inspired by TownOfUs.

## Goals

- Only the host needs to install the mod.
- Mod distribution is Steam-only for hosts.
- Vanilla players on mobile and PS5 can join host lobbies.
- Uses a custom Roletopia plugin system for installation/role loading.

## Roles

### Crewmate
- Sheriff
- Medium
- Snitch
- Engineer
- Guardian

### Neutral
- Arsonist
- Jester
- Hacker

### Impostor
- Ninja
- Assasin
- Dragon

## Plugin-based install layout

- `/roletopia/mod.json`: main mod manifest and compatibility contract.
- `/roletopia/roles.json`: role catalog.
- `/roletopia/plugins/core-roles.plugin.json`: custom plugin manifest loaded by the host.

A Steam host installs Roletopia, then the host runtime loads `core-roles.plugin.json` through the custom plugin system and applies role behavior server-side so vanilla clients can still join.
