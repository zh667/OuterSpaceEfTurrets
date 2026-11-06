using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EfTurrets
{
    // Marks the special towers leave on the ground (acid pool, poison pool, ice sheet, scorch), drawn the way the game
    // draws its own burn marks (FireBurnedLayer: a TextureDrawer on the map's ground layer). The pools are made by
    // tools/assets/make_ground.py from the game's terrain pictures; the scorch is the game's groundburn.png.
    // Marks fade in and out with the game clock and go away with the map.
    public static class EfGround
    {
        public static string Dir;            // v_common/Resources/Textures (set when the mod loads)
        public const int Variants = 3;
        public static int Added;             // test counter

        class Mark { public string key; public Vector2 at; public float size, life, age; }

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, TextureDrawer> drawers = new Dictionary<string, TextureDrawer>();
        static readonly List<Mark> marks = new List<Mark>();
        static GameObject root;
        static MapManager owner;

        // kind: acid, poison, frost (pools) or burn (scorch). radius in map units; the pool fills ~75% of its picture.
        public static void Add(string kind, Vector2 at, float radius, float seconds)
        {
            if (!MainThread.onMain) { MainThread.post(() => Add(kind, at, radius, seconds)); return; }   // see EfFx
            try
            {
                string key = kind == "burn" ? "burn" : kind + "_" + EfFx.Rand(Variants);
                if (Drawer(key) == null) return;
                float size = kind == "burn" ? radius * 2f : radius * 2f / 0.75f;
                marks.Add(new Mark { key = key, at = at, size = size, life = seconds });
                Added++;
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] ground mark: " + e.Message); }
        }

        static Texture2D Texture(string key)
        {
            if (textures.TryGetValue(key, out var t) && t != null) return t;
            t = null;
            if (key == "burn") t = TextureManager.loadImagePng(FairyStatic.imgRoot + "groundburn.png");
            else if (Dir != null)
            {
                string path = Path.Combine(Dir, "ef_ground_" + key + ".png");
                if (File.Exists(path))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    if (!t.LoadImage(File.ReadAllBytes(path))) t = null;
                }
                else Debug.LogWarning("[EfTurrets] missing " + path);
            }
            textures[key] = t;
            return t;
        }

        static TextureDrawer Drawer(string key)
        {
            var mm = MapManager.getInstance(); if (mm?.mapEventManager == null) return null;
            if (root == null || owner != mm) Reset(mm);
            if (drawers.TryGetValue(key, out var d) && d != null) return d;
            var tex = Texture(key); if (tex == null) return null;
            d = PlatformTools.createGameObject<TextureDrawer>(root);
            d.init();
            d.setTexture(tex);
            Light2DWorldLightUtility.SetWorldLight(d.getMaterial(), true);   // darker at night, like the ground under it
            d.setSorting(FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.MAP_GROUP), SortingDef.MAP_GROUP_EFFECT);
            drawers[key] = d;
            return d;
        }

        // a new map (or the first mark): the drawers live next to the game's burn layer (MapEventManager), in map
        // units; PlatformTools gives them the parent's render layer (a plain new GameObject is culled by the camera)
        static void Reset(MapManager mm)
        {
            marks.Clear(); drawers.Clear();
            owner = mm;
            root = PlatformTools.setPresent(mm.mapEventManager.gameObject, new GameObject("EfGround"));
        }

        // Called every frame (ModBehaviour.Update).
        public static void Update()
        {
            if (root == null) return;
            if (owner == null || owner != MapManager.getInstance()) { marks.Clear(); drawers.Clear(); if (root != null) UnityEngine.Object.Destroy(root); root = null; return; }
            float dt = TimeInfo.elapsedTime > 0 ? Time.deltaTime : 0f;
            for (int i = marks.Count - 1; i >= 0; i--) { marks[i].age += dt; if (marks[i].age >= marks[i].life) marks.RemoveAt(i); }
            foreach (var kv in drawers)
            {
                var d = kv.Value; if (d == null) continue;
                var tex = Texture(kv.Key); if (tex == null) continue;
                // a drawer with nothing to draw keeps its last mesh, so it is hidden instead
                bool any = false; foreach (var m in marks) if (m.key == kv.Key) { any = true; break; }
                if (d.gameObject.activeSelf != any) d.gameObject.SetActive(any);
                if (!any) continue;
                foreach (var m in marks)
                {
                    if (m.key != kv.Key) continue;
                    float a = Mathf.Clamp01(m.age / 0.25f) * Mathf.Clamp01((m.life - m.age) / 1.5f);
                    float s = m.size / tex.width;
                    d.drawImage(m.at.x, m.at.y, s, s, 0f, a, 3);
                }
                d.updatePaint();
            }
        }

        public static int Live => marks.Count;
    }
}
