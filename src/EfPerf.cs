using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace EfTurrets
{
    // This mod's own performance log (other mods keep their own; the trade helper's is perf.log in its data folder).
    // Every 30 real seconds (while the mod has anything to do) one line goes to
    // <persistent data>/EfTurrets/EfTurrets-perf.log; the self-test also echoes it, tagged "[EfTurrets][性能]", to the
    // game's Player.log (not in normal play, so the game's log is not filled with it):
    //   main thread, per frame: the game's frame time, and what the mod's Update parts cost (effects, ground marks,
    //   puffs, placement preview) plus the work posted to the main thread from the simulation thread;
    //   simulation thread: the towers' logic (the game's own turret logic for our towers included), and inside it
    //   the special towers' attacks, lasting zones and enemy scans;
    //   how much is alive (puffs, emitters, drawers, effects, ground marks) and how much the mod allocates per thread.
    // Timers are Stopwatch ticks; the simulation thread adds with Interlocked, the main thread reads and resets.
    public static class EfPerf
    {
        public enum Main { Fx, Ground, Puffs, Placement, Warm, Posted, Count }
        public enum Sim { Logic, Attack, Lasting, Enemies, Count }
        static readonly string[] MainNames = { "特效", "地面", "粒子", "放置预览", "预热", "转交主线程" };
        static readonly string[] SimNames = { "塔逻辑", "特殊攻击", "持续区域", "找敌人" };

        public static long Now => Stopwatch.GetTimestamp();
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        // main thread
        static readonly long[] frameTicks = new long[(int)Main.Count];
        static readonly double[] mainSum = new double[(int)Main.Count], mainMax = new double[(int)Main.Count];
        static long mainAlloc;
        static int frames; static double frameSum, frameMax, modFrameMax;
        static float windowStart = -1; static int gc0 = -1;
        // simulation thread
        static readonly long[] simTicks = new long[(int)Sim.Count];
        static long simMaxCall, simCalls, attacks, scans, scanned, posts, simAlloc;

        public static void AddMain(Main c, long start) { frameTicks[(int)c] += Stopwatch.GetTimestamp() - start; }

        public static void AddSim(Sim c, long start)
        {
            long d = Stopwatch.GetTimestamp() - start;
            Interlocked.Add(ref simTicks[(int)c], d);
            if (c == Sim.Logic)
            {
                Interlocked.Increment(ref simCalls);
                long m; while (d > (m = Interlocked.Read(ref simMaxCall)) && Interlocked.CompareExchange(ref simMaxCall, d, m) != m) { }
            }
        }
        public static void Attacked() { Interlocked.Increment(ref attacks); }
        public static void Scanned(int n) { Interlocked.Increment(ref scans); Interlocked.Add(ref scanned, n); }
        // hands work to the main thread (MainThread.post) and times it there
        public static void Post(Action a)
        {
            Interlocked.Increment(ref posts);
            MainThread.post(() =>
            {
                long t = Now, m = Alloc();
                try { a(); }
                finally
                {
                    long d = Now - t; frameTicks[(int)Main.Posted] += d; AddMainAlloc(m);
                    if (d > slowPost) { slowPost = d; slowPostName = a.Method.Name; }
                }
            });
        }
        static long slowPost; static string slowPostName;

        // a one-off step that took long (preparing a texture, loading a prefab): logged at once, with what it was
        public static void Slow(string what, long start, double atLeastMs = 5)
        {
            double ms = Ms(Now - start);
            if (ms >= atLeastMs) Write(DateTime.Now.ToString("HH:mm:ss") + " 一次性准备 " + what + " 用了 " + ms.ToString("0.0") + "ms");
        }

        // bytes allocated by the calling thread (Mono has it; null where the runtime does not)
        static readonly Func<long> threadAlloc = MakeThreadAlloc();
        static Func<long> MakeThreadAlloc()
        {
            try
            {
                var m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
                if (m == null) return null;
                var f = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
                // some runtimes have the method but always answer 0: only use it if it sees a known allocation
                long a = f(); var probe = new byte[8192]; long b = f(); GC.KeepAlive(probe);
                return b - a >= 8192 ? f : null;
            }
            catch { return null; }
        }
        public static long Alloc() => threadAlloc != null ? threadAlloc() : 0;
        public static void AddMainAlloc(long start) { if (threadAlloc != null) mainAlloc += threadAlloc() - start; }
        public static void AddSimAlloc(long start) { if (threadAlloc != null) Interlocked.Add(ref simAlloc, threadAlloc() - start); }

        public static string LastLine = "";
        public static bool LastLineFresh;   // the last Flush wrote a line (false: the mod did nothing in that window)
        public static string FilePath;

        // Called once per frame from ModBehaviour.Update, after the mod's parts.
        public static void EndFrame()
        {
            double mod = 0;
            for (int i = 0; i < frameTicks.Length; i++)
            {
                double ms = Ms(frameTicks[i]); frameTicks[i] = 0;
                mainSum[i] += ms; if (ms > mainMax[i]) mainMax[i] = ms; mod += ms;
            }
            if (mod > modFrameMax) modFrameMax = mod;
            double dt = Time.unscaledDeltaTime * 1000.0;
            frameSum += dt; if (dt > frameMax) frameMax = dt; frames++;
            if (windowStart < 0) { windowStart = Time.unscaledTime; gc0 = GC.CollectionCount(0); }
            if (Time.unscaledTime - windowStart >= Window) Flush();
        }

        public static float Window = 30f;

        public static void Flush()
        {
            float secs = Mathf.Max(0.001f, Time.unscaledTime - windowStart);
            long calls = Interlocked.Exchange(ref simCalls, 0);
            var st = new long[(int)Sim.Count];
            for (int i = 0; i < st.Length; i++) st[i] = Interlocked.Exchange(ref simTicks[i], 0);
            long maxCall = Interlocked.Exchange(ref simMaxCall, 0), att = Interlocked.Exchange(ref attacks, 0);
            long sc = Interlocked.Exchange(ref scans, 0), sn = Interlocked.Exchange(ref scanned, 0), po = Interlocked.Exchange(ref posts, 0);
            long sa = Interlocked.Exchange(ref simAlloc, 0);
            double modMain = 0; for (int i = 0; i < mainSum.Length; i++) modMain += mainSum[i];
            bool idle = calls == 0 && modMain < 0.01;
            LastLineFresh = frames > 0 && !idle;
            if (frames > 0 && !idle)
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(ModBehaviour.Version).Append("  ").Append(secs.ToString("0")).Append(" 秒 ")
                  .Append(frames).Append(" 帧 平均帧 ").Append((frameSum / frames).ToString("0.0")).Append("ms（")
                  .Append((frames / secs).ToString("0")).Append(" fps） 最慢帧 ").Append(frameMax.ToString("0")).Append("ms");
                sb.Append(" | 主线程 模组每帧平均 ").Append((modMain / frames).ToString("0.000")).Append("ms 最多 ").Append(modFrameMax.ToString("0.0")).Append("ms");
                for (int i = 0; i < mainSum.Length; i++)
                    sb.Append("，").Append(MainNames[i]).Append(' ').Append((mainSum[i] / frames).ToString("0.000")).Append('/').Append(mainMax[i].ToString("0.0"));
                sb.Append(" | 后台线程 每秒 ").Append((Ms(st[(int)Sim.Logic]) / secs).ToString("0.00")).Append("ms（")
                  .Append(SimNames[0]).Append(' ').Append(calls).Append(" 次，平均 ").Append(calls > 0 ? (Ms(st[0]) * 1000 / calls).ToString("0") : "0").Append("us 最多 ")
                  .Append((Ms(maxCall)).ToString("0.00")).Append("ms）");
                for (int i = 1; i < st.Length; i++) sb.Append("，").Append(SimNames[i]).Append(' ').Append((Ms(st[i]) / secs).ToString("0.00")).Append("ms/秒");
                sb.Append("（攻击 ").Append(att).Append(" 次，找敌人 ").Append(sc).Append(" 次 平均 ").Append(sc > 0 ? (sn / (double)sc).ToString("0.0") : "0").Append(" 个）");
                sb.Append(" | 转交主线程 ").Append(po).Append(" 次");
                if (slowPostName != null) sb.Append("，最慢一次 ").Append(Ms(slowPost).ToString("0.0")).Append("ms（").Append(slowPostName).Append('）');
                sb.Append(" | 存活 粒子 ").Append(EfPuffs.Live).Append(" 发射器 ").Append(EfPuffs.Emitters).Append(" 画板 ").Append(EfPuffs.Drawers)
                  .Append(" 特效 ").Append(EfFx.Live).Append(" 地面 ").Append(EfGround.Live);
                if (threadAlloc != null)
                    sb.Append(" | 模组新分配 主线程 ").Append((mainAlloc / 1048576.0).ToString("0.00")).Append("MB 后台 ").Append((sa / 1048576.0).ToString("0.00")).Append("MB");
                else sb.Append(" | 模组新分配 （这个运行时数不了）");
                sb.Append(" | 垃圾回收 ").Append(gc0 < 0 ? 0 : GC.CollectionCount(0) - gc0).Append(" 次 内存 ").Append((GC.GetTotalMemory(false) / 1048576.0).ToString("0")).Append("MB");
                LastLine = sb.ToString();
                Write(LastLine);
            }
            Array.Clear(mainSum, 0, mainSum.Length); Array.Clear(mainMax, 0, mainMax.Length);
            frames = 0; frameSum = frameMax = modFrameMax = 0; mainAlloc = 0; slowPost = 0; slowPostName = null;
            windowStart = Time.unscaledTime; gc0 = GC.CollectionCount(0);
        }

        public static bool EchoToGameLog;   // set by the self-test

        // main thread only (persistentDataPath)
        public static void Write(string line)
        {
            if (EchoToGameLog) UnityEngine.Debug.Log("[EfTurrets][性能] " + line);
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "EfTurrets");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, "EfTurrets-perf.log");
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    var old = Path.Combine(dir, "EfTurrets-perf.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[EfTurrets] perf log: " + e.Message); }
        }
    }
}
