using UnityEngine;

namespace EfTurrets
{
    // Holds the mod's per-session component. The game runs the towers (EfTurret is a Defense_BaseTurret); every frame
    // the mod only ages its effects and ground marks and keeps the placement preview upright. The self-test starts here when the isolated test asks for it.
    public class ModBehaviour : MonoBehaviour
    {
        public const string Version = "0.4.4";
        public static ModBehaviour Instance;

        void Awake()
        {
            Instance = this;
            if (SelfTest.Requested()) gameObject.AddComponent<SelfTest>();
        }

        void Update()
        {
            long m = EfPerf.Alloc();
            long t = EfPerf.Now;
            try { EfFx.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] fx update: " + e.Message); }
            EfPerf.AddMain(EfPerf.Main.Fx, t); t = EfPerf.Now;
            try { EfGround.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] ground update: " + e.Message); }
            EfPerf.AddMain(EfPerf.Main.Ground, t); t = EfPerf.Now;
            try { EfPuffs.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] puffs update: " + e.Message); }
            EfPerf.AddMain(EfPerf.Main.Puffs, t); t = EfPerf.Now;
            try { EfPlacement.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] placement: " + e.Message); }
            EfPerf.AddMain(EfPerf.Main.Placement, t); t = EfPerf.Now;
            try { EfWarm.Step(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] warm: " + e.Message); }
            EfPerf.AddMain(EfPerf.Main.Warm, t);
            EfPerf.AddMainAlloc(m);
            try { EfPerf.EndFrame(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] perf: " + e.Message); }
        }
    }
}
