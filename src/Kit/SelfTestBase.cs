using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace EfTurrets.Kit
{
    // Self-test inside the isolated copy of the game (tools/run_isolated.py). It runs only when
    // <game>/ModSelfTest.json exists, and refuses to do anything unless the game keeps its data in the test folder
    // (the copy's product name is "OS ModTests"), so it never touches the player's saves. It loads the requested save,
    // runs the mod's tests (RunTests), writes <game>/selftest-result.json and quits. A heartbeat file is written every
    // few seconds; run_isolated.py treats a stopped heartbeat as a frozen game and dumps it (tools/freeze_dump.py).
    public abstract class SelfTestBase : MonoBehaviour
    {
        public const string TestProduct = "OS ModTests";

        public class Request { public string save = "zh6657"; public string tests = "all"; }

        public class Check
        {
            public string name;
            public bool passed;
            public bool info;              // measured and reported, not part of pass/fail
            public string summary;
            public List<string> details = new List<string>();
        }

        public class Result
        {
            public string mod, version;
            public string startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            public string dataPath, save;
            public bool passed;
            public string error;
            public List<Check> checks = new List<Check>();
            public Dictionary<string, object> perf = new Dictionary<string, object>();
            public List<string> screenshots = new List<string>();
        }

        protected static string GameDir => Path.GetDirectoryName(Application.dataPath);
        static string RequestPath => Path.Combine(GameDir, "ModSelfTest.json");
        public static bool Requested() { try { return File.Exists(RequestPath); } catch { return false; } }

        protected Request req;
        protected readonly Result result = new Result();
        protected abstract string ModName { get; }
        protected abstract string ModVersion { get; }

        // The mod's tests, after the save has loaded. Use Run for synchronous checks, Guard for coroutines.
        protected abstract IEnumerator RunTests();

        // Extra text for the heartbeat line (e.g. counters that show progress).
        protected virtual string HeartbeatText() => "";

        protected void Log(string s) { Debug.Log("[" + ModName + "] selftest: " + s); }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            result.mod = ModName; result.version = ModVersion;
            try { req = JsonConvert.DeserializeObject<Request>(File.ReadAllText(RequestPath)) ?? new Request(); }
            catch { req = new Request(); }
            result.save = req.save;
            result.dataPath = Application.persistentDataPath;
            Log("data path " + result.dataPath);
            if (!result.dataPath.Replace('\\', '/').EndsWith("/" + TestProduct))
            {
                Finish("refused: data path is not the isolated test folder");
                yield break;
            }
            StartCoroutine(Heartbeat());

            ArchiveInfo info = null;
            float t0 = Time.realtimeSinceStartup;
            while (info == null && Time.realtimeSinceStartup - t0 < 300)
            {
                yield return new WaitForSecondsRealtime(3);
                if (Time.realtimeSinceStartup < 15 || UserBean.tryGetInstance() == null) continue;
                try
                {
                    var list = ArchiveManager.scanUserDatas(false);
                    if (list != null) info = list.Find(a => a != null && a.name == req.save);
                }
                catch (Exception e) { Log("archive list not ready: " + e.Message); }
            }
            if (info == null) { Finish("save " + req.save + " not found in the test folder"); yield break; }
            // A save that used a mod which is not installed here makes the game ask whether to load it anyway
            // (UserBean.loadArchive), which would stop the test. Drop such entries from this in-memory copy only.
            try
            {
                if (info.containsMods != null)
                {
                    int before = info.containsMods.Count;
                    var installed = InstalledPackageIds();
                    info.containsMods.RemoveAll(m => m == null || !installed.Contains(m.Split('_')[0]));
                    if (info.containsMods.Count != before) Log("save lists " + (before - info.containsMods.Count) + " mod(s) not installed in the test copy; ignored");
                }
            }
            catch (Exception e) { Log("mod list check: " + e.Message); }
            Log("loading " + info.name);
            float tl = Time.realtimeSinceStartup;
            UserBean.getInstance().loadArchive(info);
            while (UserBean.tryGetInstance()?.gameData == null && Time.realtimeSinceStartup - tl < 300) yield return new WaitForSecondsRealtime(1);
            if (UserBean.tryGetInstance()?.gameData == null) { yield return Shot("load-timeout"); Finish("the save did not load in 300 s"); yield break; }
            yield return new WaitForSecondsRealtime(5);   // let the world settle in
            result.perf["loadSeconds"] = Math.Round(Time.realtimeSinceStartup - tl, 1);

            yield return Guard(RunTests(), "tests");
            Finish(null);
        }

        // packageIds of the mods in <game>/mods (read from their manifests; the game's own list is internal).
        static HashSet<string> InstalledPackageIds()
        {
            var ids = new HashSet<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(Path.Combine(GameDir, "mods")))
                {
                    var manifest = Path.Combine(dir, "Manifest", "manifest.xml");
                    if (!File.Exists(manifest)) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest), "<packageId>\\s*([^<\\s]+)\\s*</packageId>");
                    if (m.Success) ids.Add(m.Groups[1].Value);
                }
            }
            catch { }
            return ids;
        }

        IEnumerator Heartbeat()
        {
            string path = Path.Combine(GameDir, "selftest-heartbeat.txt");
            while (true)
            {
                try { File.WriteAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + HeartbeatText()); } catch { }
                yield return new WaitForSecondsRealtime(5);
            }
        }

        protected static FWGameData Game => UserBean.tryGetInstance()?.gameData;

        // Runs game calls under the game's locks, in the game's order. Blocking is fine in tests; holding the locks
        // across frames is not, so keep each call synchronous.
        protected static void Locked(Action a)
        {
            var gd = Game;
            GameLock.Enter(gd);
            try { a(); } finally { GameLock.Exit(gd); }
        }

        // A synchronous check under the game's locks.
        protected void Run(Func<Check> f)
        {
            var gd = Game;
            GameLock.Enter(gd);
            try { result.checks.Add(f()); }
            catch (Exception e) { result.checks.Add(new Check { name = f.Method.Name, passed = false, summary = "exception: " + e }); }
            finally { GameLock.Exit(gd); }
        }

        // Runs a test coroutine; an exception ends that test as failed instead of stopping the whole self-test (and
        // the game, which reports exceptions to its developers, does not see it).
        protected IEnumerator Guard(IEnumerator e, string name)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!e.MoveNext()) yield break;
                    cur = e.Current;
                }
                catch (Exception ex)
                {
                    var c = result.checks.Find(x => x.name == name);
                    if (c == null) { c = new Check { name = name }; result.checks.Add(c); }
                    c.passed = false;
                    c.summary = "exception: " + ex.GetType().Name + ": " + ex.Message;
                    c.details.Add(ex.StackTrace ?? "");
                    yield break;
                }
                yield return cur;
            }
        }

        // Frame statistics over a stretch of frames (call Add every frame with Time.unscaledDeltaTime * 1000).
        public class Frames
        {
            public int n, over50, over100, over250; public double sum, worst; public long allocated;
            readonly int gc0 = GC.CollectionCount(0); readonly float t0 = Time.realtimeSinceStartup;
            long lastMem = GC.GetTotalMemory(false); int lastGc = GC.CollectionCount(0);
            public void Add(double ms)
            {
                n++; sum += ms; if (ms > worst) worst = ms; if (ms > 50) over50++; if (ms > 100) over100++; if (ms > 250) over250++;
                long mem = GC.GetTotalMemory(false); int g = GC.CollectionCount(0);
                if (g == lastGc && mem > lastMem) allocated += mem - lastMem;
                lastMem = mem; lastGc = g;
            }
            public string Text() => (Time.realtimeSinceStartup - t0).ToString("0.0") + " s, " + n + " frames, average " + (sum / Math.Max(1, n)).ToString("0.0")
                + " ms, worst " + worst.ToString("0") + " ms, over 50/100/250 ms: " + over50 + "/" + over100 + "/" + over250
                + ", garbage collections " + (GC.CollectionCount(0) - gc0) + ", allocated " + (allocated / 1048576) + " MB";
        }

        // Game clock speed (0 pauses; a loaded save starts paused). The game allows 0 to 100.
        protected static void SetClock(float speed) { MapManager.setLogicSpeed(speed); }

        protected IEnumerator Shot(string label)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                if (Screen.width <= 0 || Screen.height <= 0) { result.screenshots.Add(label + ": no screen"); yield break; }
                var p = Path.Combine(GameDir, "selftest-" + label + ".png");
                tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply(false);
                File.WriteAllBytes(p, ImageConversion.EncodeToPNG(tex));
                result.screenshots.Add(p);
            }
            catch (Exception e) { result.screenshots.Add(label + ": " + e.Message); }
            finally { if (tex != null) Destroy(tex); }
        }

        // Hide or tear down the mod's UI before the game quits (text meshes disabled after their materials are gone throw).
        protected virtual void BeforeQuit() { }

        protected void Finish(string error)
        {
            result.error = error;
            bool ok = error == null;
            foreach (var c in result.checks) if (!c.info && !c.passed) ok = false;
            result.passed = ok;
            try { File.WriteAllText(Path.Combine(GameDir, "selftest-result.json"), JsonConvert.SerializeObject(result, Formatting.Indented)); }
            catch (Exception e) { Debug.LogWarning("[" + ModName + "] selftest result write failed: " + e); }
            Log("finished, passed=" + ok + (error != null ? " error=" + error : ""));
            try { BeforeQuit(); } catch { }
            Application.Quit();
        }
    }
}
