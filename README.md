# Ram Altman

> "Well, you're not going to use more than 5% of it, so let me have it."

A parody app for Windows. Ram Altman looks at how much RAM your computer has and politely helps himself to it.

## What it does

1. Shows the portrait, the quote, and how much RAM you have.
2. Slowly fills memory by loading the same file (`sam.jpg`) over and over, until the whole machine is at about 55% in use.
3. Stops and asks for consent, with terms and conditions, a privacy policy and a RAM budget in one table.
4. If you agree, keeps going up to a hard ceiling that always leaves one eighth of your RAM free (never less than 2 GB). On a 32 GB machine it stops at 28 GB.

## Stopping it

- **Backspace** is the panic button: the app dies instantly. The window must be focused; it stays on top so it is one click away.
- Closing the window does the same thing.

Either way Windows takes every byte back immediately.

## Privacy

The app writes nothing: no settings, cache, logs, temp files or registry entries, and it has no networking code. Every launch is a first launch.

## Build and run

No installs needed. It builds with the C# compiler that ships with Windows.

1. Download or clone this repository.
2. Double-click `build.bat`.
3. Run `RamAltman.exe`.

Keep `sam.jpg` next to the exe. Swap in any other image with that name to change the portrait.

### Antivirus note

Small unsigned programs that grab a lot of memory can trip machine-learning antivirus heuristics. Windows Defender has flagged an earlier build as a false positive. The full source is one file, `RamAltman.cs`, so you can read exactly what it does before building it yourself.

## License and credits

- Code: [MIT](LICENSE). Free to use, edit and share.
- Photo: CC BY 2.0, TechCrunch. See [CREDITS.txt](CREDITS.txt).

This is a parody. It is not affiliated with or endorsed by Sam Altman, OpenAI or TechCrunch.
