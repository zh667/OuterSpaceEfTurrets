using System.Collections;
using System.Threading;
using UnityEngine;
using EfTurrets.Kit;

namespace EfTurrets
{
    // The mod's own checks in the isolated game (python tools/run_isolated.py [--tests smoke|freeze-demo]).
    // Replace the examples with checks of what the mod does; compare against the game's own functions where possible.
    public class SelfTest : SelfTestBase
    {
        protected override string ModName => "EfTurrets";
        protected override string ModVersion => ModBehaviour.Version;

        protected override string HeartbeatText() => "galaxies=" + (ModBehaviour.Instance != null ? ModBehaviour.Instance.KnownGalaxies : -1);

        protected override IEnumerator RunTests()
        {
            if (req.tests == "freeze-demo")
            {
                // Freezes the main thread on purpose, to check that run_isolated.py notices and dumps the game.
                Log("freezing the main thread for 120 s");
                Thread.Sleep(120000);
                yield break;
            }

            // Example check: the mod's Update (under the game's locks) has read the galaxy list.
            yield return new WaitForSecondsRealtime(2);
            Run(() =>
            {
                var gd = Game;
                int known = gd.galaxyBlock?.knownGalaxies?.Count ?? 0;
                int seen = ModBehaviour.Instance != null ? ModBehaviour.Instance.KnownGalaxies : -1;
                return new Check { name = "mod reads the galaxy list", passed = known > 0 && seen == known, summary = "game " + known + ", mod " + seen };
            });

            // Example measurement: frame times for 10 seconds with the clock running at x3.
            SetClock(3f);
            var f = new Frames();
            for (float t = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t < 10;) { yield return null; f.Add(Time.unscaledDeltaTime * 1000.0); }
            SetClock(0f);
            result.checks.Add(new Check { name = "frames at x3", info = true, passed = true, summary = f.Text() });
            yield return Shot("end");
        }
    }
}
