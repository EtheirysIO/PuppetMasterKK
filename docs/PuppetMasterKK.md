# PuppetMasterKK guide

PuppetMasterKK lets trusted chat messages boss your character around—wave, pose, perform a short routine, or run another text command you explicitly allow. It can also answer emotes aimed at you, follow (or come to) a player when asked, and copy someone's emotes.

> [!CAUTION]
> A trigger can run text commands on your character. Choose who can set it off, use specific phrases and trusted channels, and allow only the commands you need.

## Install and open

1. Open Dalamud Settings and go to **Experimental**.
2. Add `https://repo.etheirys.io` as a custom plugin repository and save.
3. Install **PuppetMasterKK** from the Plugin Installer.
4. Run `/pmkk` (or `/puppetmasterkk`), or open the plugin from the Plugin Installer.

> [!IMPORTANT]
> There can be only one. PuppetMasterKK replaces the original Puppet Master and Right Back At You. If another puppet-master
> plugin is loaded, every trigger would fire twice, so PuppetMasterKK warns you when it starts: disable or uninstall the
> other one. On its first start, PuppetMasterKK brings over your Puppet Master triggers and settings (from its settings
> file, or the newest backup of it that reads); the old files aren't changed.

The sidebar picks the page:

- **Triggers** — your triggers are listed at the top of the sidebar; pick one to edit it, or **+** to make one.
- **Emote replies** — answer emotes aimed at you.
- **Follow mode** — follow or come to whoever asks (or the player they name), and stop everything on request.
- **Mimic** — copy a player's emotes when asked.
- **Activity** — what's running, waiting and recently finished, counts, Practice mode and Test all triggers.
- **Message log** — capture chat to find channels and build triggers.
- **Settings** — general options, defaults for new triggers, custom channels and appearance.

What's running is also shown at the bottom of the sidebar, with **Cancel**. The chevron in the header (or a double-click on it) folds the window down to a title bar.

## Create your first trigger

Start with a simple wave. Once that works, you can decide how much chaos your friends are allowed to cause.

1. Select **+** next to **Triggers** in the sidebar.
2. Give it a recognizable name, such as `Please do`.
3. Under **Listens for**, keep **Phrase** and enter `please do`.
4. Under **Who can trigger it**, keep **Friends**, **Free Company** and **Party and alliance**, or add specific players.
5. Under **Channels**, select **Pick channels** and choose where it listens.
6. Under **Try it**, enter `please do wave`. The `/wave` line should say **Runs**.
7. Turn it on with the **On** switch in the header.

A new trigger starts off. **Settings → New triggers** holds the defaults copied into triggers created afterward.

## Phrase matching

A trigger's phrase tells PuppetMasterKK which chat messages it answers. Specific phrases are safer and less likely to fire by accident.

### Phrase triggers

The formats are:

```text
<trigger phrase> <command>
<trigger phrase> (<command with arguments>)
```

Examples:

```text
please do wave
please do (wave motion)
```

Do not include the leading `/` in an incoming message. PuppetMasterKK adds it to the generated command. Separate alternative trigger phrases with `|`:

```text
please do|simon says
```

Matching is case-insensitive, and the phrase is plain text: a `.` or `(` in it means exactly that character.

### Commands with arguments

Wrap the entire command and its arguments in parentheses. PuppetMasterKK removes the parentheses and adds the leading `/`.

| Incoming message | Generated command |
| --- | --- |
| `please do wave` | `/wave` |
| `please do (ac Vercure [t])` | `/ac Vercure <t>` |

In incoming messages, write targets with square brackets. PuppetMasterKK turns `[t]`, `[tt]`, `[me]`, `[mo]`, `[f]` and `[1]`–`[8]` into the angle brackets the game needs. Other placeholders, such as `[pos]` and `[flag]`, are left as they are, and angle brackets typed by the sender (`<pos>`) are turned into look-alike characters, so nobody can make you post where you are. Line breaks in a sender's message become spaces, so one message can never turn into several commands.

### Testing

**Try it** shows what a message would run, before anyone sends it:

- **Runs** — the command is allowed.
- **Blocked** — the command won't run; hover it to see why.
- **No match** — the message doesn't match the trigger.

With [Choices](#choices), it also says which choice it picked, and a [final action](#final-action) is listed after the commands. The test runs exactly the matching that live chat uses. To try a message against every trigger at once, or to let triggers run without sending anything, see [Activity](#activity).

<details>
<summary>Advanced matching with regular expressions</summary>

Choose **Regex pattern** when part of the incoming message can change. Text matched by the first pair of parentheses can be reused as `$1`, the second as `$2`, and so on.

Pattern:

```regex
^Random! (.*) rolls? .*?(\d+)\.$
```

Commands to run:

```text
/echo $1 rolled $2
```

Use `^` and `$` when the entire message must match. **Restore defaults** puts back the pattern and commands the phrase mode would use. A pattern that takes too long on a message is stopped after a quarter of a second.

</details>

### Choices

A regex trigger can run one of several sets of commands instead of **Commands to run**. Open **Choices** under **Listens for** and pick how the set is chosen:

- **Random** — one at random each time.
- **In turn** — the next one each time, then it starts over. Only a request that runs or waits moves it on; an ignored one doesn't.
- **By word** — the one whose word is the pattern's first capture (`$1`), case doesn't matter. Any other word doesn't trigger it at all, so a sender can only pick from your list.

Example: pattern `^please (\w+)$`, choice `hug` runs `/hug`, choice `dance` runs `/dance` then `/wait 3`. `please hug` hugs; `please logout` does nothing.

Each choice works like **Commands to run**: one command per line, `$1`, `$2`… and `/wait`. Up to 16 choices.

## Who can trigger it

Each trigger chooses who can set it off:

- **Anyone** — every player who can talk in the picked channels.
- **Friends** — players on your friend list.
- **Free Company** — your FC chat, and FC members the game has listed this session (open the FC member list once). An FC tag alone doesn't count: other FCs can use the same tag.
- **Party and alliance** — your party and alliance, and their chat channels. That includes people who joined from Party Finder.
- **Also these players** — named players, as `Name@World`, or just `Name` for any world (tagged **any world**, since a same-named player on another world counts too).

New triggers start with Friends, Free Company and Party. Triggers from before this option existed were set to **Anyone**, so they keep working as they did; review them. A trigger that **Anyone** can trigger in a public channel (Say, Yell, Shout, Tell, Party, Alliance or a cross-world linkshell) shows a warning, because strangers can trigger it.

Your own chat messages don't trigger anything, unless you turn off **Settings → General → Ignore my own messages**.

## Command rules

Each trigger's **Protections** card decides how much control it gets:

1. Commands under **Blocked** never run.
2. Emotes run unless they're blocked.
3. Other commands must be under **Allowed**, unless **Any game command** is selected.
4. Protected commands only run when listed under **Allowed** (shown as **Also allowed** with **Any game command**). Three switches in the card decide what's protected, and each has ticks for its parts:
   - **Protect chat commands**: Say, Yell, Shout, Tell, Party, Alliance, Free Company, Linkshells (including `/l` for your current one), CWLS (including `/cwl`), Novice Network, PvP Team, emote text (`/em`), dice (`/dice`), random rolls (`/random`) and quick chat (`/quickchat`).
   - **Protect risky game commands**: teleport and return, party commands (join, leave, kick, invite, leader), gear sets and glamour plates, trading, the friend list, blacklist and leaving the Novice Network, your search comment, hotbars, and resets (UI, HUD, chat log, tell history).
   - **Protect plugin commands**: every loaded plugin that has commands. The ones people could cause trouble with (Lifestream, Glamourer, Penumbra, Customize+, Dropbox, vnavmesh and others) are listed first, with the reason; the rest are under **Show other plugins**.

   Untick a part and its commands run without being listed. Switch a whole group off and all of its commands do. Everything starts protected, and **Settings → New triggers** sets what new triggers start with.
5. `/logout`, `/shutdown`, `/follow` (following is what [Follow mode](#follow-mode) is for), `/pmkk` (and `/puppetmasterkk`, and the old `/puppetmaster`) and Dalamud's `/xl…` commands never run.

**Turn off all protections** (in **Protections**, after two confirmations) is for a trigger you trust completely: it then runs every command it's sent, unlisted, including the ones above. Its **Allowed** and **Blocked** lists are switched off too (they're kept for when you turn protections back on). Only `/follow` stays blocked, and the sender filter and the send limit still apply. Turning it back on is one click; a duplicated trigger always starts with protections on.

Rules apply to the command name, not its arguments: for the Vercure example, allow `/ac`. A command's short forms count as the same command, so blocking `/shout` also blocks `/sh`.

**Hide emote text** changes allowed emotes to their `motion` form, which plays the animation without the emote's chat line.

Everything PuppetMasterKK sends goes through one limit: up to three commands at once, then one per second.

## Channels

Pick at least one channel. The picker offers the channels people chat in: **Common** (Say, Yell, Shout, Tell, Party, Cross-world Party, Alliance, Free Company, PvP Team), **CWLS**, **Linkshells**, and your **Custom** channels. Start small; you can always add more. A trigger with no channel can't activate. Public channels are tinted as a reminder that strangers talk there.

<details>
<summary>Discover and use a custom channel</summary>

1. Open **Message log** and turn on **Capture messages**.
2. Cause the desired message in game.
3. Hover the dot at the start of its row to see its channel and number.
4. If the channel is unknown, the **#** button on the row adds it to **Settings → Custom channels**, where you can name it.
5. Pick it for your trigger under **Channels → Pick channels → Custom**.

The **+** button on a row makes a new trigger that matches that exact message and listens only to its channel.

Channel numbers may change after a game or Dalamud update. If a custom channel stops working, use the message log to find its new number and change it in **Settings → Custom channels**; triggers using the old number follow it.

</details>

## Control how triggers run

**Repeats and cooldown** decides whether another matching message is ignored, saved for later, or allowed to interrupt.

### Multiple command steps

With a regex trigger, the commands can be one per line. Every line is checked on its own against the command rules.

```text
/wave
/wait 2
/echo done
```

`/wait` pauses before the next line, for up to 60 seconds, and takes decimals such as `/wait 0.5` (always with a dot). A `/wait` in the trigger's own commands doesn't need to be allowed. One that comes from a sender's message ("please do (wait 60)") only runs if `/wait` is under **Allowed**, so strangers can't keep a trigger busy. Block `/wait` to turn pauses off entirely.

### What running means

PuppetMasterKK knows when it sends a command, but not when the game finishes it. In the example above, it sends `/wave`, waits about two seconds, then sends `/echo done`. The wait doesn't confirm that the wave animation finished.

To keep a trigger busy after its final command, end with a wait:

```text
/wave
/wait 2
```

Cancelling stops unsent lines and current waits, but it can't undo a command already sent or stop an animation already playing.

### Cooldown and repeat behavior

While a trigger is busy, another run of that same trigger can't start. Different triggers can still overlap, unless they're in the same [turn group](#turn-groups).

**Cooldown** is the minimum time between starts. With a 10-second cooldown, a trigger starting at `00:00` can't start again before `00:10`. It must also finish its current run first.

### When another message arrives

| If another message arrives while the trigger is busy… | Choose |
| --- | --- |
| Ignore it | **Ignore** |
| Run every request afterward | **Queue every trigger** |
| Keep only the newest request | **Queue latest trigger** |
| Stop the remaining steps and react again immediately | **Restart immediately** |

Use **Restart immediately** for short triggers that should respond again right away. Avoid it for long multi-line triggers, because a new message stops the remaining lines and cooldown doesn't apply.

### Per-person limits

Also under **Repeats and cooldown**:

- **Cooldown per person** — after someone's request runs or starts waiting, that person can't trigger it again for this long (up to an hour). Other people still can. It applies with every repeat setting, Restart immediately too, so one person can't keep restarting it.
- **One waiting request per person** (Queue every trigger only) — a person's newer request replaces their older one that's still waiting, at the back of the line. Activity shows who each waiting request is from.

People are told apart by `Name@World`. Messages with no player behind them (system and custom channels) all count as the same person. For different commands per person, duplicate the trigger and give each copy its own **Who can trigger it**.

### Final action

**Final action** runs up to five commands after a run finishes on its own: **After every run**, or **When nothing is waiting** (only once the queue is empty). They're sent as written (`$1` isn't filled in), checked by **Protections** like any command, and a `/wait` among them is the trigger's own pause.

It never runs after **Stop**, a restart, turning the trigger off, `/pmkk off` or a stop word, and it stops by itself after 10 seconds. The trigger stays busy while it runs. Practice mode applies to it too.

### Turn groups

**Takes turns with**, at the bottom of **Repeats and cooldown**, puts triggers in a group: pick an existing group, or type a name and press **New group**. Triggers in the same group never run at the same time. When one is running, another member's request is handled by that member's own repeat setting: ignored, or queued behind it.

- Each trigger keeps its own cooldown. A member cooling down doesn't hold the others up, except while its own request is first in line.
- The group shares one line of up to 16 waiting requests, in the order they arrived. When it's full, the oldest is dropped, whichever trigger it belongs to.
- **Queue latest trigger** and **One waiting request per person** only replace the trigger's own waiting requests, never another member's. The newest keeps the trigger's place in line.
- **Restart immediately** only stops the trigger's own run. If another member is running or waiting, the new request takes its turn like **Queue latest trigger**.
- **When nothing is waiting** (final action) means nothing is waiting in the whole group.
- **Stop**, turning a trigger off or changing it only clears that trigger's waiting requests. Changing a trigger's group stops its run and clears its waiting requests.
- Names ignore case and are at most 40 characters. Duplicating a trigger keeps its group. Share codes never carry it: the group names your own triggers, which mean nothing to whoever imports it.

The sidebar shows each trigger's group next to its state, and **All triggers** on the Activity page has a **Group** column.

<details>
<summary>More about cooldowns and waiting requests</summary>

- Cooldown starts when a trigger starts.
- Waiting requests run in the order they arrived, and a new message never jumps ahead of them.
- **Queue every trigger** keeps up to 16 waiting requests per trigger (per turn group, for triggers in one).
- Only a busy trigger saves requests: one that arrives while the trigger is idle but still cooling down is ignored.
- **Restart immediately** stops unsent lines, clears older waiting requests, and starts the newest request without cooldown.
- Turning a trigger off or deleting it stops its current wait and clears its waiting requests. Changing its senders, commands or emote text stops the current run and clears waiting requests; other changes clear waiting requests, and a run already going finishes with the settings it started with.

</details>

### Avoiding trigger loops

A trigger that answers itself can loop forever—funny once, less funny when you can't stop waving. Ignoring your own messages stops the simple case, but two players whose triggers answer each other can still loop. Use cooldowns, and check whether a trigger's commands could produce a message that triggers it (or someone else's) again.

To stop everything at once:

```text
/pmkk off
```

This also clears waiting requests and stops any walk or mimic. **Cancel** in the sidebar stops every trigger and clears what's waiting; **Stop** on the Activity page stops one trigger.

## Follow mode

**Follow mode** lets someone you trust make you follow them, or another player, and stop you again.

- **Call name** is what people call you (`Ami`; several with `|`). **Follow word** (`follow`), **Come word** (`come`) and **Stop word** (`stop`) can be changed, and take several with `|` too, such as `follow|trail`.
- `Ami come` comes to whoever said it and follows them; so does `Ami follow me`. `Ami follow Nova Ral'veth@Exodus` follows that player; `Ami follow Nova` works when only one Nova is nearby. `Ami follow` on its own names nobody, so it's ignored. PuppetMasterKK targets the player, then sends `/follow`.
- **Walk to them with vnavmesh** (on, used when vnavmesh is loaded): when the player is in your zone but more than 20 yalms away, you walk to them first, then target and follow them. Party members can be anywhere in the zone; anyone else has to be close enough to see. The walk rounds off vnavmesh's corners so the run is smooth, picks a new path without stopping when they move, and stops about 2 yalms from them. Requests are ignored during combat (the stop word still works). A walk gives up in combat, after 90 seconds, if they leave the zone, or if it keeps getting stuck, and the stop word ends it.
- **Walk when they walk** (on): while following, you walk when they walk and run when they run, judged by how fast they move. More than 10 yalms behind, you always run to catch up. Your own walk toggle goes back to how it was when you stop following. The game doesn't say when `/follow` ends (say you took the controls), so being more than 15 yalms away for 3 seconds without a vnavmesh walk counts as not following any more.
- Otherwise the player has to be nearby. If they aren't, PuppetMasterKK can **Reply by tell** with your own message, where `<target>` becomes the name they asked for: `uwu I'm sorry master I don't see <target> near me :c`. One reply per person every 10 seconds.
- **Channels** picks where it listens (Tell, Party and Free Company to start). **Who can trigger it** works as it does for triggers, and **Never take requests from** blocks people outright.
- **Who you'll follow**: when **Only follow** has names, only they are followed; **Never follow** always wins.
- `Ami stop` stops every trigger (running or waiting), any walk and any mimic. With **Also stand still** on, it takes one tiny `/automove` step, which ends following, emote loops, sitting and lying down, leaving you standing. **Then also run** adds your own commands after that. However many people say stop, the step and these commands run at most once every 2 seconds.
- A line Follow mode or Mimic takes isn't also matched by your triggers.

## Mimic

**Mimic** (its own page) lets someone you trust make you copy a player's emotes.

- `Ami mimic me` copies whoever said it (it has to come from a player whose world the game shows); `Ami mimic Nova Ral'veth@Exodus` (or `Ami mimic Nova`, when only one Nova is nearby) copies that player. `Ami mimic` on its own names nobody, so it's ignored. `Ami stop` stops mimicking (if Follow mode uses the same call name and stop word, its stop stops everything).
- When they emote at someone, you target the same person and do the same emote; when they emote at you, you emote back at them; when they emote at nobody, you clear your target and emote too. Only emotes are copied, only from a player close enough to see, and not during combat.
- **Call name**, **Mimic word** and **Stop word** are Mimic's own. **Channels**, **Who can trigger it** and **Never take requests from** decide who can start and stop it. **Who you'll mimic** has **Only mimic** and **Never mimic**, for when you're told to mimic someone else.
- **Follow them too** (on to start) targets and follows the player you mimic, walking over with vnavmesh first when Follow mode's **Walk to them with vnavmesh** is on. An emote stops following, so after each copied emote you follow them again a second later. It works even with Follow mode off, and the stop word ends both (with one small step, so you really stop following). Not during combat.
- **Walk when they walk** (on) matches their pace while you follow them, as in Follow mode.
- **Jump when they jump** (on to start) copies their jumps too, while they're close enough to see. Jumps use the same wait before copying and the same repeat guard as emotes, and pause in combat.
- **Wait before copying** (off to start) copies each emote this long after they do it (0.1 to 10 seconds).
- **Skip the same emote repeated quickly** (on, 3 seconds) doesn't copy the same emote again for that long after copying it (plus the wait before copying, if that's on), so two players mimicking each other don't loop. Turn it off to copy every repeat.
- **Hide emote text** is on to start, and **When the player isn't nearby** can reply by tell, as in Follow mode.
- Settings from before Mimic had its own page start as copies of Follow mode's.

## Emote replies

**Emote replies** answers an emote aimed at you with the same emote. This used to be the separate *Right Back At You* plugin; its settings are brought over once. If it's still loaded, PuppetMasterKK warns you to remove it so emotes aren't answered twice.

- **Target them first** targets the player so the emote is aimed back at them.
- **Hide emote text** plays the animation without the chat line.
- **Wait before answering the same player again** (10 seconds by default, at least 3) stops two players who both answer emotes from emoting at each other forever.
- **Never reply with** lists emotes that are never sent back (sitting, lounging and dozing by default, so nobody can make you lie down).
- **Reply with a different emote** answers one emote with another, such as `/dote` → `/joy`. Leave the reply empty to not answer that emote at all. **Never reply with** still applies to the reply.
- Replies pause while you're in combat, so your target never changes mid-fight.
- **Who can trigger it** works as it does for triggers.

If a game update moves the emote function, the page says emote replies are unavailable until PuppetMasterKK is updated.

## Notifications

Under **Settings → General → Notifications**:

- **While a trigger runs and when it ends** shows start, step progress, completion, cancellation and a Cancel button.
- **When a trigger is ignored** occasionally tells you a message was ignored because the trigger was busy or cooling down.

"Completed" means PuppetMasterKK finished sending the configured lines. It doesn't mean the game finished them.

Each trigger can choose **Default**, **Show** or **Hide** for both under **Notifications**. **Default** follows Settings.

## Activity

Open **Activity** from the sidebar, or run:

```text
/pmkk viz
```

It shows what's running (with **Stop**), what's waiting (and who it's from), what recently finished and how long it took, and the state of every trigger. Select a trigger's name to open it in the editor.

### Counts

The counts at the top cover this session; hover one for details.

- **Ignored** — requests dropped because the trigger was busy (with **Ignore**) or cooling down (for everyone, or for that person).
- **Replaced** — waiting requests dropped for a newer one (**Queue latest trigger**, **Restart immediately**, **One waiting request per person**).
- **Discarded** — dropped under load: messages that arrived too fast, and requests over the 16 a trigger (or turn group) can queue. Each is counted against its own trigger.

**All triggers** has an **Ignored** column per trigger; hover it for everything else (started, done, stopped, interrupted, replaced, discarded, blocked lines, pattern timeouts). Under **Recently finished**, **Interrupted** means a newer request restarted it and **Replaced** means it never ran because a newer request took its place; **Stopped** means you (or a change to the trigger) stopped it. **Clear** in the message log, or `/pmkk logging clear`, resets the counts.

### Practice mode

Turn on **Practice mode** at the top of Activity, or run:

```text
/pmkk practice on
/pmkk practice off
```

Triggers match, wait, queue and check their commands exactly as usual, but nothing is sent: runs show a **Practice** tag, and hovering one lists what it would have sent. Blocked lines are still blocked. Follow mode, Mimic and Emote replies keep working for real. Turning it on or off stops every trigger, it's off every time the plugin starts, and the sidebar says when it's on.

### Test all triggers

Type a message, pick a channel and who sends it (a stranger, a friend, a Free Company or party member, and optionally a `Name@World`). Every trigger whose pattern matches is listed with what it would run (and which choice), and whether it would fire: **Fires**, **Off**, **Not listening here** or **Not from this sender**. Nothing is sent.

## Message log

When a trigger mysteriously refuses to work, the message log is usually the best place to look.

- Turn on **Capture messages** to start logging. It's off every time the plugin starts.
- **Color by channel** and **Follow new messages** only affect this session.
- **+** makes a new trigger from a message; **#** adds an unknown channel.
- **Save to file** writes the log to a text file; **Clear** empties it and resets the Activity counts.

Commands:

```text
/pmkk logging on
/pmkk logging off
/pmkk logging clear
/pmkk logging save
```

<details>
<summary>Discarded messages</summary>

During extreme message spam, PuppetMasterKK may discard older messages that are still waiting to be matched, and requests beyond the 16 a trigger can queue. The message log and Activity show how many were discarded. **Clear** resets these and the other Activity counts.

</details>

## Managing triggers and commands

Triggers may share a name. `/pmkk on|off <TriggerName>` affects every trigger with that name (case doesn't matter). `/pmkk off` on its own also stops any walk or mimic.

```text
/pmkk
/pmkk on
/pmkk off
/pmkk on <TriggerName>
/pmkk off <TriggerName>
```

### Share codes

To share a trigger, press the share button next to **Duplicate**. A code starting with `PMKK1.` is copied; paste it anywhere. It carries the trigger's phrase or pattern, commands, choices, final action, repeat and cooldown settings, Allowed and Blocked lists, protections and built-in channels. It never carries who can trigger it (no player names), notifications, custom channels, its turn group, its **Try it** message or whether it's on. Its name, phrase and commands are included exactly as written. A trigger with no protections is shared with them turned on.

To import one, copy the code, then press the import button next to **New trigger**. The code is read from the clipboard only, never from chat. Before anything is added you see its pattern, commands and Allowed list, an empty **Try it** box checked against your own rules, and **Needs your OK**: everything it allows that your **Settings → New triggers** don't, each with its own tick:

- **Any game command**.
- Each allowed chat, risky, plugin or unknown command, and each allowed game command unless your new triggers allow any game command. Emotes, and commands that never run, need no OK.
- `/wait` in the sender's message.
- **Emotes show the sender's text** (emotes not set to motion only), when your new triggers use motion only.
- **Queue every trigger**. Unticked, it uses your new-trigger repeat setting.
- Each protection it turns off (a whole group, a chat channel, a risky group or a plugin).
- Each of your default blocks it drops.

Everything starts unticked, and anything left unticked is left out. The trigger is always added turned off, for your new-trigger senders (never **Anyone**), on your new-trigger channels. The channels it used are offered as unticked extras, with public ones marked. Allowed entries written with look-alike characters (full-width letters, hidden characters, such as `/ｔell`) are shown as "looks like /tell" and never imported; at run time a look-alike only ever runs on a trigger with no protections. Hidden and direction-changing characters are taken out of the name and choice words. A code that asks for no protections, is from a newer version, or is damaged or oversized is refused.

## Appearance

**Settings → Appearance** sets the accent color, the text size and colorblind mode. Everything also follows Dalamud's global UI scale.

## Configuration upgrades and recovery

<details>
<summary>Backups and unreadable settings</summary>

When PuppetMasterKK upgrades settings from an older version, it first saves a dated backup next to them. If the settings file can't be read at all (damaged, or from a newer version), it's copied aside as an `unreadable` backup, PuppetMasterKK starts with defaults, and a notification says where the old file is. If even the copy fails, nothing is saved for the rest of the session, so the file is never overwritten.

</details>

## Troubleshooting

### The test says No match

- Check that the message has the trigger followed by a command.
- Put commands with spaces inside parentheses for a phrase trigger.
- For regex, check the pattern, `^` and `$`, and references such as `$1` or `$2`.

### A command says Blocked

- Hover it to see why.
- It may be under **Blocked**, or always blocked (`/logout`, `/shutdown`, `/follow`, `/pmkk`, `/xl…`).
- Add other commands under **Allowed**. Chat, risky and plugin commands need to be listed unless their protection is switched off.

### The test works but nothing happens in game

- Turn the trigger on.
- Pick the right channel.
- Check **Who can trigger it**: the sender may not be a friend, FC member or party member as far as the game knows.
- Your own messages are ignored unless you turn that off in Settings.
- Check that the trigger isn't busy or cooling down.
- Turn on the message log and check the channel the message really arrives on.
- Check the notifications and the Dalamud plugin log (`/xllog`).

### A trigger runs too often

- Use a longer trigger phrase, or anchor a regex with `^` and `$`.
- Narrow the channels and who can trigger it.
- Add a cooldown.
- Choose **Ignore** or **Queue latest trigger** instead of queueing every trigger.

### A custom-channel trigger stopped working

- Turn on the message log and find the channel's new number.
- Change it in **Settings → Custom channels**.

### Where is the configuration?

Dalamud stores it in the XIVLauncher plugin configuration folder (`pluginConfigs\PuppetMasterKK.json`). Avoid editing it while the game is running.
