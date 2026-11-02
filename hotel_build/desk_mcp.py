#!/usr/bin/env python3
"""hotel-desk MCP server (progetto PryingEyes) — stdio, solo stdlib.

Strumenti:
  fetch_texture(name, url)  — scarica JPG/PNG solo da host allowlist in
                             hotel_build/textures_src/, registra URL+SHA256+data
                             in textures_manifest.json. Rifiuta tutto il resto.
  focus_window(title)      — porta in primo piano la finestra il cui titolo
                             contiene `title` (via PowerShell).
  key_hold(key, seconds)   — tiene premuto un tasto (w/a/s/d/space/shift/r/escape...)
                             per N secondi via SendInput (finestra in primo piano).
  key_tap(key)             — singola pressione.
  mouse_click(x, y, button)— sposta il cursore e clicca (left/right).
  screenshot(path)        — cattura schermo intero in PNG via PowerShell.

Nessuna rete salvo allowlist, nessun shell arbitrario, nessun segreto.
"""
import ctypes
import hashlib
import json
import os
import sys
import time
import urllib.request

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TEX_DIR = os.path.join(BASE_DIR, "textures_src")
MANIFEST = os.path.join(BASE_DIR, "textures_manifest.json")
ALLOWED_HOSTS = {"cdn.jsdelivr.net", "unpkg.com", "raw.githubusercontent.com"}
ALLOWED_EXT = (".jpg", ".jpeg", ".png")
MAX_BYTES = 25 * 1024 * 1024

VK = {
    "w": 0x57, "a": 0x41, "s": 0x53, "d": 0x44,
    "r": 0x52, "e": 0x45, "q": 0x51, "c": 0x43,
    "space": 0x20, "shift": 0x10, "ctrl": 0x11, "escape": 0x1B,
    "enter": 0x0D, "tab": 0x09, "f5": 0x74,
    "up": 0x26, "down": 0x28, "left": 0x25, "right": 0x27,
}

PUL = ctypes.POINTER(ctypes.c_ulong)


class KeyBdInput(ctypes.Structure):
    _fields_ = [("wVk", ctypes.c_ushort), ("wScan", ctypes.c_ushort),
                ("dwFlags", ctypes.c_ulong), ("time", ctypes.c_ulong),
                ("dwExtraInfo", PUL)]


class MouseInput(ctypes.Structure):
    _fields_ = [("dx", ctypes.c_long), ("dy", ctypes.c_long),
                ("mouseData", ctypes.c_ulong), ("dwFlags", ctypes.c_ulong),
                ("time", ctypes.c_ulong), ("dwExtraInfo", PUL)]


class InputUnion(ctypes.Union):
    _fields_ = [("ki", KeyBdInput), ("mi", MouseInput)]


class Input(ctypes.Structure):
    _fields_ = [("type", ctypes.c_ulong), ("u", InputUnion)]


def _send(inp):
    n = ctypes.windll.user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(inp))
    if n != 1:
        raise RuntimeError("SendInput failed")


def key_event(vk, down):
    inp = Input()
    inp.type = 1  # INPUT_KEYBOARD
    inp.u.ki.wVk = vk
    inp.u.ki.wScan = 0
    inp.u.ki.dwFlags = 0 if down else 0x0002  # KEYEVENTF_KEYUP
    _send(inp)


def mouse_event(flags, dx=0, dy=0, data=0):
    inp = Input()
    inp.type = 0  # INPUT_MOUSE
    inp.u.mi.dx = dx
    inp.u.mi.dy = dy
    inp.u.mi.mouseData = data
    inp.u.mi.dwFlags = flags
    _send(inp)


def tool_fetch_texture(name, url):
    from urllib.parse import urlparse
    u = urlparse(url)
    if u.scheme != "https" or u.hostname not in ALLOWED_HOSTS:
        raise ValueError("host non consentito: %s (allowlist: %s)" % (u.hostname, sorted(ALLOWED_HOSTS)))
    path = u.path.lower()
    if not path.endswith(ALLOWED_EXT):
        raise ValueError("solo JPG/PNG, non: %s" % u.path)
    safe = "".join(c for c in name if c.isalnum() or c in ("-", "_", "."))
    if not safe or safe.startswith("."):
        raise ValueError("nome non valido")
    os.makedirs(TEX_DIR, exist_ok=True)
    dest = os.path.join(TEX_DIR, safe)
    req = urllib.request.Request(url, headers={"User-Agent": "hotel-desk-mcp/1.0"})
    h = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(req, timeout=60) as resp, open(dest, "wb") as f:
        while True:
            chunk = resp.read(65536)
            if not chunk:
                break
            size += len(chunk)
            if size > MAX_BYTES:
                f.close()
                os.remove(dest)
                raise ValueError("file troppo grande (>25MB)")
            h.update(chunk)
            f.write(chunk)
    entry = {"name": safe, "url": url, "sha256": h.hexdigest(),
             "bytes": size, "date": time.strftime("%Y-%m-%d")}
    man = []
    if os.path.exists(MANIFEST):
        with open(MANIFEST, "r", encoding="utf-8") as f:
            man = json.load(f)
    man = [e for e in man if e.get("name") != safe] + [entry]
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(man, f, indent=2)
    return entry


def tool_focus_window(title):
    import subprocess
    ps = ("Add-Type -AssemblyName UIAutomationClient; "
          "$ws=[System.Windows.Automation.AutomationElement]::RootElement.FindAll("
          "[System.Windows.Automation.TreeScope]::Children,"
          "[System.Windows.Automation.Condition]::TrueCondition); "
          "foreach($w in $ws){try{$t=$w.Current.Name; if($t -like '*%s*'){"
          "$h=$w.Current.NativeWindowHandle; "
          "Add-Type -MemberDefinition '[DllImport(\"user32.dll\")]public static extern bool SetForegroundWindow(IntPtr h);' -Name W -Namespace X; "
          "[X.W]::SetForegroundWindow($h); Write-Output \"FOCUSED: $t\"; exit 0}}catch{}}; "
          "Write-Output 'NOT-FOUND'; exit 1" % title.replace("'", ""))
    r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                       capture_output=True, text=True, timeout=30)
    out = (r.stdout or "") + (r.stderr or "")
    if "FOCUSED:" not in out:
        raise RuntimeError("finestra non trovata: %s" % title)
    time.sleep(0.5)
    return out.strip().splitlines()[-1]


def tool_key_hold(key, seconds):
    vk = VK.get(str(key).lower())
    if vk is None:
        raise ValueError("tasto non supportato: %s (supportati: %s)" % (key, sorted(VK)))
    secs = max(0.1, min(float(seconds), 20.0))
    key_event(vk, True)
    time.sleep(secs)
    key_event(vk, False)
    return {"key": key, "seconds": secs}


def tool_key_tap(key):
    vk = VK.get(str(key).lower())
    if vk is None:
        raise ValueError("tasto non supportato: %s" % key)
    key_event(vk, True)
    time.sleep(0.05)
    key_event(vk, False)
    return {"key": key}


def tool_mouse_click(x, y, button="left"):
    ctypes.windll.user32.SetCursorPos(int(x), int(y))
    time.sleep(0.1)
    if button == "left":
        mouse_event(0x0002)
        time.sleep(0.05)
        mouse_event(0x0004)
    elif button == "right":
        mouse_event(0x0008)
        time.sleep(0.05)
        mouse_event(0x0010)
    else:
        raise ValueError("button: left|right")
    return {"x": int(x), "y": int(y), "button": button}


def tool_screenshot(path):
    import subprocess
    if not path.lower().endswith(".png"):
        raise ValueError("solo .png")
    dest = path if os.path.isabs(path) else os.path.join(BASE_DIR, path)
    ps = ("Add-Type -AssemblyName System.Windows.Forms,System.Drawing; "
          "$b=[System.Windows.Forms.SystemInformation]::VirtualScreen; "
          "$bmp=New-Object System.Drawing.Bitmap($b.Width,$b.Height); "
          "$g=[System.Drawing.Graphics]::FromImage($bmp); "
          "$g.CopyFromScreen($b.X,$b.Y,0,0,$bmp.Size); "
          "$bmp.Save('%s'); $g.Dispose(); $bmp.Dispose(); 'OK'" % dest.replace("'", ""))
    r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                       capture_output=True, text=True, timeout=60)
    if "OK" not in (r.stdout or ""):
        raise RuntimeError("screenshot fallito: %s" % ((r.stdout or "") + (r.stderr or ""))[:300])
    return {"path": dest, "bytes": os.path.getsize(dest)}


TOOLS = {
    "fetch_texture": (tool_fetch_texture,
        {"type": "object", "properties": {
            "name": {"type": "string", "description": "Nome file destinazione, es. brick_diffuse.jpg"},
            "url": {"type": "string", "description": "URL https (solo host allowlist)"}},
         "required": ["name", "url"]},
        "Scarica una texture JPG/PNG solo da host verificati (cdn.jsdelivr.net, unpkg.com, raw.githubusercontent.com) in hotel_build/textures_src/ e registra URL+SHA256+data nel manifest."),
    "focus_window": (tool_focus_window,
        {"type": "object", "properties": {
            "title": {"type": "string", "description": "Sottostringa del titolo finestra"}},
         "required": ["title"]},
        "Porta in primo piano una finestra (es. 'Unreal Editor')."),
    "key_hold": (tool_key_hold,
        {"type": "object", "properties": {
            "key": {"type": "string", "description": "w/a/s/d/r/space/shift/escape/..."},
            "seconds": {"type": "number", "description": "Durata 0.1-20s"}},
         "required": ["key", "seconds"]},
        "Tiene premuto un tasto per N secondi (finestra in primo piano)."),
    "key_tap": (tool_key_tap,
        {"type": "object", "properties": {"key": {"type": "string"}},
         "required": ["key"]},
        "Singola pressione di tasto."),
    "mouse_click": (tool_mouse_click,
        {"type": "object", "properties": {
            "x": {"type": "integer"}, "y": {"type": "integer"},
            "button": {"type": "string", "description": "left|right", "default": "left"}},
         "required": ["x", "y"]},
        "Sposta il cursore e clicca."),
    "screenshot": (tool_screenshot,
        {"type": "object", "properties": {
            "path": {"type": "string", "description": "Percorso PNG di uscita"}},
         "required": ["path"]},
        "Cattura lo schermo intero in PNG."),
}


def respond(mid, result=None, error=None):
    msg = {"jsonrpc": "2.0", "id": mid}
    if error is not None:
        msg["error"] = {"code": -32000, "message": str(error)}
    else:
        msg["result"] = result
    sys.stdout.write(json.dumps(msg) + "\n")
    sys.stdout.flush()


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except ValueError:
            continue
        mid = req.get("id")
        method = req.get("method", "")
        params = req.get("params", {}) or {}
        try:
            if method == "initialize":
                respond(mid, {"protocolVersion": "2024-11-05",
                              "capabilities": {"tools": {}},
                              "serverInfo": {"name": "hotel-desk", "version": "1.0"}})
            elif method in ("notifications/initialized", "notifications/cancelled"):
                pass
            elif method == "tools/list":
                respond(mid, {"tools": [
                    {"name": n, "description": fn[2], "inputSchema": fn[1]}
                    for n, fn in TOOLS.items()]})
            elif method == "tools/call":
                name = params.get("name", "")
                args = params.get("arguments", {}) or {}
                if name not in TOOLS:
                    respond(mid, error="tool sconosciuto: " + name)
                else:
                    out = TOOLS[name][0](**args)
                    respond(mid, {"content": [{"type": "text",
                                                "text": json.dumps(out, ensure_ascii=False)}]})
            elif method == "ping":
                respond(mid, {})
            else:
                respond(mid, error="metodo sconosciuto: " + method)
        except Exception as e:  # noqa: BLE001 - boundary MCP verso agent
            try:
                respond(mid, error=str(e)[:500])
            except ValueError:
                pass


if __name__ == "__main__":
    main()
