using UnityEngine;

namespace EfTurrets
{
    // Holds the mod's per-session component. The game runs the towers (EfTurret is a Defense_BaseTurret); every frame
    // the mod only ages its effects and ground marks and keeps the placement preview upright. The self-test starts here when the isolated test asks for it.
    public class ModBehaviour : MonoBehaviour
    {
        public const string Version = "0.4.2";
        public static ModBehaviour Instance;

        void Awake()
        {
            Instance = this;
            if (SelfTest.Requested()) gameObject.AddComponent<SelfTest>();
        }

        void Update()
        {
            try { EfFx.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] fx update: " + e.Message); }
            try { EfGround.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] ground update: " + e.Message); }
            try { EfPuffs.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] puffs update: " + e.Message); }
            try { EfPlacement.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] placement: " + e.Message); }
        }
    }
}
