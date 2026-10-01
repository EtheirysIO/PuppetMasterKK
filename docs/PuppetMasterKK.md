# PuppetMasterKK guide

PuppetMasterKK lets trusted chat messages boss your character around—wave, pose, perform a short routine, or run another text command you explicitly allow. It can also answer emotes aimed at you with the same emote.

> [!CAUTION]
> A trigger can run text commands on your character. Choose who can set it off, use specific phrases and trusted channels, and allow only the commands you need.

## Install and open

1. Open Dalamud Settings and go to **Experimental**.
2. Add the custom plugin repository from the [README](../README.md).
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
- **Follow mode** — follow whoever asks (or the player they name), and stop everything on request.
- **Activity** — what's running, waiting and recently finished.
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

The test runs exactly the matching that live chat uses.

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

## Who can trigger it

Each trigger chooses who can set it off:

- **Anyone** — every player who can talk in the picked channels.
- **Friends** — players on your friend list.
- **Free Company** — your FC chat, and FC members the game has listed this session (open the FC member list once). An FC tag alone doesn't count: other FCs can use the same tag.
- **Party and alliance** — your party and alliance, and their chat channels.
- **Also these players** — named players, as `Name@World`, or just `Name` for any world.

New triggers start with Friends, Free Company and Party. Triggers from before this option existed were set to **Anyone**, so they keep working as they did; review them. A trigger that **Anyone** can trigger in a public channel (Say, Yell, Shout, Tell, Party, Alliance or a cross-world linkshell) shows a warning, because strangers can trigger it.

Your own chat messages never trigger anything (**Settings → General → Ignore my own messages**).

## Command rules

Each trigger's **Protections** card decides how much control it gets:

1. Commands under **Blocked** never run.
2. Emotes run unless they're blocked.
3. Other commands must be under **Allowed**, unless **Any game command** is selected.
4. Protected commands only run when listed under **Allowed** (shown as **Also allowed** with **Any game command**). Three switches in the card decide what's protected, and each has ticks for its parts:
   - **Protect chat commands**: Say, Yell, Shout, Tell, Party, Alliance, Free Company, Linkshells, CWLS, Novice Network, PvP Team and emote text (`/em`).
   - **Protect risky game commands**: teleport and return, party commands (leave, kick, invite), gear sets and glamour plates, trading, the friend list and blacklist, and hotbars.
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

While a trigger is busy, another run of that same trigger can't start. Different triggers can still overlap.

**Cooldown** is the minimum time between starts. With a 10-second cooldown, a trigger starting at `00:00` can't start again before `00:10`. It must also finish its current run first.

### When another message arrives

| If another message arrives while the trigger is busy… | Choose |
| --- | --- |
| Ignore it | **Ignore** |
| Run every request afterward | **Queue every trigger** |
| Keep only the newest request | **Queue latest trigger** |
| Stop the remaining steps and react again immediately | **Restart immediately** |

Use **Restart immediately** for short triggers that should respond again right away. Avoid it for long multi-line triggers, because a new message stops the remaining lines and cooldown doesn't apply.

<details>
<summary>More about cooldowns and waiting requests</summary>

- Cooldown starts when a trigger starts.
- Waiting requests run in the order they arrived, and a new message never jumps ahead of them.
- **Queue every trigger** keeps up to 16 waiting requests per trigger.
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

This also clears waiting requests. **Cancel** in the sidebar stops every trigger and clears what's waiting; **Stop** on the Activity page stops one trigger.

## Follow mode

**Follow mode** lets someone you trust make you follow them, or another player, and stop you again.

- **Call name** is what people call you (`Ami`; several with `|`). **Follow word** (`follow`), **Come word** (`come`) and **Stop word** (`stop`) can be changed, and take several with `|` too, such as `follow|trail`.
- `Ami come` comes to whoever said it and follows them; so does `Ami follow me`. `Ami follow Nova Ral'veth@Exodus` follows that player; `Ami follow Nova` works when only one Nova is nearby. `Ami follow` on its own names nobody, so it's ignored. PuppetMasterKK targets the player, then sends `/follow`.
- **Walk to them with vnavmesh** (on, used when vnavmesh is loaded): when the player is in your zone but more than 20 yalms away, you walk to them first, then target and follow them. Party members can be anywhere in the zone; anyone else has to be close enough to see. The walk gives up in combat, after 90 seconds, or if they leave the zone, and the stop word ends it.
- Otherwise the player has to be nearby. If they aren't, PuppetMasterKK can **Reply by tell** with your own message, where `<target>` becomes the name they asked for: `uwu I'm sorry master I don't see <target> near me :c`. One reply per person every 10 seconds.
- **Channels** picks where it listens (Tell, Party and Free Company to start). **Who can trigger it** works as it does for triggers, and **Never take requests from** blocks people outright.
- **Who you'll follow**: when **Only follow** has names, only they are followed; **Never follow** always wins.
- `Ami stop` stops every trigger (running or waiting). With **Also stand still** on, it takes one tiny `/automove` step, which ends following, emote loops, sitting and lying down, leaving you standing. **Then run** adds your own commands after that.
- A line Follow mode takes isn't also matched by your triggers.

## Emote replies

**Emote replies** answers an emote aimed at you with the same emote. This used to be the separate *Right Back At You* plugin; its settings are brought over once. If it's still loaded, PuppetMasterKK warns you to remove it so emotes aren't answered twice.

- **Target them first** targets the player so the emote is aimed back at them.
- **Hide emote text** plays the animation without the chat line.
- **Wait before answering the same player again** (10 seconds by default, at least 3) stops two players who both answer emotes from emoting at each other forever.
- **Never answer with** lists emotes that are never copied back (sitting, lounging and dozing by default, so nobody can make you lie down).
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

It shows what's running (with **Stop**), what's waiting, what recently finished and how long it took, and the state of every trigger. Select a trigger's name to open it in the editor.

## Message log

When a trigger mysteriously refuses to work, the message log is usually the best place to look.

- Turn on **Capture messages** to start logging. It's off every time the plugin starts.
- **Color by channel** and **Follow new messages** only affect this session.
- **+** makes a new trigger from a message; **#** adds an unknown channel.
- **Save to file** writes the log to a text file; **Clear** empties it.

Commands:

```text
/pmkk logging on
/pmkk logging off
/pmkk logging clear
/pmkk logging save
```

<details>
<summary>Discarded messages</summary>

During extreme message spam, PuppetMasterKK may discard older messages that are still waiting to be matched, and requests beyond the 16 a trigger can queue. The message log and Activity show how many were discarded.

</details>

## Managing triggers and commands

Triggers may share a name. Name-based commands affect every exact, case-sensitive match.

```text
/pmkk
/pmkk on
/pmkk off
/pmkk on <ReactionName>
/pmkk off <ReactionName>
```

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
- It may be under **Blocked**, or always blocked (`/logout`, `/shutdown`, `/pmkk`, `/xl…`).
- Add other commands under **Allowed**. Chat and plugin commands always need to be listed.

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
