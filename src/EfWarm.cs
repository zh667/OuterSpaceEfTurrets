using System;
using System.Collections.Generic;

namespace EfTurrets
{
    // The first use of an effect loads its prefab, copies a texture out of it and recolours it, and the first ground
    // mark of a kind decodes its picture: 6-30 ms each, about 40 of them, several in the same frame when the first fight starts (a 76 ms frame in the perf test). Once the map has
    // any of the mod's towers (EfTurret.Logic sets Wanted), they are prepared here instead, one per frame (main thread).
    public static class EfWarm
    {
        public static volatile bool Wanted;
        static Queue<Action> todo;
        public static int Total, Done;
        public static bool Finished => todo != null && todo.Count == 0;

        // Called every frame (ModBehaviour.Update).
        public static void Step()
        {
            if (!Wanted || (todo != null && todo.Count == 0)) return;
            if (MapManager.getInstance() == null || EffectManager.getInstance() == null) return;
            if (todo == null)
            {
                todo = new Queue<Action>();
                foreach (var p in EfFx.WarmPaths()) { var path = p; todo.Enqueue(() => EfFx.Prefab(path)); }
                foreach (var kv in EfLooks.Palette()) { var k = kv; todo.Enqueue(() => EfPuffs.Prepare(k.Key, k.Value)); }
                foreach (var g in EfGround.Keys()) { var key = g; todo.Enqueue(() => EfGround.Prepare(key)); }
                Total = todo.Count;
            }
            todo.Dequeue()(); Done++;
        }
    }
}
