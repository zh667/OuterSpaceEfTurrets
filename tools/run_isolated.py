"""Run the mod's self-test in an isolated copy of the game and wait for its result file.

python tools/run_isolated.py [--tests all|smoke|freeze-demo|...] [--save zh6657] [--exact-save] [--limit 900] [--no-build]

The copy lives in the mod kit (E:/projects/outer-space-modkit/runtime) and is shared by all mods; one test at a time.
It differs from the installed game in three places only:
  - Outer Space_Data/globalgamemanagers: product name "Outer Space" -> "OS ModTests" (same length, one occurrence),
    and app.info (Unity takes the log folder from there), so saves, settings and logs go to
    LocalLow/CatStudio/OS ModTests instead of the player's folder;
  - steam_appid.txt next to the exe, so Steam does not restart the original game.
The player's folder is only read: the newest save starting with --save (written at least a minute ago) is copied.
The test copy carries the player's other installed mods and enables the ones they enabled, plus this mod. The copy starts minimised, with UNITY_MIXED_CALLSTACK=1 so a
freeze dump can name managed frames. A heartbeat that stops for a minute counts as a freeze: the game is dumped
(tools/freeze_dump.py), its threads listed with names, and it is ended.
"""
import argparse, json, os, re, shutil, struct, subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOD = json.loads((ROOT / "mod.json").read_text(encoding="utf-8"))
KIT = Path(r"E:\projects\outer-space-modkit")
GAME = Path(r"E:\SteamLibrary\steamapps\common\Outer Space")
LOCALLOW = Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow" / "CatStudio"
PLAYER_DATA = LOCALLOW / "Outer Space"
TEST_NAME = b"OS ModTests"
TEST_DATA = LOCALLOW / TEST_NAME.decode()
RUNTIME = KIT / "runtime"
APP_ID = "3080070"
SKIP_TOP = {"mods", "Outer Space_BurstDebugInformation_DoNotShip"}


def copy_game(refresh):
    n = 0
    for src_dir, dirs, files in os.walk(GAME):
        rel = Path(src_dir).relative_to(GAME)
        if rel.parts and rel.parts[0] in SKIP_TOP:
            dirs[:] = []
            continue
        dst_dir = RUNTIME / rel
        dst_dir.mkdir(parents=True, exist_ok=True)
        for f in files:
            s, d = Path(src_dir) / f, dst_dir / f
            if not refresh and d.exists() and d.stat().st_size == s.stat().st_size and int(d.stat().st_mtime) == int(s.stat().st_mtime):
                continue
            shutil.copy2(s, d)
            n += 1
    print("game files copied/updated:", n)


def patch_names():
    assert len(TEST_NAME) == 11
    ggm = RUNTIME / "Outer Space_Data" / "globalgamemanagers"
    b = bytearray(ggm.read_bytes())
    head = b"\x09\x00\x00\x00CatStudio\x00\x00\x00\x0b\x00\x00\x00"
    if b.count(head + TEST_NAME) != 1:
        assert b.count(head + b"Outer Space") == 1, "expected exactly one company/product name record"
        i = b.find(head + b"Outer Space")
        b[i + len(head):i + len(head) + 11] = TEST_NAME
        ggm.write_bytes(bytes(b))
        print("product name patched")
    info = RUNTIME / "Outer Space_Data" / "app.info"
    lines = info.read_text(encoding="utf-8").splitlines()
    if lines[1] != TEST_NAME.decode():
        assert lines[0] == "CatStudio" and lines[1] == "Outer Space", lines
        lines[1] = TEST_NAME.decode()
        info.write_text("\n".join(lines), encoding="utf-8")
        print("app.info patched")
    (RUNTIME / "steam_appid.txt").write_text(APP_ID, encoding="ascii")


def newest_save(prefix):
    best = None
    for d in (PLAYER_DATA / "archives").iterdir():
        if not d.is_dir() or not d.name.startswith(prefix):
            continue
        arcs = list(d.glob("*.arc"))
        if arcs and time.time() - arcs[0].stat().st_mtime < 60:
            continue   # the running game may still be writing it
        if arcs and (best is None or arcs[0].stat().st_mtime > best[0]):
            best = (arcs[0].stat().st_mtime, d.name)
    return best[1] if best else prefix


def read_mod_dat(path):
    ids = []
    try:
        b = path.read_bytes()
        n, pos = struct.unpack(">i", b[:4])[0], 4
        for _ in range(n):
            ln = struct.unpack(">H", b[pos:pos + 2])[0]
            ids.append(b[pos + 2:pos + 2 + ln].decode("utf-8"))
            pos += 2 + ln
    except (OSError, struct.error, UnicodeDecodeError):
        pass
    return ids


def prepare_data(save):
    assert TEST_DATA != PLAYER_DATA and TEST_DATA.name == TEST_NAME.decode()
    archives = TEST_DATA / "archives"
    if archives.exists():
        shutil.rmtree(archives)
    archives.mkdir(parents=True)
    shutil.copytree(PLAYER_DATA / "archives" / save, archives / save)
    for name in ("start_config", "userInfo", "versionCode"):
        s = PLAYER_DATA / name
        if s.is_dir():
            if (TEST_DATA / name).exists():
                shutil.rmtree(TEST_DATA / name)
            shutil.copytree(s, TEST_DATA / name)
        elif s.exists():
            shutil.copy2(s, TEST_DATA / name)
    if (PLAYER_DATA / "Assets").exists() and not (TEST_DATA / "Assets").exists():
        shutil.copytree(PLAYER_DATA / "Assets", TEST_DATA / "Assets")   # audio/asset caches: faster first start
    # The player's mod setup: their enabled mods stay enabled (their files are copied by install_mod), so a save that
    # used them loads without the game's "missing mod" question; this mod is added.
    ids = read_mod_dat(PLAYER_DATA / "mod.dat")
    if MOD["packageId"] not in ids:
        ids.append(MOD["packageId"])
    # mod.dat (ModPluginManager.saveInfo): big-endian int count, then per mod a big-endian ushort length + UTF-8 id.
    data = struct.pack(">i", len(ids)) + b"".join(struct.pack(">H", len(i.encode())) + i.encode() for i in ids)
    (TEST_DATA / "mod.dat").write_bytes(data)
    print("test data:", TEST_DATA, "save:", save, "mods enabled:", ids)


def install_mod(no_build):
    if not no_build:
        r = subprocess.run([sys.executable, str(ROOT / "tools" / "build.py")], capture_output=True, text=True, encoding="utf-8", errors="replace")
        print(r.stdout[-1500:])
        if r.returncode:
            sys.exit("build failed")
    mods = RUNTIME / "mods"
    if mods.exists():
        shutil.rmtree(mods)
    mods.mkdir()
    # The player's other installed mods (code and manifest only; their data folders stay with the player).
    for other in (GAME / "mods").iterdir() if (GAME / "mods").exists() else []:
        if other.is_dir() and other.name != MOD["folder"]:
            for part in ("Manifest", "v_common"):
                if (other / part).exists():
                    shutil.copytree(other / part, mods / other.name / part)
    shutil.copytree(ROOT / "dist" / MOD["folder"], mods / MOD["folder"])
    print("mods in the test copy:", sorted(p.name for p in mods.iterdir()))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--save", default="zh6657", help="prefix of the player's saves; the newest one is used")
    ap.add_argument("--exact-save", action="store_true", help="use --save as the exact save folder name")
    ap.add_argument("--tests", default="all")
    ap.add_argument("--limit", type=int, default=900, help="seconds before the run is abandoned")
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--refresh-runtime", action="store_true", help="copy every game file again (after a game update)")
    a = ap.parse_args()

    ps = subprocess.run(["powershell", "-NoProfile", "-Command",
                         "Get-Process -Name 'Outer Space' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path"],
                        capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    if str(RUNTIME).lower() in ps.lower():
        sys.exit("a test copy of the game is already running (one test at a time)")
    copy_game(a.refresh_runtime)
    patch_names()
    save = a.save if a.exact_save else newest_save(a.save)
    prepare_data(save)
    install_mod(a.no_build)

    res, beat, log = RUNTIME / "selftest-result.json", RUNTIME / "selftest-heartbeat.txt", RUNTIME / "selftest-player.log"
    for p in (res, beat, log):
        if p.exists():
            p.unlink()
    (RUNTIME / "ModSelfTest.json").write_text(json.dumps({"save": save, "tests": a.tests}), encoding="utf-8")
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 7   # minimised, not activated: the player's window keeps the focus
    env = dict(os.environ, UNITY_MIXED_CALLSTACK="1")
    proc = subprocess.Popen([str(RUNTIME / "Outer Space.exe"), "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900",
                             "-logFile", str(log)], cwd=RUNTIME, startupinfo=si, env=env)
    print("started pid", proc.pid, "tests", a.tests)
    t0, frozen = time.time(), None
    while time.time() - t0 < a.limit and not res.exists() and proc.poll() is None:
        time.sleep(3)
        if beat.exists() and time.time() - beat.stat().st_mtime > 60:
            frozen = beat.read_text(encoding="utf-8", errors="replace")
            print("FROZEN: no heartbeat since", time.ctime(beat.stat().st_mtime), "| last:", frozen)
            r = subprocess.run([sys.executable, str(ROOT / "tools" / "freeze_dump.py"), str(proc.pid), str(ROOT / "local" / "dumps")],
                               capture_output=True, text=True, encoding="utf-8", errors="replace")
            print(r.stdout[-8000:], r.stderr[-1500:])
            break
    time.sleep(5)
    if proc.poll() is None:
        proc.kill()
    print("elapsed", round(time.time() - t0), "exit", proc.poll())

    tag = "[" + MOD["name"] + "]"
    mod_exceptions = 0
    if log.exists():
        lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
        quit_at = next((i for i, l in enumerate(lines) if "Application Quit" in l), len(lines))
        exc = [i for i, l in enumerate(lines) if re.match(r"^[\w.]*Exception\b", l)]
        mod_exc = [i for i in exc if any(MOD["dll"][:-4] in lines[j] for j in range(i, min(len(lines), i + 15)))]
        mod_exceptions = len(mod_exc)
        print("--- game log: %d exception stacks (%d before quit), %d with mod code ---" % (len(exc), sum(1 for i in exc if i < quit_at), mod_exceptions))
        for i in mod_exc[:3]:
            print("\n".join(lines[i:i + 12]))
        print("\n".join(l for l in lines if tag in l)[-4000:])
    print("--- result ---")
    print(res.read_text(encoding="utf-8")[-12000:] if res.exists() else "NO RESULT")
    try:
        passed = json.loads(res.read_text(encoding="utf-8")).get("passed") is True if res.exists() else False
    except ValueError:
        passed = False
    if frozen is not None:
        passed = False
        print("FAIL: the game froze")
    if mod_exceptions:
        passed = False
        print("FAIL: exceptions thrown from mod code")
    if not passed:
        sys.exit("self-test FAILED")


if __name__ == "__main__":
    main()
