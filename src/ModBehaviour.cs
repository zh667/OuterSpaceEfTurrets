using System;
using UnityEngine;
using EfTurrets.Kit;

namespace EfTurrets
{
    public class ModBehaviour : MonoBehaviour
    {
        public const string Version = "0.1.0";
        public static ModBehaviour Instance;

        // Example state read from the game (shown by the self-test).
        public int KnownGalaxies { get; private set; }

        void Awake()
        {
            Instance = this;
            if (SelfTest.Requested()) gameObject.AddComponent<SelfTest>();
        }

        void Update()
        {
            try
            {
                var gd = UserBean.tryGetInstance()?.gameData;
                if (gd == null) return;
                // Anything that reads or changes game objects runs under the game's locks, tried and never awaited
                // (see Kit/GameLock.cs). Keep it short: a few milliseconds per frame.
                if (GameLock.TryEnter(gd))
                {
                    try { Tick(gd); }
                    finally { GameLock.Exit(gd); }
                }
                // UI updates and file writing that only use data copied out above go here, outside the locks.
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] update: " + e); }
        }

        void Tick(FWGameData gd)
        {
            KnownGalaxies = gd.galaxyBlock?.knownGalaxies?.Count ?? 0;
        }
    }
}
