# PuppetMasterKK

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/OWNER/REPO/main/PuppetMasterKK.json
```

PuppetMasterKK lets trusted chat messages boss your character around. A matching message can make your character wave, pose, perform a short routine, or run another text command you explicitly allow.

[Read the setup and usage guide](docs/PuppetMasterKK.md).

## What it can do

- React to simple phrases or messages with changing text.
- Listen only to the chat channels you choose, and only to the people you choose: friends, your Free Company, your party, or named players.
- Allow safe commands and block commands you do not want. Logging out, shutting down and reconfiguring plugins are never allowed.
- Ignore repeated requests, save them for later, keep only the newest, or react again immediately.
- Run several command lines with short waits between them.
- Answer emotes aimed at you with the same emote (formerly the Right Back At You plugin).
- Show incoming messages, what's running and waiting, and recent activity inside the plugin.

## Useful commands

```text
/pmkk
/pmkk on
/pmkk off
/pmkk on <ReactionName>
/pmkk off <ReactionName>
/pmkk viz
```

There can be only one: PuppetMasterKK replaces Puppet Master and Right Back At You, warns you if another puppet-master
plugin is loaded, and brings your Puppet Master reactions over on its first start.

> [!CAUTION]
> PuppetMasterKK can run text commands on your character. Choose who can trigger each reaction, use specific phrases and trusted channels, and allow only the commands you need.

## A brief history

PuppetMasterKK continues DodingDaga's Puppet Master. Puppet Master started as a simple way for friends to sync emotes through chat. One `please dance` message in Free Company chat could make every online FC member using Puppet Master dance together, wherever they were in the game.

Over time, it grew beyond emotes. Reactions gained support for other text commands, several command lines, custom chat channels, and waits between steps.

Today, each reaction can have its own trigger, senders, allowed commands, channels, cooldown, and repeat behavior. Right Back At You's emote replies now live inside PuppetMasterKK too. The message log, notifications, and the Activity page make it easier to set reactions up and see what they are doing.

## What's next

These are ideas, not promises. Plans may change as they are tested.

- Group selected reactions so they take turns instead of overlapping.
- Export a reaction as a share code that another user can review before enabling.
- Preview matches and waiting activity without sending commands to the game.
- Show clearer counts for ignored, replaced, or discarded requests.
- Add different behavior for each person sending requests.
- Let reactions choose from approved alternatives or run a final action when their work is done.

## Building

```text
git clone --recursive <this repository>
dotnet build PuppetMasterKK.sln -c Release
dotnet run --project PuppetMasterKK.Tests -c Release
```

The UI comes from the phys1ksUI kit, compiled in as source: it must sit next to this repository (`..\phys1ksUI`).

| Folder | What's in it |
| --- | --- |
| `PuppetMasterKK` | The plugin: `Chat` (matching, sender and command rules, running reactions), `Config` (settings and upgrades), `Emotes` (emote replies), `Diagnostics` (message log, activity), `UI` (the window). |
| `PuppetMasterKK.Tests` | A console test runner for everything that doesn't need the game, with sample old configs in `TestConfigs`. |
| `lib/ECommons` | ECommons (git submodule). |
| `docs` | The user guide. |
| `bin` | Build output (not in git): `bin\Release\PuppetMasterKK.dll`, and the release zip in `bin\Release\PuppetMasterKK\latest.zip`. |
