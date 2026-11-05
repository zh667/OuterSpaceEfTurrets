# Runs on Windows. Renders every tower in towers.json with Blender into local/render/<id>/ (skips finished ones).
import json, os, subprocess, sys, time
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(os.path.dirname(HERE))
BL = r"E:\Develop\Blender-5.1.0-Beta\blender.exe"
SRC = r"E:\projects\SurvivalcraftMechDefense\local\endfield\tower-library\reference-assembled"
spec = json.load(open(os.path.join(HERE, "towers.json"), encoding="utf-8"))
force = "force" in sys.argv[1:]
only = [a for a in sys.argv[1:] if a != "force"]
for t in spec["towers"]:
    if only and t["id"] not in only: continue
    out = os.path.join(ROOT, "local", "render", t["id"])
    if os.path.exists(os.path.join(out, "info.json")) and not force:
        print("skip", t["id"]); continue
    t0 = time.time()
    r = subprocess.run([BL, "-b", os.path.join(SRC, t["fam"] + ".blend"), "--python", os.path.join(HERE, "bl_dirs.py"), "--",
                        out, str(spec["elev"]), str(1 if t.get("static") else spec["dirs"]), str(spec["render_ppm"]), ",".join(t["head"]), ",".join(t["base"]) or "-"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace", creationflags=0x08000000, timeout=7200)
    ok = [l for l in r.stdout.splitlines() if l.startswith("DONE")]
    print(t["id"], t["fam"], "rc", r.returncode, ok, "%.0fs" % (time.time() - t0), flush=True)
    if r.returncode or not ok: print(r.stdout[-1500:], r.stderr[-1500:])
