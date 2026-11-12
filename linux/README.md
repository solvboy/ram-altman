# Ram Altman for Linux

The same app as the Windows version, as one executable Python script. Nothing to build or install.

## Run it

```bash
chmod +x ram-altman
```

```bash
./ram-altman
```

`python3 ram-altman` works too. Keep `sam.png` in the same folder; swap in any other PNG or GIF with that name to change the portrait.

## Requirements

- A graphical desktop (X11 or Wayland).
- Python 3.8 or newer with tkinter. Most desktops already have it. If you see "needs tkinter":
  - Debian, Ubuntu, Mint: `sudo apt install python3-tk`
  - Fedora: `sudo dnf install python3-tkinter`
  - Arch: `sudo pacman -S tk`

## What it does

1. Reads how much RAM you have from `/proc/meminfo`.
2. Slowly fills memory by loading `sam.png` over and over, until the machine is at about 55% in use.
3. Stops and asks for consent, with the terms, privacy policy and RAM budget in one table.
4. If you agree, continues to a hard ceiling that always leaves one eighth of your RAM free (never less than 2 GB).

**Backspace** is the panic button: the process is killed instantly and Linux takes every byte back. Closing the window does the same.

It writes nothing to disk and has no networking code.
