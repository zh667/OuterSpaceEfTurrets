# Runs ON WINDOWS. Builds the mod against the installed game's Managed DLLs and lays out dist/<folder>.
#   python tools/build.py            build only
#   python tools/build.py install    also copy it to <game>/mods/<folder> (only Manifest and v_common are replaced;
#                                    anything else there, such as a data folder, is kept; the old copy is backed up)
import hashlib, json, os, shutil, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = r"E:\SteamLibrary\steamapps\common\Outer Space"
MANAGED = os.path.join(GAME, "Outer Space_Data", "Managed")
MOD = json.load(open(os.path.join(ROOT, "mod.json"), encoding="utf-8"))


def build():
    r = subprocess.run(["dotnet", "build", os.path.join(ROOT, MOD["csproj"]), "-c", "Release", "-p:GameManaged=" + MANAGED,
                        "-nologo", "-v:minimal"], capture_output=True, text=True, cwd=ROOT, creationflags=0x08000000)
    print(r.stdout[-6000:], r.stderr[-3000:])
    print("build rc", r.returncode)
    if r.returncode != 0:
        sys.exit(1)
    dll = os.path.join(ROOT, "bin", "Release", MOD["dll"])
    dist = os.path.join(ROOT, "dist", MOD["folder"])
    shutil.rmtree(dist, ignore_errors=True)
    os.makedirs(os.path.join(dist, "Manifest"))
    os.makedirs(os.path.join(dist, "v_common", "Assemblies"))
    shutil.copy2(os.path.join(ROOT, "Manifest", "manifest.xml"), os.path.join(dist, "Manifest", "manifest.xml"))
    shutil.copy2(dll, os.path.join(dist, "v_common", "Assemblies", MOD["dll"]))
    # pictures made by tools/assets/make_assets.py (local/gen): sprite files and build-menu icons
    gen = os.path.join(ROOT, "local", "gen")
    for sub, dst in (("Sprites", ("v_common", "Sprites")), ("Textures", ("v_common", "Resources", "Textures"))):
        src = os.path.join(gen, sub)
        if not os.path.isdir(src):
            sys.exit("missing " + src + " (run tools/assets/render_all.py and make_assets.py)")
        out = os.path.join(dist, *dst); os.makedirs(out, exist_ok=True)
        files = [f for f in os.listdir(src) if f.endswith((".bin", ".png")) and ".rt." not in f]
        for f in files:
            shutil.copy2(os.path.join(src, f), os.path.join(out, f))
        print(sub, len(files), "files")
    print("dll", os.path.getsize(dll), hashlib.sha256(open(dll, "rb").read()).hexdigest())
    return dist


def install(dist):
    target = os.path.join(GAME, "mods", MOD["folder"])
    os.makedirs(target, exist_ok=True)
    bak = os.path.join(ROOT, "dist", "replaced-" + time.strftime("%Y%m%d-%H%M%S"))
    for part in ("Manifest", "v_common"):
        cur = os.path.join(target, part)
        if os.path.isdir(cur):
            shutil.copytree(cur, os.path.join(bak, part))
            shutil.rmtree(cur)
        shutil.copytree(os.path.join(dist, part), cur)
    if os.path.isdir(bak):
        print("previous install backed up to", bak)
    print("installed to", target, "kept:", sorted(set(os.listdir(target)) - {"Manifest", "v_common"}))
    print("enable it once in the game's mod menu (the game keeps the list in LocalLow/CatStudio/Outer Space/mod.dat)")


if __name__ == "__main__":
    d = build()
    if "install" in sys.argv[1:]:
        install(d)
