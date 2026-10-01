# PuppetMasterKK

<img src="PuppetMasterKK/images/icon.png" alt="PuppetMasterKK" width="128" align="right" />

Let your friends boss your character around, safely.

PuppetMasterKK watches the chat channels you pick. When someone you trust says the right thing, your character waves, dances, poses, runs a little routine, follows them, walks over to them, or copies their emotes. Everything else stays locked down.

## Install

1. Open Dalamud Settings → **Experimental**.
2. Add this custom plugin repository and save:

   ```text
   https://repo.etheirys.io
   ```

3. Install **PuppetMasterKK** from the Plugin Installer, then type `/pmkk`.

[Read the full setup and usage guide](docs/PuppetMasterKK.md).

> [!CAUTION]
> PuppetMasterKK runs text commands on your character. Choose who can trigger each rule, use specific phrases and trusted channels, and allow only the commands you need.

## Features

### Triggers

- **Phrase triggers:** `please do wave` runs `/wave`; `please do (ac Vercure [t])` runs `/ac Vercure <t>`.
- **Regex triggers:** text captured from the message (`$1`, `$2`) goes into your commands. Several command lines, with `/wait` pauses of up to 60 seconds between them.
- **Who and where:** each trigger picks its channels (Say, Tell, Party, FC, linkshells, CWLS, custom channels) and who can set it off (Anyone, Friends, your Free Company, your party and alliance, or named players).
- **Repeats and cooldowns:** a message that arrives while a trigger is busy can be ignored, queued, kept as the newest only, or restart the trigger right away.
- **Per-person limits:** a cooldown for each person, and one waiting request per person.
- **Choices:** run one of several sets of commands, at random, in turn, or picked by a word from your list.
- **Final action:** up to five commands after a run finishes, such as a closing emote.
- **Share codes:** copy a trigger as a code. Importing one shows everything it would allow; nothing risky comes in unless you tick it, and it arrives turned off.
- **Try it:** type a message in the editor and see exactly what it would run (and which choice), and why anything is blocked.

### Protections

- **Locked down by default:** emotes run; anything else has to be allowed.
- **Three protection switches per trigger, each with its own parts:**
  - **Chat commands:** Say, Shout, Tell, Party, FC, linkshells, CWLS, dice, random, quick chat and more.
  - **Risky game commands:** teleporting, party commands, gear sets, trading, the blacklist, search comment, hotbars and UI resets.
  - **Plugin commands:** any installed plugin that has commands. The ones that could cause chaos are listed first: Lifestream, Glamourer, Penumbra, Customize+, Dropbox, vnavmesh and others.
- **Allowed and Blocked lists** for anything else, with defaults for new triggers.
- **Never runs:** `/logout`, `/shutdown`, `/follow` (that's Follow mode's job), Dalamud's `/xl` commands and PuppetMasterKK's own commands. Look-alike spellings with full-width letters or hidden characters are caught too.
- **No protections:** a trigger you trust completely can run anything except `/follow`. Turning it on takes two confirmations.
- **Safety nets:**
  - Senders can't break a message into extra command lines, post your position, or pause a trigger for longer than you set.
  - Everything PuppetMasterKK sends shares one rate limit.

### Follow mode

- `Ami follow me`, `Ami follow Nova`, or `Ami follow Nova Ral'veth@Exodus` targets that player and follows them.
- `Ami come` walks to whoever said it and follows them.
- When they're more than 20 yalms away in the same zone, PuppetMasterKK walks there with vnavmesh first. Party members can be anywhere in the zone. The path's corners are rounded off so the run looks natural, and it stops right beside them.
- `Ami stop` stops everything: triggers, walking and mimicking. It can also take one step so you stand up, and run your own extra commands.
- Only and Never lists for who you'll follow; a block list for who can ask; an optional "I don't see them" tell when the player isn't nearby.

### Mimic

- `Ami mimic me` and you copy their emotes until `Ami stop`.
- If they emote at someone, you emote at the same person. If they emote at you, you emote back at them.
- Its own call name, channels, who can ask, and Only and Never lists for who you'll mimic.
- An optional delay before copying, and a repeat guard so two players mimicking each other never loop.

### Emote replies

- Answer emotes aimed at you with the same emote (this used to be the Right Back At You plugin).
- Reply with a different emote, such as `/dote` → `/joy`, or not at all.
- A Never reply with list (sitting and lying down by default), a per-player wait so two repliers don't loop, and no replies during combat.

### Seeing what's going on

- **Activity:** what's running, what's waiting, what finished and how long it took, with Stop buttons, plus counts of ignored, replaced and discarded requests.
- **Practice mode:** triggers match, wait and queue as usual but send nothing; Activity shows what they would have sent.
- **Test all triggers:** type a message, pick a channel and a sender, and see every trigger it would set off.
- **Message log:** capture chat to find a channel's number, add custom channels, or turn a message into a trigger with one click.
- **Notifications** for trigger progress and ignored requests, per trigger or globally.

### Everything else

- **Moving in:** brings over your Puppet Master triggers and Right Back At You settings on first start, and warns you if either is still loaded. Settings from older versions are upgraded with a dated backup first.
- **Looks:** accent color, text size and a colorblind mode.

## Commands

```text
/pmkk                     open the window (also /puppetmasterkk)
/pmkk on                  turn every trigger on
/pmkk off                 turn every trigger off, and stop any walk or mimic
/pmkk on <TriggerName>    turn triggers with that name on
/pmkk off <TriggerName>   turn triggers with that name off
/pmkk viz                 open Activity
/pmkk practice on|off     practice mode: triggers run but send nothing
/pmkk logging on|off|clear|save
```

## A brief history

PuppetMasterKK continues DodingDaga's Puppet Master. Puppet Master started as a simple way for friends to sync emotes through chat. One `please dance` message in Free Company chat could make every online FC member using Puppet Master dance together, wherever they were in the game.

Over time it grew beyond emotes: other text commands, several command lines, custom chat channels and waits between steps. PuppetMasterKK adds protections built for strangers in public chat, per-trigger senders and channels, Follow mode with vnavmesh walking, Mimic, and Right Back At You's emote replies, all in one window.

## What's next

These are ideas, not promises. Plans may change as they are tested.

- Group selected triggers so they take turns instead of overlapping.

## Building

```text
git clone https://github.com/NCC-Lykos/PuppetMaster
dotnet build PuppetMasterKK.sln -c Release
dotnet run --project PuppetMasterKK.Tests -c Release
```

The UI comes from the phys1ksUI kit, compiled in as source: it must sit next to this repository (`..\phys1ksUI`).

| Folder | What's in it |
| --- | --- |
| `PuppetMasterKK` | The plugin: `Chat` (matching, sender and command rules, protections, running triggers), `Config` (settings and upgrades), `Emotes` (emote replies), `Follow` (Follow mode, Mimic, walking with vnavmesh), `Diagnostics` (message log, activity), `UI` (the window). |
| `PuppetMasterKK.Tests` | A console test runner for everything that doesn't need the game, including hostile-input tests, with sample old and broken configs in `TestConfigs`. |
| `docs` | The user guide. |
| `bin` | Build output (not in git): `bin\Release\PuppetMasterKK.dll`, and the release zip in `bin\Release\PuppetMasterKK\latest.zip`. |
