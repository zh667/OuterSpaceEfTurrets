using System;
using System.Collections.Generic;
using UnityEngine;

namespace EfTurrets
{
    // Effects of the special towers, all from the game's own assets so they look like the game's turrets:
    //   Particle  a game prefab (realistic F3D particles: Eff_Defense / Eff_Bullets / Eff_Groung) at a map position
    //   Game      a picture from the game's effect sheet (Effect_Global: heal crosses, burning, poisoned bubble...)
    // Map positions are the game's units (32 per cell, y up); angles 0 = right, counter-clockwise.
    public static class EfFx
    {
        public const string PlasmaBurst = "Eff_Defense/effects/bomb/Plasma";          // blue electric crackle
        public const string ElectricSphere = "Eff_Defense/effects/bomb/Laser_Big";    // big electric ring
        public const string ToxicBurst = "Eff_Defense/effects/bomb/Plasma_Big";       // dark teal smoke burst
        public const string GlowOrb = "Eff_Defense/effects/bullets/Plasma";           // small cyan glow
        public const string BlueColumn = "Eff_Defense/effects/muzzle/Laser_Big";      // blue glow at a muzzle
        public const string FlameBlob = "Eff_Bullets/effects/Spue/Bullet";            // spray of fire (flamethrower shot)
        public const string FlameHit = "Eff_Bullets/effects/Spue/Hit";
        public const string AcidHit = "Eff_Bullets/effects/plasma/Plasma Hit";        // yellow-green crackle
        // the prefabs the towers' attacks load (EfWarm loads them before the first fight)
        public static IEnumerable<string> WarmPaths()
        {
            yield return EmpBurst; yield return AcidHit; yield return FlameHit; yield return FlameBlob; yield return GroundFire;
            foreach (var s in EfData.Towers) if (s.Kind == "frost" || s.Kind == "acid" || s.Kind == "poison") yield return EfSpecialTurret.BulletPath(s.Bullet);
        }
        public static readonly List<string> LoadLog = new List<string>();   // prefabs in the order they were loaded (tests)
        public const string EmpBurst = "Eff_Groung/Bomb/Bomb_Emp";                    // white-blue shock burst
        public const string GroundFire = "Eff_Groung/Fire/Fire";                      // small burning flames (loops)
        public static int Spawned;   // test counter

        // The map logic (and with it the towers) runs on the game's simulation thread since 0.9.0020; effects are
        // Unity objects, so calls from there are handed to the main thread (MainThread.post, as the game's own
        // EffectManager does). Loading a prefab off the main thread crashes the game.
        static bool OffMain(Action a)
        {
            if (MainThread.onMain) return false;
            EfPerf.Post(a);
            return true;
        }

        static readonly System.Random rng = new System.Random();
        public static float Rand(float a, float b) { lock (rng) return a + (float)rng.NextDouble() * (b - a); }
        public static int Rand(int n) { lock (rng) return rng.Next(n); }

        public static void Game(AnimPlayerID.Glo id, Vector2 at)
        {
            if (OffMain(() => Game(id, at))) return;
            try { EffectManager.getInstance()?.addEffect_Global(id, 1, at.x, at.y); Spawned++; }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] game fx: " + e.Message); }
        }

        // ------------------------------------------------------------------ the mod's own emitters from game parts
        // A Look picks parts (child particle systems, by name) of a game prefab and drives them: colour, size (scale of
        // the whole object), a loop over a circle (pools, clouds, an orb) with a rate and a drift, for a time. The game's
        // particle API is stripped down to these controls (no per-particle emit), so every look is a prefab clone.
        // Parts named "decal..." (black scorch left for 6-10 s, the black blots on light ground) are always switched off.
        public class Look
        {
            public string path; public string[] only; public Color? tint; public Dictionary<string, Color> tints;
            public float scale = 1f; public float rate = -1f, radius = -1f; public Vector2 drift; public bool loop;   // drift: only y (rise) is used
        }

        public static List<GameObject> Recent;   // self-test: the objects Spawn made, when set
        public static void Spawn(Look l, Vector2 at, float angle = 0f, float seconds = 2f)
        {
            if (OffMain(() => Spawn(l, at, angle, seconds))) return;
            try
            {
                var prefab = Prefab(l.path); var em = EffectManager.getInstance(); if (prefab == null || em == null) return;
                var go = UnityEngine.Object.Instantiate(prefab, em.transform);
                go.transform.localPosition = new Vector3(at.x, at.y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                int layer = FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    string n = ps.gameObject.name;
                    bool keep = !n.StartsWith("decal") && (l.only == null || Array.IndexOf(l.only, n) >= 0);
                    var r = ps.GetComponent<Renderer>();
                    if (!keep) { if (r != null) r.enabled = false; var e0 = ps.emission; e0.enabled = false; continue; }
                    if (r != null) { r.sortingLayerID = layer; r.sortingOrder = SortingDef.Effect_Eff; r.enabled = true; }
                    if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);   // some parts are switched off in the prefab
                    var main = ps.main;
                    // size: each system's own transform (the prefabs use local scaling; switching them to hierarchy or
                    // world simulation under the effect layer shrank them to nothing)
                    ps.transform.localScale = ps.transform.localScale * l.scale;
                    Color c;
                    if (l.tints != null && l.tints.TryGetValue(n, out c)) main.startColor = new ParticleSystem.MinMaxGradient(c);
                    else if (l.tint.HasValue) main.startColor = new ParticleSystem.MinMaxGradient(l.tint.Value);
                    if (l.loop)
                    {
                        main.loop = true;
                        var e = ps.emission; e.enabled = true; if (l.rate >= 0) e.rateOverTime = l.rate;
                        if (l.radius >= 0) { var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = l.radius / Mathf.Max(0.0001f, ps.transform.lossyScale.x); }
                    }
                    // rising: negative gravity (the stripped API cannot switch on velocity over lifetime); about drift.y
                    // map units per second over a puff's life
                    if (l.drift.y != 0f) main.gravityModifierMultiplier = -l.drift.y / 5f;
                }
                go.SetActive(true);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
                timed.Add(new Timed { go = go, left = seconds, fade = l.loop ? 2.5f : 0f });
                if (Recent != null) Recent.Add(go);
                Spawned++;
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] spawn " + l.path + ": " + e.Message); }
        }

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        class Timed { public GameObject go; public float left, fade; }   // fade: a loop stops emitting, then goes after this
        static readonly List<Timed> timed = new List<Timed>();

        public static GameObject Prefab(string path)
        {
            if (!prefabs.TryGetValue(path, out var p))
            {
                long t0 = EfPerf.Now;
                try { p = PrefabLoader.getPrefab(FairyStatic.prefabRoot + path); } catch { p = null; }
                prefabs[path] = p; EfPerf.Slow("特效预制体 " + path, t0); LoadLog.Add(path);
            }
            return p;
        }

        // Pooled F3D effects go through the game's EffectManager; the others are instantiated on the effect layer and
        // removed after 'life' seconds (weather prefabs such as Cloud/cloud1 need their weather and must not be used).
        public static void Particle(string path, Vector2 at, float angle = 0f, float life = 2f)
        {
            if (OffMain(() => Particle(path, at, angle, life))) return;
            try
            {
                var prefab = Prefab(path); var em = EffectManager.getInstance(); if (prefab == null || em == null) return;
                Spawned++;
                if (prefab.GetComponent<F3DAdaptiveEffect>() != null) { em.addEffect_Fight(prefab, at.x, at.y, angle); return; }
                var go = UnityEngine.Object.Instantiate(prefab, em.transform);
                go.transform.localPosition = new Vector3(at.x, at.y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                int layer = FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerID = layer; r.sortingOrder = SortingDef.Effect_Eff; if (r.gameObject.name.StartsWith("decal")) r.enabled = false; }
                go.SetActive(true);
                timed.Add(new Timed { go = go, left = life });
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] particle " + path + ": " + e.Message); }
        }

        // A shell on the mod's own arc (the pitched launchers): from 'from' to 'to' in 'seconds' of game time, 'apex' map
        // units above the straight line at the middle, the picture turned along the arc like the game's bullets.
        class Flying { public GameObject go; public Vector2 from, to; public float apex, time, age; }
        static readonly List<Flying> flying = new List<Flying>();
        public static void Lob(string path, Vector2 from, Vector2 to, float apex, float seconds)
        {
            if (OffMain(() => Lob(path, from, to, apex, seconds))) return;
            try
            {
                var prefab = Prefab(path); var em = EffectManager.getInstance(); if (prefab == null || em == null) return;
                var go = UnityEngine.Object.Instantiate(prefab, em.transform);
                go.transform.localPosition = new Vector3(from.x, from.y, 0f);
                int layer = FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerID = layer; r.sortingOrder = SortingDef.Effect_Eff; }
                go.SetActive(true);
                flying.Add(new Flying { go = go, from = from, to = to, apex = apex, time = Mathf.Max(0.05f, seconds) });
                Spawned++;
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] lob " + path + ": " + e.Message); }
        }
        static Vector2 ArcAt(Flying f, float u) => Vector2.Lerp(f.from, f.to, u) + new Vector2(0f, 4f * f.apex * u * (1f - u));

        // Called every frame (ModBehaviour.Update): moves the shells, removes instantiated prefabs when their time is up
        // (game clock).
        public static void Update()
        {
            if (flying.Count > 0)
            {
                float gdt = Time.deltaTime * MapManager.getNowSpeed();
                for (int i = flying.Count - 1; i >= 0; i--)
                {
                    var f = flying[i];
                    if (f.go == null) { flying.RemoveAt(i); continue; }
                    f.age += gdt; float u = Mathf.Clamp01(f.age / f.time);
                    Vector2 p = ArcAt(f, u), q = ArcAt(f, Mathf.Min(1f, u + 0.02f));
                    f.go.transform.localPosition = new Vector3(p.x, p.y, 0f);
                    if ((q - p).sqrMagnitude > 0.01f) f.go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg - 90f);
                    if (u >= 1f) { UnityEngine.Object.Destroy(f.go); flying.RemoveAt(i); }
                }
            }
            if (timed.Count == 0) return;
            float dt = TimeInfo.elapsedTime > 0 ? Time.deltaTime : 0f;
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                var t = timed[i]; t.left -= dt;
                if (t.go == null) { timed.RemoveAt(i); continue; }
                if (t.left > 0) continue;
                if (t.fade > 0)
                {
                    foreach (var ps in t.go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    t.left = t.fade; t.fade = 0; continue;
                }
                UnityEngine.Object.Destroy(t.go); timed.RemoveAt(i);
            }
        }

        public static int Live => timed.Count + flying.Count;
    }
}
