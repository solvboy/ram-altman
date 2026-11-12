# Ram Altman for Windows

A single small native program. No installer, nothing to set up.

## Run it

Double-click `RamAltman.exe`. Keep `sam.jpg` in the same folder; swap in any other image with that name to change the portrait.

## Build it yourself

No installs needed. It builds with the C# compiler that ships with Windows.

1. Double-click `build.bat`.
2. Run the `RamAltman.exe` it creates.

The whole program is one file, `RamAltman.cs`, so you can read exactly what it does first.

## Antivirus note

Small unsigned programs that grab a lot of memory can trip machine-learning antivirus heuristics, and Windows Defender has flagged an earlier build as a false positive. If that happens, build it yourself with `build.bat`, or allow it in Windows Security under Protection history.

## What it does

1. Reads how much RAM you have.
2. Slowly fills memory by loading `sam.jpg` over and over, until the machine is at about 55% in use.
3. Stops and asks for consent, with the terms, privacy policy and RAM budget in one table.
4. If you agree, continues to a hard ceiling that always leaves one eighth of your RAM free (never less than 2 GB).

**Backspace** is the panic button: the app is killed instantly and Windows takes every byte back. Closing the window does the same.

It writes nothing to disk or the registry and has no networking code.
