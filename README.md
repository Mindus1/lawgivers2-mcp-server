<div align="center">

# MCP Server

Lets AI tools see and drive Lawgivers II, for testing mods.

![Lawgivers II 0.17.2](https://img.shields.io/badge/Lawgivers_II-0.17.2-F28019?style=flat-square)
![BepInEx 6 IL2CPP](https://img.shields.io/badge/BepInEx-6_IL2CPP-4B5563?style=flat-square)
![License: MIT](https://img.shields.io/badge/license-MIT-6B7280?style=flat-square)

</div>

The MCP Server is a mod without anything to see. While the game runs, it answers [MCP](https://modelcontextprotocol.io) requests on `http://127.0.0.1:47120/mcp`, so an AI assistant like Claude Code can look at the screen, click through menus, flip switches, drag sliders, take screenshots and read the log. It's for people who build mods with an AI and want the AI to test them in the real game instead of guessing.

This is what the AI gets back when it opens the party window:

```
> press Party
Pressed the game's Party action
Appeared or changed (13):
UI/Game/Windows/Party/Buttons/ButtonMain [button] "General"
UI/Game/Windows/Party/Buttons/ButtonMembers [button] "Members"
UI/Game/Windows/Party/Buttons/ButtonLaws [button] "Laws"
UI/Game/Windows/Party/Buttons/ButtonMoney [button] "Money"
UI/Game/Windows/Party/Buttons/ButtonRelations [button] "Relations"
UI/Game/Windows/Party/Buttons/ButtonMissions [button] "Promises"
UI/Game/Windows/Party/Buttons/ButtonMessages [button] "Messages"
UI/Game/Windows/Party/Buttons/ButtonStatistics [button] "Statistics"
UI/Game/Windows/Party/Buttons/ButtonActions [button] "Actions"
UI/Game/Windows/Party/Background/Exit [button] "Exit"
UI/Game/Windows/Party/Scroll/Main/Leader/Value [button] "Adam Anderson"
UI/Game/Windows/Party/Scroll/Main/Alliance [button] "Alliance" = CDU-BU
UI/Game/Windows/Party/Scroll/Main/Affiliation [button] "European Union" = European Populaton Party
```

The server only listens on your own PC and turns away requests from web pages.

## Installation

1. Install [BepInEx 6 for IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) (x64, build 790 or newer) and the [Mod Library](https://github.com/Mindus1/lawgivers2-mod-library).
2. Download `McpServer.dll` from [Releases](../../releases) and put it into `Lawgivers II\BepInEx\plugins`.
3. Tell your AI tool about the server. For Claude Code:

```bash
claude mcp add --transport http lawgivers2 http://127.0.0.1:47120/mcp
```

The game has to be running for the tools to work. If you started the game after the AI tool, reconnect it (in Claude Code: `/mcp`).

## Tools

| Tool | What it does |
|---|---|
| `load_save` | Loads a saved world from the title screen or the main menu. |
| `ui_list` | Lists the buttons, switches, sliders and other controls on screen. |
| `ui_click`, `ui_hover`, `ui_drag` | Click, point at or drag things, the way the player would. |
| `ui_set` | Sets a switch, slider, text field, dropdown or the game's own choices. |
| `ui_inspect` | Shows how a window is built: sizes, layouts, images, fonts. |
| `ui_do` | Runs several of those in a row. |
| `press` | Runs one of the game's shortcuts, like ending the turn. |
| `screenshot` | Takes a picture of the game, or of one window. |
| `game_state`, `game_log` | What the game is doing, and the end of the BepInEx log. |

## Documentation

The [wiki](../../wiki) explains how to set it up, what every tool takes and returns, how to make your own mod's UI work with it, and what to watch out for while testing.

## License

[MIT](LICENSE)
