# Changelog

Each release's section is what Dalamud's plugin installer shows. CI publishes the section whose heading matches the tag
(tag `v8.0.0.0` → `## 8.0.0.0`).

## 8.0.0.0

PuppetMasterKK: Puppet Master, rebuilt.

- **Triggers:** phrase or regex triggers with their own channels, senders, cooldowns and repeat behavior. Several command lines with `/wait`, Choices (pick a command block at random, in turn, or by the sender's word), and a Final action after a run.
- **Protections:** emotes run, everything else has to be allowed. Separate switches for chat commands, risky game commands and other plugins' commands. `/logout`, `/shutdown`, `/follow`, `/xl` and look-alike spellings never run.
- **Who can trigger it:** Anyone, friends, your Free Company, your party, or named players, plus per-person cooldowns and one waiting request per person.
- **Follow mode:** "Ami follow me", "Ami come" (walks over with vnavmesh), "Ami stop".
- **Mimic:** "Ami mimic me" copies their emotes, aimed at the same target.
- **Emote replies:** Right Back At You is built in, with "reply with a different emote".
- **Activity:** counts, Practice mode (nothing is sent) and Test all triggers.
- **Share codes:** copy a trigger as a code; importing shows everything risky and strips it unless you tick it.
- **Turn groups:** triggers in a group take turns instead of overlapping.
- Brings over your Puppet Master triggers and Right Back At You settings on first start.
