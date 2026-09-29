# Folder Move Protector

Prompts for confirmation before a **folder** (never a plain file) is moved
into or out of a configured set of protected directories, via Windows
Explorer. Includes a small GUI so the protected-folder list can be changed
without recompiling anything.

## Project layout

```
FolderMoveProtector/
  Hook/                          The Explorer copy-hook (a COM DLL)
    FolderMoveProtector.Hook.csproj
    ICopyHook.cs                 Managed declaration of Windows' ICopyHookW
    FolderMoveHook.cs            The COM class Explorer actually calls
    ProtectedRootsStore.cs       Shared config logic (see below)
  Settings/                      The configuration GUI (a normal .exe)
    FolderMoveProtector.Settings.csproj
    Program.cs
    SettingsForm.cs
    app.manifest                 Requests admin elevation automatically
  Setup/                         A standalone installer .exe
    FolderMoveProtector.Setup.csproj
    Program.cs                    Copies files, registers COM, adds shortcut
    app.manifest
  Install.ps1                     Script-based install (for later GPO use)
  Uninstall.ps1
  Package.ps1                     Gathers build output for hand-off to a test machine
```

This replaces the earlier single-project version. If you previously
registered that one on your VM, run its old `Unregister.ps1` first (or just
snapshot back to a clean VM state) before installing this one.

## Two ways to install - use Setup.exe for now

There are now two independent ways to get this onto a machine, and they don't
interfere with each other - same end state either way:

- **`Setup.exe`** (in the `Setup` project) - a compiled program you copy over
  and double-click. This is what the rest of this section covers, and it's
  the one to use for testing on your VM right now.
- **`Install.ps1`** - the original PowerShell script. Keep this around for
  later, once you're ready to push out via Group Policy as a startup script -
  it's the better fit there since GPO can run a `.ps1` directly with no
  separate "send this .exe over" step.

## Building and testing on your VM

1. On your main computer, build all three projects (**Release**, **x64**):
   `Hook`, `Settings`, and `Setup`.
2. Run the packaging script, which gathers just what a destination machine
   needs into one folder:
   ```powershell
   .\Package.ps1
   ```
   This creates `.\dist\` containing exactly three files:
   `FolderMoveProtector.Hook.dll`, `FolderMoveProtector.Settings.exe`, and
   `Setup.exe`. Nothing else needs to go with it - not the source code, not
   the rest of the build output.
3. Zip `dist\` (or copy the folder as-is) to the VM.
4. On the VM, double-click `Setup.exe`. Windows will show a UAC prompt (the
   manifest requests elevation automatically) - approve it. It copies the
   two files into `C:\Program Files\Folder Move Protector`, registers the
   COM hook, and adds a "Folder Move Protector" Start Menu shortcut.
5. Restart Explorer once: `Stop-Process -Name explorer -Force; Start-Process explorer`
6. Launch "Folder Move Protector" from the Start Menu to add protected folders.

To remove it again: `Setup.exe /uninstall` (also needs to run elevated).

Setup.exe deliberately doesn't call `RegAsm.exe` at all - it writes the exact
same registry entries `RegAsm /codebase` would have written, directly, using
.NET's own registry APIs. That removes the dependency on locating
`RegAsm.exe` on the destination machine, which is one less thing that can go
wrong when you're just handing someone a folder of three files.

## How the two projects share configuration

`ProtectedRootsStore.cs` lives in the Hook project and holds the entire
protected-folder list as a single multi-string registry value under
`HKEY_LOCAL_MACHINE\SOFTWARE\Folder Move Protector`. Both programs go through
it:

- **FolderMoveHook** (running inside `explorer.exe`) calls
  `IsUnderAnyProtectedRoot()` on every move to decide whether to prompt.
- **Settings.exe** (the GUI) calls `GetProtectedRoots()` /
  `SetProtectedRoots()` to display and edit the same list.

The Settings project references the Hook project directly
(`<ProjectReference>` in its `.csproj`) - a completely ordinary .NET assembly
reference, nothing to do with COM. COM only matters for the one class,
`FolderMoveHook`, that Explorer specifically loads; everything else in that
project is just a normal class library as far as the Settings app is
concerned.

Because the hook reads the registry fresh on every single move rather than
caching the list at startup, **a change saved in the GUI applies immediately
to every user on the machine** - no Explorer restart, no logoff, nothing
else to do.

## The GUI (`SettingsForm.cs`)

A plain WinForms window, built entirely in code (no `.resx`/designer file,
so it's easy to read as text - you can still open it in Visual Studio's
designer if you'd rather work visually). It has:

- A list box showing the current protected folders.
- A text box plus **Browse...** button (standard folder picker) to add one.
- **Remove Selected** and **Save** buttons.

One deliberate behavior: whatever folder you add - typed or browsed to -
gets run through the same `ResolveMappedDriveToUnc()` used by the hook
itself before being stored. If an admin browses to a mapped drive like
`Z:\Faxes`, what actually gets saved is the real UNC path
(`\\server\share\Faxes`). That matters because different users can map the
same share to different letters (or not map it at all) - storing the
drive-letter form would only work correctly for the one person who happened
to have that exact mapping.

The manifest (`app.manifest`) sets `requireAdministrator`, so Windows shows
the UAC prompt the moment the app launches, rather than the save silently
failing partway through with an access-denied error.

## Building

Open both `.csproj` files in Visual Studio (or open the `FolderMoveProtector`
folder and let VS generate a solution). Set configuration to **Release**,
platform to **x64**, and build both projects.

## Installing

From an elevated PowerShell prompt, from the `FolderMoveProtector` folder:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install.ps1
```

This copies everything to `C:\Program Files\Folder Move Protector`,
registers the hook, and adds a **"Folder Move Protector"** shortcut to the
Start Menu for every user on the machine. Then restart Explorer once:

```powershell
Stop-Process -Name explorer -Force; Start-Process explorer
```

Launch the Start Menu shortcut (as an admin) to add your first protected
folder(s).

## About "running at startup"

Worth being precise about what actually needs to run, because the two
pieces behave differently:

- **The hook itself needs nothing added for startup.** It isn't a program
  that runs continuously - it's a DLL that Explorer loads on demand, and it
  becomes active for every user the moment `Install.ps1` registers it
  machine-wide (`HKEY_LOCAL_MACHINE`). Any user logging into this machine
  afterward - now or in six months, with no further setup - gets the
  protection automatically. There's no service, tray icon, or startup entry
  to manage.
- **The Settings GUI is not something you'd want auto-starting for every
  user**, and here's the concrete reason: it's manifest-elevated, so
  launching it always triggers a UAC prompt. If it auto-started for every
  account at login, your regular fax-processing staff - who by design
  shouldn't have (and likely can't satisfy) admin rights - would either see
  a UAC prompt they can't get past, or the app would just fail to start.
  The Start Menu shortcut `Install.ps1` creates is the right amount of
  access: reachable by anyone, but only actually usable by an admin.

If you later decide you *do* want the Settings shortcut to auto-launch for
every user regardless (e.g. so an admin sees it immediately after logging
into a fresh machine), the mechanism is dropping a shortcut into the
all-users Startup folder:
`C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp`. I'd hold off
on that unless you hit a concrete reason for it, given the UAC caveat above.

## Testing

- Add a folder under a UNC path you control as a protected root via the
  GUI, click Save.
- Drag that folder (or a folder inside it) somewhere else - dialog appears.
- Drag some unrelated folder into it - dialog appears too (source OR
  destination match triggers it).
- Move a folder entirely outside every protected root - no dialog.
- Move a single file anywhere - no dialog.
- Remove a folder from the list and Save - moves involving it stop
  prompting immediately.

## Uninstalling

```powershell
.\Uninstall.ps1
Stop-Process -Name explorer -Force; Start-Process explorer
```

Add `-RemoveConfiguration` to also delete the saved protected-folder list;
without it, the registry entry is left in place in case you reinstall later.

## Troubleshooting

- **Save fails / GUI won't start properly.** Confirm you're running it
  elevated - right-click the Start Menu shortcut isn't enough by itself if
  UAC is disabled entirely on the machine; the manifest triggers the
  standard UAC flow, so it needs UAC to be functioning normally.
- **No dialog appears at all after installing.** Restart Explorer (or sign
  out/in) - it only reads `CopyHookHandlers` at its own startup. Also
  confirm both projects were built as **x64**.
- **Dialog doesn't fire for a folder you added.** Check what path Explorer
  is actually reporting versus what's saved - a DFS namespace can resolve to
  a different real server name than the one users see, for instance. You can
  temporarily log `pszSrcFile`/`pszDestFile` from inside `CopyCallback`'s
  `catch` block to see the exact strings Explorer is sending.
