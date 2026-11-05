using UnityEngine;

namespace EfTurrets
{
    // Holds the mod's per-session component. The towers themselves need no per-frame work from the mod: the game
    // runs them (EfTurret is a Defense_BaseTurret). The self-test starts here when the isolated test asks for it.
    public class ModBehaviour : MonoBehaviour
    {
        public const string Version = "0.2.0";
        public static ModBehaviour Instance;

        void Awake()
        {
            Instance = this;
            if (SelfTest.Requested()) gameObject.AddComponent<SelfTest>();
        }

        void Update()
        {
            try { EfFx.Update(); } catch (System.Exception e) { Debug.LogWarning("[EfTurrets] fx update: " + e.Message); }
        }
    }
}
