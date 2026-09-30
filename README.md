# Folder Move Protector

Prompts for confirmation before a **folder** (not a plain file) is moved
into, out of, or within a configured set of protected directories, via
Windows Explorer. Includes a small GUI so the protected-folder list can be
changed without recompiling anything, and a standalone installer.

For anyone looking to just run the executable, the project is compiled into 
the /dist folder. If you want to build the project for yourself, download
this main branch and follow the readme file's instructions.

## Folder layout

Organize the files exactly like this before building - each `.csproj` needs
its own `.cs` files sitting alongside it:

```
FolderMoveProtector/
  Hook/
    FolderMoveProtector.Hook.csproj
    ICopyHook.cs                  Managed declaration of Windows' ICopyHookW
    FolderMoveHook.cs             The COM class Explorer actually calls
    ProtectedRootsStore.cs        Shared config logic (see below)
  Settings/
    FolderMoveProtector.Settings.csproj
    Program.cs                    namespace FolderMoveProtector.Settings
    SettingsForm.cs
    app.manifest                  name="FolderMoveProtector.Settings.app"
  Setup/
    FolderMoveProtector.Setup.csproj
    Program.cs                    namespace FolderMoveProtector.Setup
    app.manifest                  name="FolderMoveProtector.Setup.app"
  Install.ps1                     Script-based install (for later GPO use)
  Uninstall.ps1
  Package.ps1                     Gathers build output for hand-off to a test machine
  README.md
```

Two pairs of files share the same name (`Program.cs` and `app.manifest` each
appear twice) - if you downloaded them individually, check the first line of
each before placing it, using the notes in the column above to tell them
apart.

## How the pieces fit together

- **Hook** compiles to `FolderMoveProtector.Hook.dll` - the actual Explorer
  copy-hook, a COM component Explorer loads on demand whenever a folder move
  happens. It calls `ProtectedRootsStore.IsUnderAnyProtectedRoot()` on both
  the source and destination path of every move, and only then shows the
  confirmation dialog.
- **Settings** compiles to `FolderMoveProtector.Settings.exe` - a small
  WinForms GUI, requiring admin (via its manifest), for viewing/adding/
  removing protected folders. It references the Hook project directly as a
  normal .NET assembly (nothing to do with COM) so both programs read and
  write the exact same registry-backed list via `ProtectedRootsStore`.
  Because the hook re-reads that registry value on every single move rather
  than caching it, a change saved here applies immediately, with no Explorer
  restart needed.
- **Setup** compiles to `Setup.exe` - the installer. It expects to sit next
  to the built `FolderMoveProtector.Hook.dll` and `FolderMoveProtector.Settings.exe`
  (which `Package.ps1` arranges for you), and when run:
  1. Copies both files to `C:\Program Files\Folder Move Protector`
  2. Strips the "downloaded from the internet" marker Windows may have
     tagged them with (see the callout below)
  3. Registers the hook by calling `RegAsm.exe` (found at its fixed path
     under the .NET Framework install, not a separate download)
  4. Adds the Explorer `CopyHookHandlers` registry entry
  5. Creates a Start Menu shortcut to the Settings GUI

`Install.ps1` / `Uninstall.ps1` do the same job as `Setup.exe` /
`Setup.exe /uninstall`, as a PowerShell script instead of a compiled
program. Keep them for later - once you're ready to push this out via Group
Policy as a startup script, a `.ps1` is the better fit there (GPO runs it
directly, no separate "send this .exe over" step). For hand-testing on a VM
right now, use `Setup.exe`.

## About the "downloaded from the internet" marker

Windows tags files that arrived through a browser download (including a
GitHub zip) with a hidden marker, and a tagged .NET assembly can fail to
load with a confusing "file not found"-style error despite the file being
completely intact. `Setup.exe` now strips this marker itself from both files
before registering anything (a no-op if the marker was never there), so this
shouldn't bite you again regardless of how the files traveled to a given
machine. Nothing extra to do on your end for this specific issue - it's
handled inside `Setup.exe` now.

(To be clear on what actually caused the failure you hit before: it turned
out to be an unrelated bug in `Setup.exe`'s own registration code, already
fixed by having it call `RegAsm.exe` - the marker was checked and ruled out
at the time. This is separate, added hardening for a real risk that just
hadn't come up yet.)

## Building (from VS Code's integrated terminal)

You don't need Visual Studio's toolbar/Debug-Release dropdowns for any of
this - the `dotnet` CLI does the same thing:

```powershell
cd Settings
dotnet build -c Release -p:Platform=x64
```
(This also builds Hook automatically, since Settings references it.)

```powershell
cd ..\Setup
dotnet build -c Release -p:Platform=x64
```

Because both projects target `x64` explicitly (not `AnyCPU`), MSBuild's
output lands in `bin\x64\Release\net48\` under each project folder, not the
`bin\Release\net48\` you'd see without a platform set - worth knowing if you
ever go looking for the files by hand.

## Packaging and moving to a test machine

From the `FolderMoveProtector` root folder:

```powershell
.\Package.ps1
```

This gathers exactly three files - `FolderMoveProtector.Hook.dll`,
`FolderMoveProtector.Settings.exe`, and `Setup.exe` - into a new `dist\`
folder. That's the entire hand-off: zip `dist\` (or copy it as-is) to the
test machine. None of the source code or other build artifacts need to go
along.

## Installing on the test machine

1. Double-click `Setup.exe`. Approve the UAC prompt (its manifest requests
   elevation automatically).
2. Restart Explorer once, so it picks up the new registration:
   ```powershell
   Stop-Process -Name explorer -Force; Start-Process explorer
   ```
3. Open **Folder Move Protector** from the Start Menu (as an admin) to add
   your protected folder(s), then **Save**.

To remove it again: run `Setup.exe /uninstall` (also needs to run elevated).

## The Settings GUI

- List box of current protected folders, a text box + **Browse...** button
  to add one, **Remove Selected**, and **Save**.
- Whatever you add - typed or browsed to - is run through the same
  drive-letter-to-UNC resolution the hook itself uses, before being stored.
  If you browse to a mapped drive like `Z:\Faxes`, what actually gets saved
  is the real network path (`\\server\share\Faxes`) - this matters because
  different users can map the same share to different letters (or not map
  it at all), so storing the drive-letter form would only be correct for
  whoever happened to have that exact mapping.
- Prefer typing/browsing to the real `\\server\share\...` path directly
  rather than a mapped drive letter where you can - since Settings.exe runs
  elevated, it's sometimes unable to see drive letters mapped in your
  regular, non-elevated login session, which can otherwise trigger a "this
  doesn't look like a network path" warning unnecessarily.

## What actually enforces this, and where

The hook has no running process at all - it's a passive DLL Explorer loads
into itself on demand, with no separate service, tray icon, or startup entry
of any kind. Registering it under `HKEY_LOCAL_MACHINE` makes it active for
**every** user account on that one machine, immediately and permanently,
with nothing further to configure per-user or per-login. It only affects
machines it's actually been installed on - a second computer that has never
run `Setup.exe`/`Install.ps1` has no code path anywhere that knows this
feature exists, regardless of what shared folder it's browsing to.

## Testing checklist

- Add a real folder as a protected root via the GUI, click **Save**.
- Drag that folder (or something inside it) elsewhere - prompt appears.
- Drag some unrelated folder into it - prompt appears too (source OR
  destination match triggers it).
- Move a folder entirely outside every protected root - no prompt.
- Move a single **file** anywhere, including between two protected folders -
  no prompt.
- Remove a folder from the list, **Save**, then retry a move that used to
  prompt - should go through immediately, no Explorer restart needed.
- For the real test of readiness: try the whole flow (`Setup.exe` only, no
  manual `RegAsm` or registry steps) on a **clean VM snapshot** - that's the
  closest thing to what an actual end-user machine will experience.

## Troubleshooting

- **No prompt at all after installing.** Restart Explorer (or sign out/in) -
  it only reads the `CopyHookHandlers` registration at its own startup.
  Confirm both projects were built as **x64**.
- **COM activation itself is the suspect** (prompt never appears even after
  an Explorer restart). From a regular, non-elevated PowerShell window,
  run:
  ```powershell
  New-Object -ComObject "FolderMoveProtector.Hook.FolderMoveHook"
  ```
  Silent success means activation works fine and the issue is elsewhere
  (e.g. the protected-root path just doesn't match). A red error here is
  much more informative than Explorer's own silent failure mode - Explorer
  simply lets a move through with no prompt and no error if the hook fails
  to load, so this direct test is the fastest way to tell those two
  situations apart.
- **Dialog doesn't fire for a folder you added.** Check what path Explorer
  is actually reporting versus what's saved - a DFS namespace can resolve to
  a different real server name than the one users see, for instance. You
  can temporarily log `pszSrcFile`/`pszDestFile` from inside
  `CopyCallback`'s `catch` block in `FolderMoveHook.cs` to see the exact
  strings Explorer is sending.
- **Settings GUI won't save.** Confirm it's actually running elevated - the
  manifest triggers the standard UAC flow automatically, so this usually
  means UAC itself is disabled or misconfigured on that machine.
