# PuppetMasterKK guide

PuppetMasterKK lets trusted chat messages boss your character around—wave, pose, perform a short routine, or run another text command you explicitly allow. It can also answer emotes aimed at you with the same emote.

> [!CAUTION]
> A reaction can run text commands on your character. Choose who can trigger it, use specific triggers and trusted channels, and allow only the commands you need.

## Install and open

1. Open Dalamud Settings and go to **Experimental**.
2. Add the custom plugin repository from the [README](../README.md).
3. Install **PuppetMasterKK** from the Plugin Installer.
4. Run `/pmkk` (or `/puppetmasterkk`), or open the plugin from the Plugin Installer.

> [!IMPORTANT]
> There can be only one. PuppetMasterKK replaces the original Puppet Master and Right Back At You. If another puppet-master
> plugin is loaded, every trigger would fire twice, so PuppetMasterKK warns you when it starts: disable or uninstall the
> other one. On its first start, PuppetMasterKK brings over your Puppet Master reactions and settings; the old file isn't
> changed.

The sidebar picks the page:

- **Reactions** — your reactions are listed at the top of the sidebar; pick one to edit it, or **+** to make one.
- **Emote replies** — answer emotes aimed at you.
- **Activity** — what's running, waiting and recently finished.
- **Message log** — capture chat to find channels and build reactions.
- **Settings** — general options, defaults for new reactions, custom channels and appearance.

What's running is also shown at the bottom of the sidebar, with **Cancel**. The chevron in the header (or a double-click on it) folds the window down to a title bar.

## Create your first reaction

Start with a simple wave. Once that works, you can decide how much chaos your friends are allowed to cause.

1. Select **+** next to **Reactions** in the sidebar.
2. Give it a recognizable name, such as `Please do`.
3. Under **Trigger**, keep **Phrase** and enter `please do`.
4. Under **Who can trigger it**, keep **Friends**, **Free Company** and **Party and alliance**, or add specific players.
5. Under **Channels**, select **Pick channels** and choose where it listens.
6. Under **Try it**, enter `please do wave`. The `/wave` line should say **Runs**.
7. Turn it on with the **On** switch in the header.

A new reaction starts off. **Settings → New reactions** holds the defaults copied into reactions created afterward.

## Trigger matching

A trigger tells PuppetMasterKK which chat messages deserve a reaction. Specific phrases are safer and less likely to fire by accident.

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

In incoming messages, write targets with square brackets. PuppetMasterKK turns `[t]`, `[tt]`, `[me]`, `[mo]`, `[f]` and `[1]`–`[8]` into the angle brackets the game needs. Other placeholders, such as `[pos]` and `[flag]`, are left as they are, so nobody can make you post where you are.

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

Each reaction chooses who can set it off:

- **Anyone** — every player who can talk in the picked channels.
- **Friends** — players on your friend list.
- **Free Company** — your FC chat, and FC members the game has listed this session (open the FC member list once). An FC tag alone doesn't count: other FCs can use the same tag.
- **Party and alliance** — your party and alliance, and their chat channels.
- **Also these players** — named players, as `Name@World`, or just `Name` for any world.

New reactions start with Friends, Free Company and Party. Reactions from before this option existed were set to **Anyone**, so they keep working as they did; review them. A reaction that **Anyone** can trigger from Say, Shout, Yell, Tell, Party or Novice Network shows a warning, because strangers can trigger it.

Your own chat lines never trigger reactions (**Settings → General → Ignore my own messages**).

## Command rules

**Commands** decides how much control a reaction gets:

1. Commands under **Blocked** never run.
2. Emotes run unless they're blocked.
3. Other commands must be under **Allowed**, unless **Any game command** is selected.
4. Chat commands (say, shout, tell, party, FC, linkshells, `/em`…) and other plugins' commands only run when listed under **Allowed** (shown as **Also allowed** with **Any game command**).
5. `/logout`, `/shutdown`, `/pmkk` and Dalamud's `/xl…` commands never run.

Rules apply to the command name, not its arguments: for the Vercure example, allow `/ac`. A command's short forms count as the same command, so blocking `/shout` also blocks `/sh`.

**Hide emote text** changes allowed emotes to their `motion` form, which plays the animation without the emote's chat line.

Everything PuppetMasterKK sends goes through one limit: up to three commands at once, then one per second.

## Channels

Pick at least one channel. Start small; you can always add more. A reaction with no channel can't activate. Public channels are tinted as a reminder that strangers talk there.

<details>
<summary>Discover and use a custom channel</summary>

1. Open **Message log** and turn on **Capture messages**.
2. Cause the desired message in game.
3. Hover the dot at the start of its row to see its channel and number.
4. If the channel is unknown, the **#** button on the row adds it to **Settings → Custom channels**, where you can name it.
5. Pick it for your reaction under **Channels → Pick channels → Custom**.

The **+** button on a row makes a new reaction that matches that exact message and listens only to its channel.

Channel numbers may change after a game or Dalamud update. If a custom channel stops working, use the message log to find its new number and change it in **Settings → Custom channels**; reactions using the old number follow it.

</details>

## Control how reactions run

**Repeats and cooldown** decides whether another matching message is ignored, saved for later, or allowed to interrupt.

### Multiple command steps

With a regex trigger, the commands can be one per line. Every line is checked on its own against the command rules.

```text
/wave
/wait 2
/echo done
```

`/wait` pauses before the next line, for up to 60 seconds, and takes decimals such as `/wait 0.5` (always with a dot). It doesn't need to be allowed; block `/wait` to turn pauses off.

### What running means

PuppetMasterKK knows when it sends a command, but not when the game finishes it. In the example above, it sends `/wave`, waits about two seconds, then sends `/echo done`. The wait doesn't confirm that the wave animation finished.

To keep a reaction busy after its final command, end with a wait:

```text
/wave
/wait 2
```

Cancelling stops unsent lines and current waits, but it can't undo a command already sent or stop an animation already playing.

### Cooldown and repeat behavior

While a reaction is busy, another run of that same reaction can't start. Different reactions can still overlap.

**Cooldown** is the minimum time between starts. With a 10-second cooldown, a reaction starting at `00:00` can't start again before `00:10`. It must also finish its current run first.

### When another message arrives

| If another message arrives while the reaction is busy… | Choose |
| --- | --- |
| Ignore it | **Ignore** |
| Run every request afterward | **Queue every trigger** |
| Keep only the newest request | **Queue latest trigger** |
| Stop the remaining steps and react again immediately | **Restart immediately** |

Use **Restart immediately** for short reactions that should respond again right away. Avoid it for long multi-line reactions, because a new message stops the remaining lines and cooldown doesn't apply.

<details>
<summary>More about cooldowns and waiting requests</summary>

- Cooldown starts when a reaction starts.
- Waiting requests run in the order they arrived, and a new message never jumps ahead of them.
- **Queue every trigger** keeps up to 16 waiting requests per reaction.
- **Restart immediately** stops unsent lines, clears older waiting requests, and starts the newest request without cooldown.
- Turning a reaction off or deleting it stops its current wait and clears its waiting requests. Changing its settings clears waiting requests; a run already going may finish with the settings it started with.

</details>

### Avoiding reaction loops

A reaction that answers itself can loop forever—funny once, less funny when you can't stop waving. Ignoring your own messages stops the simple case, but two players whose reactions answer each other can still loop. Use cooldowns, and check whether a reaction's commands could produce a message that triggers it (or someone else's) again.

To stop everything at once:

```text
/pmkk off
```

This also clears waiting requests. **Cancel** in the sidebar stops what's running now; **Stop** on the Activity page stops one reaction.

## Emote replies

**Emote replies** answers an emote aimed at you with the same emote. This used to be the separate *Right Back At You* plugin; its settings are brought over once, and you'll be reminded to remove it so emotes aren't answered twice.

- **Target them first** targets the player so the emote is aimed back at them.
- **Hide emote text** plays the animation without the chat line.
- **Wait before answering the same player again** (10 seconds by default) stops two players who both answer emotes from emoting at each other forever.
- **Who can trigger it** works as it does for reactions.

If a game update moves the emote function, the page says emote replies are unavailable until PuppetMasterKK is updated.

## Notifications

Under **Settings → General → Notifications**:

- **While a reaction runs and when it ends** shows start, step progress, completion, cancellation and a Cancel button.
- **When a trigger is ignored** occasionally tells you a message was ignored because the reaction was busy or cooling down.

"Completed" means PuppetMasterKK finished sending the configured lines. It doesn't mean the game finished them.

Each reaction can choose **Default**, **Show** or **Hide** for both under **Notifications**. **Default** follows Settings.

## Activity

Open **Activity** from the sidebar, or run:

```text
/pmkk viz
```

It shows what's running (with **Stop**), what's waiting, what recently finished and how long it took, and the state of every reaction. Select a reaction's name to open it in the editor.

## Message log

When a trigger mysteriously refuses to work, the message log is usually the best place to look.

- Turn on **Capture messages** to start logging. It's off every time the plugin starts.
- **Color by channel** and **Follow new messages** only affect this session.
- **+** makes a new reaction from a message; **#** adds an unknown channel.
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

During extreme message spam, PuppetMasterKK may discard older messages that are still waiting to be matched, and requests beyond the 16 a reaction can queue. The message log and Activity show how many were discarded.

</details>

## Managing reactions and commands

Reactions may share a name. Name-based commands affect every exact, case-sensitive match.

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

- Turn the reaction on.
- Pick the right channel.
- Check **Who can trigger it**: the sender may not be a friend, FC member or party member as far as the game knows.
- Your own messages are ignored unless you turn that off in Settings.
- Check that the reaction isn't busy or cooling down.
- Turn on the message log and check the channel the message really arrives on.
- Check the notifications and the Dalamud plugin log (`/xllog`).

### A reaction runs too often

- Use a longer trigger phrase, or anchor a regex with `^` and `$`.
- Narrow the channels and who can trigger it.
- Add a cooldown.
- Choose **Ignore** or **Queue latest trigger** instead of queueing every trigger.

### A custom-channel reaction stopped working

- Turn on the message log and find the channel's new number.
- Change it in **Settings → Custom channels**.

### Where is the configuration?

Dalamud stores it in the XIVLauncher plugin configuration folder (`pluginConfigs\PuppetMaster.json`). Avoid editing it while the game is running.
