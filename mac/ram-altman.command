#!/usr/bin/env python3
# Ram Altman for macOS - a parody RAM "borrower".
# One file, standard library only. No settings, cache, logs, temp files or network.
# Nothing is ever written to disk.
#
# Run:      double-click ram-altman.command, or in Terminal: ./ram-altman.command
# Needs:    python3 with Tk 8.6 (python.org installer, or: brew install python-tk)
# License:  MIT, see LICENSE. The photo is licensed separately, see CREDITS.txt.

import os
import re
import signal
import subprocess
import sys
import threading
import time

sys.dont_write_bytecode = True

try:
    import tkinter as tk
    from tkinter import font as tkfont
except ImportError:
    sys.exit("Ram Altman needs tkinter. Install Python from python.org, or run: brew install python-tk")

BACK = "#14161a"
PANEL = "#22262c"
LINE = "#3a404a"
TEXT = "#e8eaed"
DIM = "#969da8"
ACCENT = "#10a37f"
OTHER = "#606876"
DANGER = "#eb6054"

GIB = 1024 ** 3
QUOTE = "“Well, you're not going to use more than 5% of it, so let me have it.”"


def gb(n):
    return "%.1f GB" % (n / GIB)


def installed_ram():
    """RAM installed in this Mac, in bytes."""
    out = subprocess.run(["/usr/sbin/sysctl", "-n", "hw.memsize"], capture_output=True, text=True, timeout=5)
    return int(out.stdout.strip())


def parse_vm_stat(text, total):
    """(total, available) in bytes from the output of vm_stat.

    "Used" is what Activity Monitor calls Memory Used: app memory + wired + compressed.
    """
    page = 4096
    match = re.search(r"page size of (\d+) bytes", text)
    if match:
        page = int(match.group(1))
    pages = {}
    for line in text.splitlines():
        key, sep, value = line.partition(":")
        value = value.strip().rstrip(".")
        if sep and value.isdigit():
            pages[key.strip().strip('"')] = int(value)
    if "Anonymous pages" in pages:
        used = page * (pages["Anonymous pages"] - pages.get("Pages purgeable", 0)
                       + pages.get("Pages wired down", 0) + pages.get("Pages occupied by compressor", 0))
    else:  # very old macOS, or vm_stat gave nothing: count everything not plainly free as used
        used = total - page * (pages.get("Pages free", 0) + pages.get("Pages inactive", 0)
                               + pages.get("Pages speculative", 0))
    used = max(0, min(total, used))
    return total, total - used


_memory = {"total": None, "at": 0.0, "value": None}


def read_memory():
    """Installed RAM and how much of it is available right now. Refreshed at most 5 times a second."""
    now = time.monotonic()
    if _memory["value"] is None or now - _memory["at"] >= 0.2:
        if _memory["total"] is None:
            _memory["total"] = installed_ram()
        out = subprocess.run(["/usr/bin/vm_stat"], capture_output=True, text=True, timeout=5)
        _memory["value"] = parse_vm_stat(out.stdout, _memory["total"])
        _memory["at"] = now
    return _memory["value"]


def panic(*_):
    """Instant, unconditional death. No cleanup handlers, no saved state."""
    os.kill(os.getpid(), signal.SIGKILL)


class RamEater(threading.Thread):
    """Loads the same file into memory over and over, at a gentle pace, until told to stop."""

    CHUNK = 4 * 1024 * 1024

    def __init__(self, payload_path, bytes_per_second):
        super().__init__(daemon=True)
        self.payload_path = payload_path
        self.rate = max(bytes_per_second, self.CHUNK)
        self.hoard = []
        self.held = 0
        self.loads = 0
        self.refused = False
        self._target_used = 0
        self._max_held = 0
        self._eating = False
        self._restart = False
        self._reached = False
        self.start()

    def begin(self, target_used, max_held):
        """Eat until machine-wide usage hits target_used, never holding more than max_held ourselves."""
        self._target_used = target_used
        self._max_held = max_held
        self._reached = False
        self._restart = True
        self._eating = True

    def take_reached(self):
        """True once (per begin) when the target has been reached."""
        if not self._reached:
            return False
        self._reached = False
        return True

    def run(self):
        started = time.monotonic()
        session = 0
        payload = None
        while True:
            if not self._eating:
                time.sleep(0.03)
                continue
            if self._restart:
                self._restart = False
                session = 0
                started = time.monotonic()

            try:
                total, available = read_memory()
            except (OSError, ValueError, subprocess.SubprocessError):
                self.refused = True
                self._eating = False
                self._reached = True
                continue
            if total - available >= self._target_used or self.held >= self._max_held:
                self._eating = False
                self._reached = True
                continue

            # Pace ourselves: only allocate what the clock says we are allowed so far.
            if session >= (time.monotonic() - started) * self.rate:
                time.sleep(0.005)
                continue

            try:
                if payload is None:
                    payload = open(self.payload_path, "rb", buffering=0)
                chunk = bytearray(self.CHUNK)
                view = memoryview(chunk)
                offset = 0
                empty_reads = 0
                while offset < self.CHUNK:
                    n = payload.readinto(view[offset:])
                    if not n:
                        # Reached the end of the file: load it again from the top.
                        payload.seek(0)
                        self.loads += 1
                        empty_reads += 1
                        if empty_reads > 1:
                            break  # empty file, nothing to load
                        continue
                    empty_reads = 0
                    offset += n
                self.hoard.append(chunk)
                self.held += self.CHUNK
                session += self.CHUNK
            except (MemoryError, OSError):
                # macOS said no (out of memory, file vanished...). Stop where we are.
                self.refused = True
                self._eating = False
                self._reached = True


class FlatButton(tk.Label):
    """A button drawn as a label, because native macOS buttons ignore colours."""

    def __init__(self, parent, text, font, command, enabled=True):
        super().__init__(parent, text=text, font=font, bg=PANEL, fg=TEXT if enabled else DIM,
                         highlightthickness=1, highlightbackground=LINE)
        self.command = command
        self.enabled = enabled
        self.bind("<Button-1>", self.click)

    def click(self, _event=None):
        if self.enabled:
            self.command()

    def set(self, enabled, color):
        self.enabled = enabled
        self.configure(bg=color, fg=TEXT if enabled else DIM)


class ConsentDialog:
    def __init__(self, app, used, held):
        self.app = app
        self.done = False
        self.agreed = False
        px, font = app.px, app.font
        left_for_you = max(0, app.total - app.ceiling)
        extra = max(0, app.ceiling - used)

        win = self.win = tk.Toplevel(app.root, bg=BACK)
        win.title("Ram Altman - Consent")
        win.resizable(False, False)
        w, h = px(680), px(600)
        x = app.root.winfo_rootx() + (app.root.winfo_width() - w) // 2
        y = app.root.winfo_rooty() + (app.root.winfo_height() - h) // 2
        win.geometry("%dx%d+%d+%d" % (w, h, max(0, x), max(0, y)))
        win.transient(app.root)
        win.attributes("-topmost", True)
        win.protocol("WM_DELETE_WINDOW", lambda: self.finish(False))

        tk.Label(win, text="Would you like to go more?", fg=TEXT, bg=BACK, font=font(20, "bold"),
                 anchor="w").place(x=px(20), y=px(14), width=px(640), height=px(34))
        tk.Label(win, fg=DIM, bg=BACK, font=font(13), anchor="nw", justify="left", wraplength=px(640),
                 text="I'm holding %s. Say yes and I'll take about %s more, stopping at %s in use. "
                      "Read the fine print first." % (gb(held), gb(extra), gb(app.ceiling))
                 ).place(x=px(20), y=px(50), width=px(640), height=px(40))

        # The table: fixed header, scrolling body.
        widths = (px(56), px(170), px(396))
        table = tk.Frame(win, bg=PANEL)
        table.place(x=px(20), y=px(96), width=px(640), height=px(384))
        header = tk.Frame(table, bg=BACK)
        header.pack(fill="x")
        for i, name in enumerate(("No.", "Clause", "What it says")):
            tk.Label(header, text=name, fg=DIM, bg=BACK, font=font(12), anchor="w",
                     padx=px(8), pady=px(6)).grid(row=0, column=i, sticky="w")
            header.grid_columnconfigure(i, minsize=widths[i])
        body = tk.Frame(table, bg=PANEL)
        body.pack(fill="both", expand=True)
        canvas = tk.Canvas(body, bg=PANEL, highlightthickness=0, yscrollincrement=px(24))
        scroll = tk.Scrollbar(body, orient="vertical", command=canvas.yview)
        canvas.configure(yscrollcommand=scroll.set)
        scroll.pack(side="right", fill="y")
        canvas.pack(side="left", fill="both", expand=True)
        inner = self.inner = tk.Frame(canvas, bg=PANEL)
        canvas.create_window(0, 0, window=inner, anchor="nw")
        inner.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
        for i in range(3):
            inner.grid_columnconfigure(i, minsize=widths[i])
        self.widths = widths
        self.next_row = 0
        win.bind("<MouseWheel>", lambda e: canvas.yview_scroll(-1 if e.delta > 0 else 1, "units"))

        self.section("TERMS AND CONDITIONS")
        self.row("1", "The loan", "You lend Ram Altman more of this computer's memory. It is borrowed, not consumed.")
        self.row("2", "The 5% doctrine", "\"You're not going to use more than 5% of it.\" This is a sales pitch, not a measurement.")
        self.row("3", "Hard stop", "Total memory in use will not be pushed past %s. About %s always stays yours."
                 % (gb(app.ceiling), gb(left_for_you)))
        self.row("4", "Revocation", "Press Delete (backspace) at any time. The app dies instantly and macOS takes every byte back.")
        self.row("5", "Returns", "Closing the window also returns 100% of the RAM. There is nothing to uninstall or undo.")
        self.row("6", "Side effects", "Other programs may slow down and macOS may start compressing and swapping memory while I hold your RAM. Save your work before agreeing.")
        self.row("7", "Warranty", "None. This is a joke app, provided as is.")
        self.row("8", "Parody", "Not affiliated with, endorsed by, or connected to Sam Altman or OpenAI.")

        self.section("PRIVACY POLICY")
        self.row("9", "Data collected", "None.")
        self.row("10", "Files written", "None. No settings, cache, logs or temp files. Every launch is a first launch.")
        self.row("11", "Network", "Never used. There is no networking code in this app.")
        self.row("12", "What I read", "How much RAM you have (from macOS's own sysctl and vm_stat tools), and one file (%s) loaded again and again as filler." % app.payload_name)
        self.row("13", "After exit", "Nothing of mine remains. Terminal may keep the command in its history; that is not mine to control.")

        self.section("RAM BUDGET")
        self.row("14", "Installed", gb(app.installed))
        self.row("15", "In use right now", "%s, of which I hold %s" % (gb(used), gb(held)))
        self.row("16", "If you consent", "I take about %s more, slowly." % gb(extra))
        self.row("17", "Ceiling", "%s in use, machine-wide" % gb(app.ceiling))
        self.row("18", "Left for you", gb(left_for_you))

        # A hand-drawn checkbox, for the same reason as FlatButton.
        box = self.box = tk.Canvas(win, bg=BACK, highlightthickness=0)
        box.place(x=px(20), y=px(493), width=px(22), height=px(22))
        text = tk.Label(win, text="I have read the above and I consent to Ram Altman taking more RAM.",
                        fg=TEXT, bg=BACK, font=font(13), anchor="w")
        text.place(x=px(50), y=px(490), width=px(610), height=px(28))
        for widget in (box, text):
            widget.bind("<Button-1>", self.toggle)
        self.draw_box()

        tk.Label(win, text="Delete (backspace) = instant shutdown", fg=DANGER, bg=BACK, font=font(13),
                 anchor="w").place(x=px(20), y=px(540), width=px(300), height=px(38))
        self.enough = FlatButton(win, "No, that's enough", font(13), lambda: self.finish(False))
        self.enough.place(x=px(336), y=px(540), width=px(160), height=px(38))
        self.more = FlatButton(win, "Take more", font(13), lambda: self.finish(True), enabled=False)
        self.more.place(x=px(506), y=px(540), width=px(154), height=px(38))

        win.after(80, self.grab)

    def cell(self, text, column, span, fg, bg, weight):
        pad = self.app.px(8)
        width = sum(self.widths[column:column + span])
        tk.Label(self.inner, text=text, fg=fg, bg=bg, font=self.app.font(13, weight), anchor="nw",
                 justify="left", wraplength=width - 2 * pad, padx=pad, pady=self.app.px(7)
                 ).grid(row=self.next_row, column=column, columnspan=span, sticky="nsew")

    def end_row(self):
        tk.Frame(self.inner, bg=LINE, height=1).grid(row=self.next_row + 1, column=0, columnspan=3, sticky="ew")
        self.next_row += 2

    def row(self, number, clause, says):
        for column, text in enumerate((number, clause, says)):
            self.cell(text, column, 1, TEXT, PANEL, "normal")
        self.end_row()

    def section(self, name):
        self.cell("", 0, 1, ACCENT, BACK, "bold")
        self.cell(name, 1, 2, ACCENT, BACK, "bold")
        self.end_row()

    def draw_box(self):
        s = self.app.px(20)
        box = self.box
        box.delete("all")
        box.create_rectangle(1, 1, s, s, fill=ACCENT if self.agreed else PANEL, outline=LINE)
        if self.agreed:
            box.create_line(s * 0.22, s * 0.52, s * 0.42, s * 0.72, s * 0.80, s * 0.28, fill=TEXT, width=2)

    def toggle(self, _event=None):
        self.agreed = not self.agreed
        self.draw_box()
        self.more.set(self.agreed, ACCENT if self.agreed else PANEL)

    def grab(self):
        try:
            self.win.grab_set()
            self.win.focus_set()
        except tk.TclError:
            pass

    def finish(self, yes):
        if self.done:
            return
        self.done = True
        self.win.destroy()
        self.app.answer(yes)


class App:
    DETECTING, EATING_TO_ASK, AWAITING_CONSENT, EATING_TO_CEILING, FULL, DECLINED, REFUSED = range(7)

    def __init__(self, root):
        self.root = root
        self.scale = max(1.0, root.winfo_fpixels("1i") / 96.0)
        self.family = tkfont.nametofont("TkDefaultFont").actual("family")
        self.fonts = {}
        self.dialog = None
        self.phase = self.DETECTING
        self.started = time.monotonic()

        total, available = read_memory()
        self.total = self.installed = total

        # Leave room: one eighth of the machine (32 GB -> stop at 28 GB), and never less than 2 GB.
        reserve = max(self.installed // 8, 2 * GIB)
        self.ceiling = max(0, self.installed - reserve)
        self.ask_at = min(int(total * 0.55), self.ceiling)
        # Runaway guard on our own share. Slack covers other programs shrinking while we grow,
        # so the machine-wide ceiling is normally what stops us.
        self.max_held = min(self.ceiling, max(0, self.ceiling - (total - available) + reserve // 2))

        here = os.path.dirname(os.path.abspath(__file__))
        portrait_path = None
        for name in ("sam.png", "sam.gif"):
            candidate = os.path.join(here, name)
            if os.path.isfile(candidate) and os.path.getsize(candidate) > 0:
                portrait_path = candidate
                break
        payload_path = portrait_path or os.path.abspath(__file__)
        self.payload_name = os.path.basename(payload_path)

        px = self.px
        root.title("Ram Altman")
        root.configure(bg=BACK)
        root.resizable(False, False)
        w, h = px(460), px(632)
        root.geometry("%dx%d+%d+%d" % (w, h, (root.winfo_screenwidth() - w) // 2,
                                       max(0, (root.winfo_screenheight() - h) // 2 - px(20))))
        root.attributes("-topmost", True)
        root.protocol("WM_DELETE_WINDOW", panic)  # closing is the same as panicking
        root.bind_all("<BackSpace>", panic)  # the key labelled "delete" on Mac keyboards

        self.portrait = tk.Canvas(root, bg=PANEL, highlightthickness=0)
        self.portrait.place(x=px(130), y=px(22), width=px(200), height=px(200))
        self.draw_portrait(portrait_path)

        self.label("RAM ALTMAN", 14, ACCENT, weight="bold").place(x=px(20), y=px(228), width=px(420), height=px(24))
        self.label(QUOTE, 17, TEXT, PANEL, slant="italic", wrap=390).place(x=px(20), y=px(258), width=px(420), height=px(78))

        self.installed_label = self.label()
        self.used_label = self.label()
        self.held_label = self.label("", 14, ACCENT, weight="bold")
        self.file_label = self.label("", 13, DIM)
        for i, widget in enumerate((self.installed_label, self.used_label, self.held_label, self.file_label)):
            widget.place(x=px(20), y=px(352 + 26 * i), width=px(420), height=px(24))

        self.bar = tk.Canvas(root, bg=BACK, highlightthickness=0)
        self.bar.place(x=px(20), y=px(466), width=px(420), height=px(46))

        self.status_label = self.label("", 13, TEXT, wrap=420)
        self.status_label.place(x=px(20), y=px(520), width=px(420), height=px(44))

        self.label("PANIC BUTTON: Delete (backspace). Instant shutdown, nothing saved.", 13, DANGER, PANEL
                   ).place(x=0, y=px(588), width=px(460), height=px(44))

        # About half a percent of the machine per second: slow enough to watch.
        self.eater = RamEater(payload_path, total // 200)
        root.after(300, self.take_focus)
        self.tick()

    def take_focus(self):
        # Launched from Terminal, the window can open without keyboard focus; the panic key needs it.
        self.root.lift()
        self.root.focus_force()

    def px(self, value):
        return int(round(value * self.scale))

    def font(self, size, weight="normal", slant="roman"):
        key = (size, weight, slant)
        if key not in self.fonts:
            self.fonts[key] = tkfont.Font(family=self.family, size=-self.px(size), weight=weight, slant=slant)
        return self.fonts[key]

    def label(self, text="", size=13, fg=TEXT, bg=BACK, weight="normal", slant="roman", wrap=None):
        return tk.Label(self.root, text=text, fg=fg, bg=bg, font=self.font(size, weight, slant),
                        justify="center", wraplength=self.px(wrap) if wrap else 0)

    def draw_portrait(self, path):
        size = self.px(200)
        canvas = self.portrait
        self.image = None
        if path:
            try:
                image = tk.PhotoImage(file=path)
                smallest = min(image.width(), image.height())
                if smallest >= 2 * size:
                    image = image.subsample(smallest // size)
                elif size >= 2 * smallest:
                    image = image.zoom(size // smallest)
                self.image = image  # keep a reference or Tk drops the picture
            except tk.TclError:
                self.image = None
        if self.image:
            canvas.create_image(size // 2, size // 2, image=self.image)
        else:
            canvas.create_oval(size * 0.33, size * 0.20, size * 0.67, size * 0.58, fill=LINE, outline="")
            canvas.create_oval(size * 0.12, size * 0.64, size * 0.88, size * 1.44, fill=LINE, outline="")
            canvas.create_text(size // 2, size - self.px(14), text="put sam.png next to the app",
                               fill=TEXT, font=self.font(11))
        canvas.create_rectangle(1, 1, size - 1, size - 1, outline=ACCENT, width=2)

    def draw_bar(self, used, held):
        """One bar for the whole machine: everything else | Ram Altman's share | free, with both limits marked."""
        canvas = self.bar
        canvas.delete("all")
        w, bar_h = self.px(420), self.px(27)
        held = min(held, used)
        x_other = w * (used - held) / self.total
        x_used = w * min(used, self.total) / self.total
        canvas.create_rectangle(0, 0, w, bar_h, fill=PANEL, outline="")
        canvas.create_rectangle(0, 0, x_other, bar_h, fill=OTHER, outline="")
        canvas.create_rectangle(x_other, 0, x_used, bar_h, fill=ACCENT, outline="")
        for at, text, color in ((self.ask_at, "ask first", TEXT), (self.ceiling, "hard stop", DANGER)):
            x = min(w - 1, w * at / self.total)
            canvas.create_line(x, 0, x, bar_h + 2, fill=color, width=2)
            half = self.font(11).measure(text) / 2
            canvas.create_text(max(half, min(w - half, x)), bar_h + self.px(10), text=text, fill=color,
                               font=self.font(11))

    def tick(self):
        self.root.after(100, self.tick)
        try:
            total, available = read_memory()
        except (OSError, ValueError, subprocess.SubprocessError):
            return
        used = total - available
        held = self.eater.held

        self.installed_label.configure(text="Your Mac has %s of RAM" % gb(self.installed))
        self.used_label.configure(text="In use right now: %s  (%d%%)" % (gb(used), round(100.0 * used / self.total)))
        self.held_label.configure(text="Ram Altman is holding %s" % gb(held))
        self.file_label.configure(text="Loaded %s %s times" % (self.payload_name, format(self.eater.loads, ",")))
        self.draw_bar(used, held)

        phase = self.phase
        if phase == self.DETECTING:
            status = "Counting your RAM..."
            if time.monotonic() - self.started > 2.5:
                self.phase = self.EATING_TO_ASK
                self.eater.begin(self.ask_at, self.max_held)
        elif phase == self.EATING_TO_ASK:
            status = "Loading %s again. And again. And again." % self.payload_name
            if self.eater.take_reached():
                if self.eater.refused:
                    self.phase = self.REFUSED
                elif self.ceiling - self.ask_at < 64 * 1024 * 1024:
                    self.phase = self.FULL
                else:
                    self.phase = self.AWAITING_CONSENT
                    self.dialog = ConsentDialog(self, used, held)
        elif phase == self.AWAITING_CONSENT:
            status = "I'd like more. Waiting for your consent."
        elif phase == self.EATING_TO_CEILING:
            status = "Consent received. Scaling up."
            if self.eater.take_reached():
                self.phase = self.REFUSED if self.eater.refused else self.FULL
        elif phase == self.FULL:
            status = ("That's my limit. I left you %s. Close the window to get it all back."
                      % gb(max(0, self.total - self.ceiling)))
        elif phase == self.DECLINED:
            status = "Fine. I'll just hold on to what I've got.\nClose the window to get it back."
        else:
            status = "macOS won't give me any more. Holding here."
        self.status_label.configure(text=status)

    def answer(self, yes):
        self.dialog = None
        if yes:
            self.phase = self.EATING_TO_CEILING
            self.eater.begin(self.ceiling, self.max_held)
        else:
            self.phase = self.DECLINED


def main():
    if sys.platform != "darwin":
        sys.exit("This is the macOS version of Ram Altman. See the windows and linux versions for other systems.")
    if tk.TkVersion < 8.6:
        print("Note: this Python uses an old Tk (%s), so the portrait may not show. "
              "Install Python from python.org, or run: brew install python-tk" % tk.TkVersion, file=sys.stderr)
    try:
        root = tk.Tk()
    except tk.TclError as error:
        sys.exit("Ram Altman could not open a window: %s" % error)
    App(root)
    root.mainloop()


if __name__ == "__main__":
    main()
