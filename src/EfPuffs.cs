using System;
using System.Collections.Generic;
using UnityEngine;

namespace EfTurrets
{
    // The mod's own particles, drawn with the game's effect textures: smoke puffs, glows, sparks, crackle and the shock
    // ring are copied out of the game's effect prefabs (their materials' textures), recoloured once per colour, and drawn
    // as quads with a TextureDrawer on the effect layer (like EfGround draws the pools). Motion, growth, fade and colour
    // are the mod's; the game's stripped particle API cannot place or colour single particles.
    // Everything runs on the main thread (calls from the simulation thread are posted) and on the game clock.
    public static class EfPuffs
    {
        public enum Tex { Smoke, Smoke2, Glow, Spark, Ring, Crackle }
        static readonly Dictionary<Tex, KeyValuePair<string, string>> Source = new Dictionary<Tex, KeyValuePair<string, string>>
        {
            { Tex.Smoke, new KeyValuePair<string, string>("Eff_Bullets/effects/laser/Laser Hit 1", "smokePuff") },     // realistic puff
            { Tex.Smoke2, new KeyValuePair<string, string>("Eff_Bullets/effects/grenade/Hit", "Hit") },                 // denser puff
            { Tex.Glow, new KeyValuePair<string, string>("Eff_Groung/Lightning/Lightning_Boom", "Core") },             // soft centre glow
            { Tex.Spark, new KeyValuePair<string, string>("Eff_Defense/effects/bomb/Missile_Big", "spark") },          // spark dot
            { Tex.Ring, new KeyValuePair<string, string>("Eff_Bullets/effects/laser/Laser Hit 1", "laser_impulse_wave") }, // shock ring
            { Tex.Crackle, new KeyValuePair<string, string>("Eff_Bullets/effects/plasma/Plasma Hit", "core") },        // plasma crackle
        };
        static readonly HashSet<Tex> Additive = new HashSet<Tex> { Tex.Glow, Tex.Spark, Tex.Ring, Tex.Crackle };

        class Puff { public string key; public Vector2 pos, vel; public float s0, s1, life, age, a, rot, spin, rise; }
        class Emitter { public Tex tex; public Color c; public Vector2 at; public float radius, rate, left, acc, life, s0, s1, rise, a; }

        static readonly List<Puff> puffs = new List<Puff>();
        static readonly List<Emitter> emitters = new List<Emitter>();
        static readonly Dictionary<Tex, Texture2D> gray = new Dictionary<Tex, Texture2D>();
        static readonly Dictionary<string, Texture2D> tinted = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, TextureDrawer> drawers = new Dictionary<string, TextureDrawer>();
        static GameObject root; static MapManager owner;
        static readonly System.Random rng = new System.Random();
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        public static int Live => puffs.Count;

        static bool OffMain(Action a) { if (MainThread.onMain) return false; MainThread.post(a); return true; }

        // ---------------------------------------------------------------- what the towers call
        // one puff: from 'at' moving with 'vel' (map units/s), growing s0 -> s1 (map units), 'life' seconds, peak alpha a,
        // rising 'rise' units/s more each second (warm gas)
        public static void One(Tex t, Color c, Vector2 at, Vector2 vel, float s0, float s1, float life, float a, float rise = 0f)
        {
            if (OffMain(() => One(t, c, at, vel, s0, s1, life, a, rise))) return;
            string key = Key(t, c); if (Drawer(key, t, c) == null) return;
            puffs.Add(new Puff { key = key, pos = at, vel = vel, s0 = s0, s1 = s1, life = Mathf.Max(0.05f, life), a = a, rot = R(0, 360), spin = R(-40, 40), rise = rise });
        }

        // n puffs bursting out of 'at' at up to 'speed'
        public static void Burst(Tex t, Color c, Vector2 at, int n, float speed, float s0, float s1, float life, float a)
        {
            for (int i = 0; i < n; i++)
            {
                float ang = R(0, 6.283f), sp = R(0.3f, 1f) * speed;
                One(t, c, at + new Vector2(R(-4, 4), R(-4, 4)), new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f) * sp, s0 * R(0.8f, 1.2f), s1 * R(0.8f, 1.2f), life * R(0.8f, 1.2f), a);
            }
        }

        // puffs rising from random points of a circle for 'seconds' ('rate' per second)
        public static void Emit(Tex t, Color c, Vector2 at, float radius, float rate, float seconds, float life, float s0, float s1, float rise, float a)
        {
            if (OffMain(() => Emit(t, c, at, radius, rate, seconds, life, s0, s1, rise, a))) return;
            emitters.Add(new Emitter { tex = t, c = c, at = at, radius = radius, rate = rate, left = seconds, life = life, s0 = s0, s1 = s1, rise = rise, a = a, acc = 1f });
        }

        // a ring growing from r0 to r1 (radius, map units) and fading
        public static void Ring(Color c, Vector2 at, float r0, float r1, float life, float a) => One(Tex.Ring, c, at, Vector2.zero, r0 * 2f, r1 * 2f, life, a);

        // ---------------------------------------------------------------- drawing
        static string Key(Tex t, Color c) => t + "_" + Mathf.RoundToInt(c.r * 31) + "_" + Mathf.RoundToInt(c.g * 31) + "_" + Mathf.RoundToInt(c.b * 31);

        // the game texture, made readable, as a mask: grey = brightness (additive textures have a black background)
        static Texture2D Gray(Tex t)
        {
            if (gray.TryGetValue(t, out var g)) return g;
            g = null;
            try
            {
                var src = Source[t]; var prefab = EfFx.Prefab(src.Key);
                Texture tex = null;
                if (prefab != null) foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.name == src.Value && r.sharedMaterial != null) { tex = r.sharedMaterial.mainTexture; break; }
                if (tex != null)
                {
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    g = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    g.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); g.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                }
                else Debug.LogWarning("[EfTurrets] puff texture missing: " + src.Key + " / " + src.Value);
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] puff texture " + t + ": " + e.Message); }
            gray[t] = g; return g;
        }

        static Texture2D Tinted(Tex t, Color c, string key)
        {
            if (tinted.TryGetValue(key, out var x)) return x;
            var g = Gray(t); if (g == null) { tinted[key] = null; return null; }
            var px = g.GetPixels32(); bool add = Additive.Contains(t);
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i]; float lum = Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255f;
                float a = add ? lum * (p.a / 255f) : p.a / 255f;
                float shade = add ? 1f : 0.55f + 0.45f * lum;   // smoke keeps its light and shadow, glows are flat colour
                px[i] = new Color32((byte)(c.r * shade * 255), (byte)(c.g * shade * 255), (byte)(c.b * shade * 255), (byte)(Mathf.Clamp01(a) * 255));
            }
            x = new Texture2D(g.width, g.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            x.SetPixels32(px); x.Apply(false, true);
            tinted[key] = x; return x;
        }

        static TextureDrawer Drawer(string key, Tex t, Color c)
        {
            var em = EffectManager.getInstance(); var mm = MapManager.getInstance(); if (em == null || mm == null) return null;
            if (root == null || owner != mm) { puffs.Clear(); emitters.Clear(); drawers.Clear(); owner = mm; root = PlatformTools.setPresent(em.gameObject, new GameObject("EfPuffs")); }
            if (drawers.TryGetValue(key, out var d) && d != null) return d;
            var tex = Tinted(t, c, key); if (tex == null) return null;
            d = PlatformTools.createGameObject<TextureDrawer>(root);
            d.init(); d.setTexture(tex);
            d.setSorting(FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect), SortingDef.Effect_Eff);
            drawers[key] = d; return d;
        }

        // Called every frame (ModBehaviour.Update).
        public static void Update()
        {
            if (root == null) return;
            if (owner != MapManager.getInstance()) { puffs.Clear(); emitters.Clear(); drawers.Clear(); UnityEngine.Object.Destroy(root); root = null; return; }
            float dt = Time.deltaTime * MapManager.getNowSpeed();
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                var e = emitters[i]; e.left -= dt; e.acc += dt * e.rate;
                while (e.acc >= 1f)
                {
                    e.acc -= 1f; float ang = R(0, 6.283f), rr = Mathf.Sqrt(R(0, 1)) * e.radius;
                    Vector2 p = e.at + new Vector2(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr * 0.75f);
                    One(e.tex, e.c, p, new Vector2(R(-3, 3), e.rise), e.s0, e.s1, e.life, e.a);
                }
                if (e.left <= 0) emitters.RemoveAt(i);
            }
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i]; p.age += dt;
                if (p.age >= p.life) { puffs.RemoveAt(i); continue; }
                p.vel.y += p.rise * dt; p.vel *= 1f - Mathf.Min(0.9f, 1.2f * dt);
                p.pos += p.vel * dt; p.rot += p.spin * dt;
            }
            foreach (var kv in drawers)
            {
                var d = kv.Value; if (d == null) continue;
                bool any = false; foreach (var p in puffs) if (p.key == kv.Key) { any = true; break; }
                if (d.gameObject.activeSelf != any) d.gameObject.SetActive(any);
                if (!any) continue;
                var tex = tinted[kv.Key]; float w = tex.width;
                foreach (var p in puffs)
                {
                    if (p.key != kv.Key) continue;
                    float u = p.age / p.life;
                    float a = p.a * Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.5f);
                    float s = Mathf.Lerp(p.s0, p.s1, Mathf.Sqrt(u)) / w;
                    d.drawImage(p.pos.x, p.pos.y, s, s, p.rot, a, 3);
                }
                d.updatePaint();
            }
        }
    }
}
