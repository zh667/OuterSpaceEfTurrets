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
        public const string EmpBurst = "Eff_Groung/Bomb/Bomb_Emp";                    // white-blue shock burst
        public const string GroundFire = "Eff_Groung/Fire/Fire";                      // small burning flames (loops)
        public static int Spawned;   // test counter

        public static void Game(AnimPlayerID.Glo id, Vector2 at)
        {
            try { EffectManager.getInstance()?.addEffect_Global(id, 1, at.x, at.y); Spawned++; }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] game fx: " + e.Message); }
        }

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        class Timed { public GameObject go; public float left; }
        static readonly List<Timed> timed = new List<Timed>();

        public static GameObject Prefab(string path)
        {
            if (!prefabs.TryGetValue(path, out var p)) { try { p = PrefabLoader.getPrefab(FairyStatic.prefabRoot + path); } catch { p = null; } prefabs[path] = p; }
            return p;
        }

        // Pooled F3D effects go through the game's EffectManager; the others are instantiated on the effect layer and
        // removed after 'life' seconds (weather prefabs such as Cloud/cloud1 need their weather and must not be used).
        public static void Particle(string path, Vector2 at, float angle = 0f, float life = 2f)
        {
            try
            {
                var prefab = Prefab(path); var em = EffectManager.getInstance(); if (prefab == null || em == null) return;
                Spawned++;
                if (prefab.GetComponent<F3DAdaptiveEffect>() != null) { em.addEffect_Fight(prefab, at.x, at.y, angle); return; }
                var go = UnityEngine.Object.Instantiate(prefab, em.transform);
                go.transform.localPosition = new Vector3(at.x, at.y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                int layer = FairySortingLayerConfig.getLayerSystemId(SortingDef.LAYERS.Effect);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerID = layer; r.sortingOrder = SortingDef.Effect_Eff; }
                go.SetActive(true);
                timed.Add(new Timed { go = go, left = life });
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] particle " + path + ": " + e.Message); }
        }

        // Called every frame (ModBehaviour.Update): removes instantiated prefabs when their time is up (game clock).
        public static void Update()
        {
            if (timed.Count == 0) return;
            float dt = TimeInfo.elapsedTime > 0 ? Time.deltaTime : 0f;
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                var t = timed[i]; t.left -= dt;
                if (t.go == null) { timed.RemoveAt(i); continue; }
                if (t.left <= 0) { UnityEngine.Object.Destroy(t.go); timed.RemoveAt(i); }
            }
        }

        public static int Live => timed.Count;
    }
}
