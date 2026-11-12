# Ram Altman for macOS

The same app as the Windows version, as one executable Python script. Nothing to build or install.

## Run it

Double-click `ram-altman.command` in Finder. A Terminal window opens and starts the app.

If macOS says it "cannot be opened because it is from an unidentified developer", right-click the file, choose **Open**, then **Open** again. That is only needed the first time.

Or from Terminal, in this folder:

```bash
chmod +x ram-altman.command
```

```bash
./ram-altman.command
```

Keep `sam.png` in the same folder; swap in any other PNG with that name to change the portrait.

## Requirements

Python 3 with Tk 8.6. The Python that Apple ships with its developer tools has an older Tk and may not show the portrait. Either of these works:

- Install Python from [python.org](https://www.python.org/downloads/macos/).
- With Homebrew: `brew install python-tk`

## What it does

1. Asks macOS how much RAM you have and how much is in use (the same "Memory Used" figure Activity Monitor shows).
2. Slowly fills memory by loading `sam.png` over and over, until the Mac is at about 55% in use.
3. Stops and asks for consent, with the terms, privacy policy and RAM budget in one table.
4. If you agree, continues to a hard ceiling that always leaves one eighth of your RAM free (never less than 2 GB).

The **Delete** key (backspace) is the panic button: the process is killed instantly and macOS takes every byte back. Closing the window does the same. Terminal will print "Killed: 9"; that is the panic button working.

It writes nothing to disk and has no networking code.

Macs often sit above 55% memory use already. In that case the app skips straight to the consent question.
