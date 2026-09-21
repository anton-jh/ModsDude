# 11 — Releasing the client

How the client gets from a commit to somebody else's machine, and how an installed copy keeps itself
up to date. The client is packaged with [Velopack](https://velopack.io) and distributed as GitHub
Releases of this (public) repo. **Nothing is code-signed** — see [First install](#first-install).

## Cutting a release

1. Make sure `ModsDude.Client/ModsDude.Client.Wpf/appsettings.Production.json` is committed and
   holds the deployed server's address. The workflow refuses to run without it, or with one that
   still says `localhost`.
2. Push a tag named for the version:

   ```
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. [`.github/workflows/release.yml`](../.github/workflows/release.yml) runs the client tests,
   publishes a self-contained `win-x64` build, packs it with `vpk`, and uploads the result to a
   **draft** GitHub Release.
4. Look at the draft. **Pressing Publish is the decision to ship**: every installed copy finds
   new versions from the published releases of this repo, so a published release is an update for
   everyone who has the app. The workflow never publishes on its own.

The tag is the version (`v1.2.3` or `v1.2.3-beta.1`, a semantic version). Each release is packed as a
small delta over the previous one — an update is typically a few hundred kilobytes, and the full
installer is about 100 MB.

## What gets installed

| | |
| --- | --- |
| Where | `%LocalAppData%\ModsDude.Client` (`current\` is the running version) |
| Shortcuts | Desktop and Start Menu, both named `ModsDude` |
| Elevation | None. Per user, no UAC prompt |
| Runtime | Self-contained; nothing else to install |

**The package id (`ModsDude.Client`) is not the data folder's name (`ModsDude`) on purpose.**
Velopack installs to `%LocalAppData%\<packId>` and an uninstall deletes that folder; the app's own
data — settings, sign-in, logs, and the default content stores — is in `%LocalAppData%\ModsDude`
(see [05 — Client](05-client.md#installs-and-what-each-one-owns)) and must survive an uninstall and
a reinstall.

An uninstall (`Program.RemoveWhatTheAppLeftOutsideItsFolder`) also removes what Velopack does not
know about: the start-with-Windows entry, which would otherwise launch a missing exe at every
sign-in, and the notification identity. It leaves the data alone.

## Updates

`AppUpdater` checks the feed 20 seconds after start and then every four hours, and only in an
installed copy — a debug build has nothing to update and never touches the network for it.

- **Downloads on its own, never restarts on its own.** The app lives in the tray, possibly
  mid-apply. A downloaded version is *announced*: an Info notice in the column (`Restart now`), a
  `Restart to update` item in the tray menu, and a line in Settings.
- **Restarting asks the same question closing does** ("something is still running") and shuts
  down normally, so the tray icon and toasts are put away.
- **A downloaded update is installed when the app next starts as the first instance**, before
  anything is on screen. Velopack's own apply-on-startup is switched off
  (`SetAutoApplyOnStartup(false)`), because it would also fire for a *second* launch while the app is
  running in the tray and replace it without asking. A second launch just brings the window forward.
- A failed check is a state (`Update: Failed` in the log, and Settings), not an error, and is tried
  again on the next round.

The update notice is Info severity and is never a toast.

### Trying an update without publishing one

`Updates:Directory` reads releases from a folder instead of GitHub. Pack two versions into one
folder with `vpk pack`, install the older one with its own `Setup.exe`, and drop an
`appsettings.<Environment>.json` next to the installed exe containing
`{ "Updates": { "Directory": "C:/path/to/the/folder" } }`. Run it under a throwaway environment
(`DOTNET_ENVIRONMENT=Testing`) so it uses its own data folder and identity instead of the real
install's. Note that `Setup.exe` always installs the *latest* version in its folder, so keep a copy of
the older installer before packing the newer one.

## Shortcuts and notifications

Windows will not show an unpackaged app's toasts without a Start Menu shortcut naming its exe and
carrying its app id (see [05 — Client](05-client.md#running-in-the-background)). An installed copy
gets one from the installer; `vpk pack --aumid ModsDude` gives it the app's own identity
(`AppIdentity.Name`), so the shortcut and the running process agree. A copy that is not installed
makes its own shortcut, which is why a debug build leaves a `ModsDude (Development)` entry in the
Start Menu.

## First install

Nothing is signed, so Windows SmartScreen says "Windows protected your PC" the first time somebody
runs the installer. **More info → Run anyway**, once. Updates the app downloads itself do not show it.
Code signing is not available to an individual in the EU, and nothing here depends on it.
