using System.Collections.Generic;
using UnityEngine;

namespace EfTurrets
{
    // The second batch's effects. The rhythm follows the towers of the survival game mod (launch, flight, impact, and a
    // lasting part that keeps moving: gas over a pool, mist over the ice, crackle in the orb); the pictures are the game's
    // own: its effect prefabs where they play as they are (EfFx.Particle), and its smoke, glow, spark, crackle and ring
    // textures driven by the mod (EfPuffs) where colour, size or motion must differ. Nothing leaves a dark blot on light
    // ground. Sizes in map units (32 per cell).
    public static class EfLooks
    {
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
        static readonly Color Poison = C(176, 110, 220), PoisonGas = C(186, 140, 222);
        static readonly Color Acid = C(205, 232, 80), AcidGas = C(212, 228, 130);
        static readonly Color Ice = C(205, 236, 255), Mist = C(228, 244, 255);
        static readonly Color Spark = C(190, 225, 255), White = C(255, 255, 255), Water = C(215, 236, 255), Smoke = C(96, 92, 88);

        static Color Of(string kind) => kind == "poison" ? Poison : kind == "acid" ? Acid : Ice;
        static Color GasOf(string kind) => kind == "poison" ? PoisonGas : kind == "acid" ? AcidGas : Mist;

        // a lobbed canister bursting: a coloured cloud thrown out, a ring over the splash, a few sparks
        public static void Burst(string kind, Vector2 at, float radius)
        {
            if (kind == "frost") EfFx.Particle(EfFx.EmpBurst, at, 0f, 1.5f);
            if (kind == "acid") EfFx.Particle(EfFx.AcidHit, at);
            EfPuffs.Burst(EfPuffs.Tex.Smoke2, Of(kind), at, 10, radius * 0.7f, 24f, radius * 1.0f, 1.1f, 0.8f);
            EfPuffs.Ring(Of(kind), at, 8f, radius * 1.1f, 0.5f, 0.75f);
            EfPuffs.Burst(EfPuffs.Tex.Spark, White, at, 8, radius * 3f, 7f, 3f, 0.4f, 1f);
        }

        // the lasting part over a pool or the ice: gas or mist rising over the whole circle for 'seconds'
        public static void Cloud(string kind, Vector2 at, float radius, float seconds)
        {
            bool ice = kind == "frost";
            EfPuffs.Emit(EfPuffs.Tex.Smoke, GasOf(kind), at, radius * 0.85f, ice ? 5f : 7f, seconds, ice ? 2.6f : 2.2f, radius * 0.45f, radius * 0.95f, ice ? 3f : 7f, ice ? 0.4f : 0.5f);
        }

        // bubbles popping in a pool (now and then while it lasts): a bright pop and a small puff of gas
        public static void Bubble(string kind, Vector2 at)
        {
            EfPuffs.One(EfPuffs.Tex.Glow, Of(kind), at, new Vector2(0f, 4f), 4f, 14f, 0.45f, 0.9f);
            EfPuffs.One(EfPuffs.Tex.Smoke, GasOf(kind), at, new Vector2(0f, 10f), 8f, 26f, 1.1f, 0.55f);
        }

        // ice glinting
        public static void Glint(Vector2 at)
        {
            EfPuffs.One(EfPuffs.Tex.Spark, White, at, Vector2.zero, 10f, 4f, 0.4f, 1f);
            EfPuffs.One(EfPuffs.Tex.Glow, Ice, at, Vector2.zero, 6f, 18f, 0.4f, 0.6f);
        }

        // an electric arc arriving: a flash, crackle and sparks flying off
        public static void Zap(Vector2 at, float k = 1f)
        {
            EfPuffs.One(EfPuffs.Tex.Glow, Spark, at, Vector2.zero, 20f * k, 64f * k, 0.3f, 0.9f);
            EfPuffs.One(EfPuffs.Tex.Crackle, Spark, at, Vector2.zero, 30f * k, 52f * k, 0.35f, 1f);
            EfPuffs.Burst(EfPuffs.Tex.Spark, White, at, 7, 140f * k, 6f, 2f, 0.3f, 1f);
        }

        // the charge orb: a glow that stays, crackling all over it, sparks jumping off
        public static void Orb(Vector2 at, float radius, float seconds)
        {
            EfPuffs.Emit(EfPuffs.Tex.Glow, Spark, at, radius * 0.15f, 12f, seconds, 0.45f, 34f, 50f, 0f, 0.6f);
            EfPuffs.Emit(EfPuffs.Tex.Crackle, Spark, at, radius * 0.6f, 7f, seconds, 0.3f, 22f, 38f, 0f, 1f);
            EfPuffs.Emit(EfPuffs.Tex.Spark, White, at, radius * 0.75f, 12f, seconds, 0.3f, 6f, 2f, 0f, 1f);
        }

        // the sound wave: rings spreading from the tower over the range, dust thrown up
        public static void Wave(Vector2 at, float range)
        {
            EfPuffs.Ring(White, at, 16f, range, 0.45f, 0.45f);
            EfPuffs.Ring(Spark, at, 12f, range * 0.75f, 0.6f, 0.35f);
            EfPuffs.Ring(White, at, 8f, range * 0.5f, 0.75f, 0.25f);
            EfPuffs.Burst(EfPuffs.Tex.Smoke, Dust, at, 8, range * 0.5f, 24f, 60f, 0.8f, 0.18f);
        }
        public static void Stunned(Vector2 at)
        {
            EfPuffs.Ring(Spark, at, 6f, 30f, 0.5f, 0.9f);
            EfPuffs.One(EfPuffs.Tex.Glow, Spark, at, Vector2.zero, 10f, 26f, 0.4f, 0.7f);
        }

        // around the flamethrower's stream of flame bullets (EfSpecialTurret): a warm glow along it and light smoke
        // rolling off the far half (EfPuffs.Spray: the puffs reach the end of the range against their drag)
        public static void Flame(Vector2 from, Vector2 dir, float length, float seconds)
        {
            EfPuffs.Spray(EfPuffs.Tex.Glow, Fire, from, dir, length * 0.9f, 4f, 20f, seconds, 0.45f, 10f, length * 0.12f, 0.5f);
            EfPuffs.Spray(EfPuffs.Tex.Smoke, Smoke, from + dir * (length * 0.4f), dir, length * 0.7f, 10f, 10f, seconds, 1.2f, 20f, 70f, 0.22f);
        }

        // where the flame ends: the game's fire burst and light smoke rising over the burning ground
        public static void FlameEnd(Vector2 at, float angle)
        {
            EfFx.Particle(EfFx.FlameHit, at, angle);
            EfPuffs.Emit(EfPuffs.Tex.Smoke, Smoke, at, 22f, 6f, 1.6f, 1.6f, 20f, 54f, 20f, 0.3f);
        }

        // water: white spray from the nozzle out along the jet, droplets, a splash where it hits
        public static void Jet(Vector2 from, Vector2 dir, float length)
        {
            for (int i = 1; i <= 8; i++)
                EfPuffs.One(EfPuffs.Tex.Smoke, Water, from + dir * (length * i / 8f), dir * 120f, 10f + 3f * i, 22f + 5f * i, 0.55f, 0.75f);
            for (int i = 0; i < 10; i++)
                EfPuffs.One(EfPuffs.Tex.Spark, White, from + dir * (length * Random01()), dir * 200f, 5f, 3f, 0.35f, 1f);
        }
        public static void Splash(Vector2 at)
        {
            EfPuffs.Burst(EfPuffs.Tex.Smoke2, Water, at, 8, 80f, 14f, 40f, 0.8f, 0.7f);
            EfPuffs.Ring(Water, at, 6f, 40f, 0.45f, 0.7f);
            EfPuffs.Burst(EfPuffs.Tex.Spark, White, at, 10, 160f, 5f, 2f, 0.35f, 1f);
        }

        static float Random01() => EfFx.Rand(0f, 1f);

        // every texture and colour the looks above use (EfWarm prepares them before the first fight; the perf test
        // fails if a fight still has to make one, so a new colour must be added here)
        static readonly Color Dust = C(225, 222, 214), Fire = C(255, 170, 80);
        public static IEnumerable<KeyValuePair<EfPuffs.Tex, Color>> Palette()
        {
            foreach (var k in new[] { "poison", "acid", "frost" })
            {
                yield return P(EfPuffs.Tex.Smoke2, Of(k)); yield return P(EfPuffs.Tex.Ring, Of(k)); yield return P(EfPuffs.Tex.Smoke, GasOf(k));
                if (k != "frost") yield return P(EfPuffs.Tex.Glow, Of(k));
            }
            yield return P(EfPuffs.Tex.Spark, White); yield return P(EfPuffs.Tex.Glow, Ice);
            yield return P(EfPuffs.Tex.Glow, Spark); yield return P(EfPuffs.Tex.Crackle, Spark);
            yield return P(EfPuffs.Tex.Ring, White); yield return P(EfPuffs.Tex.Ring, Spark); yield return P(EfPuffs.Tex.Smoke, Dust);
            yield return P(EfPuffs.Tex.Glow, Fire); yield return P(EfPuffs.Tex.Smoke, Smoke);
            yield return P(EfPuffs.Tex.Smoke, Water); yield return P(EfPuffs.Tex.Smoke2, Water); yield return P(EfPuffs.Tex.Ring, Water);
        }
        static KeyValuePair<EfPuffs.Tex, Color> P(EfPuffs.Tex t, Color c) => new KeyValuePair<EfPuffs.Tex, Color>(t, c);
    }
}
