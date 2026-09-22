# LoupixDeck.Plugin.Systemd

Monitors and controls systemd units from a Loupedeck device. It talks to
`org.freedesktop.systemd1` over D-Bus directly — it never runs `systemctl`, never starts `sudo`
and never asks for a password.

Linux only.

## What it does

- Serves both systemd instances: the **user** instance over the session bus and the **system**
  instance over the system bus.
- Runtime actions on a unit: start, stop, restart, reload, toggle and reset-failed. Each one waits
  for the systemd job it created, so a button reports what actually happened rather than that the
  request was accepted.
- Ten touch displays for one unit each: status, name, description, sub state, load state, unit
  file state, uptime, main process, last result and instance. The status button prints the unit
  name above the state; setting its second value to `0` leaves the name out.
- Persistent actions on a unit: enable, disable, mask and unmask. They are off until **Allow
  persistent actions** is switched on, carry "(persistent)" in their name and, by default, run only
  on a second press within three seconds. See [Persistent actions](#persistent-actions).
- Ten favorite slots whose button state follows the unit: `Inactive`, `Activating`, `Active`,
  `Deactivating`, `Reloading`, `Failed`, `NotFound` and `PermissionDenied`.
- A touch folder listing the favorite units with their state and a colour per state.

The command menu lists services by default; sockets, timers, mounts and targets can be added in
the settings. Unit file editing and the journal are deliberately left out.

## Choosing a unit

A unit is stored as `instance:name`, for example `user:pipewire.service` or
`system:sshd.service`. A name without an instance uses the preferred instance from the settings,
and a name without a type suffix is read as a service.

The command menu walks to a unit — instance, type, filter, letter group, unit — and bakes it into
the command. The type level appears only when more than one unit type is listed. A menu entry is
named after its unit, for example `pipewire — Restart`, so a button dropped from it carries the
unit name as its caption. A service drops its `.service` suffix there; other types keep theirs, so
`foo.socket` and `foo` stay apart. Every unit in the menu also offers **Add to
Favorites**, which is how the favorites list is built without typing unit names.

Below an instance the same list appears once per filter — **Failed**, **Active**, **Loaded** and
**All** — each with the number of units behind it. A filter that keeps the same units as **All** is
left out, and when only **All** remains the filter level is skipped entirely. The **Unit search**
setting narrows every one of those lists to the units whose name or description contains the term,
and the instance then reads `User Units — Search: blue` so a short list is never mistaken for a
short machine.

Dropping a command on a touch button writes a plain caption, not a symbol with a label underneath.
The host builds that symbol from the glyph the picker row shows, and a row without an icon of its
own inherits its category's, so the commands declare a glyph outside the host's curated symbol set
instead of none at all.

## Persistent actions

Enable, disable, mask and unmask change the unit file state, so unlike start and stop they survive
a reboot. The plugin treats them apart from the runtime actions:

- **Opt-in.** Off by default, and off for every settings file written before they existed. While
  off, their buttons show `Persistent actions are off` and do nothing, and the command menu hides
  them.
- **Marked.** The command picker lists them as `Enable Unit (persistent)` and so on; the command
  menu puts them in a **Persistent Changes** group below each unit.
- **Confirmed.** With **Confirm with a second press** on (the default), the first press shows
  `Press again to confirm` and only a second press within three seconds runs the action.

After a change the plugin runs a daemon-reload, as `systemctl` does, and the button reports
`Enabled`, `Disabled`, `Masked` or `Unmasked` — or `Already in that state` when systemd had
nothing to write. Enabling a unit without an `[Install]` section also ends there. A unit whose
file lives in `/etc/systemd` or `~/.config/systemd` cannot be masked; systemd refuses that, and
the button shows `Failed`.

## Permissions

User units work as they are. System units usually need a PolicyKit authorization, and this plugin
never asks for one: the call is made without the interactive flag, so PolicyKit answers with a
denial instead of prompting. A denied command shows `PermissionDenied` and leaves the unit's own
state alone — a running unit is never shown as failed only because stopping it was not allowed.

To allow specific system units without a prompt, add a PolicyKit rule on the machine itself. That
is a system decision and stays outside the plugin.

## Build

```bash
dotnet build -c Release
```

Copy `plugin.json`, `LoupixDeck.Plugin.Systemd.dll` and the `strings.*.json` from `bin/Release/`
into `<LoupixDeck>/plugins/systemd/`.

## Settings

| Setting | Meaning |
|---|---|
| Preferred instance | `user` or `system`, used when a unit carries no prefix |
| Show system units | Off never opens the system bus at all |
| Favorite units | The comma list behind the folder and the favorite slots |
| Unit types | Comma list of `service`, `socket`, `timer`, `mount`, `target`; default `service`. With more than one type, the menu groups the units by type |
| Show stopped / unloaded units | What the command menu lists |
| Unit search | Narrows the command menu and the listing to matching names and descriptions |
| Unit filter | What the listing offers: `all`, `loaded`, `active` or `failed` |
| Action when an entry is pressed | `toggle`, `start`, `stop`, `restart`, `reload` or `status` |
| D-Bus timeout | How long one call to systemd may take (250–10000 ms) |
| Command timeout | How long a button waits for its job (1000–120000 ms). The unit keeps going when the wait runs out. |
| Allow persistent actions | Lets enable, disable, mask and unmask run; off by default |
| Confirm with a second press | A persistent action needs a second press within three seconds; on by default |

**List units** prints the units of both instances that pass the search and the filter, with their
state and description, so their names can be copied into the favorites field. **Add listed units
to favorites** adds that same list at once, which is how a search narrowed to a few units replaces
the pick list the SDK has no control for. Both stop after 40 units.
