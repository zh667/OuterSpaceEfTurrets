using UnityEngine;

// The game looks this type up by its exact global name ("ModPluginLauncher") and calls launcher().
public class ModPluginLauncher : BaseModLauncher
{
    static bool hooked;

    public override void launcher()
    {
        base.launcher();
        setDefLoader(new EfTurrets.EfDefLoader(config));
        if (!hooked) { ModDelegateDispatch.generator_BuildClass += EfTurrets.EfTurret.Create; hooked = true; }
        if (EfTurrets.ModBehaviour.Instance != null) return;
        var go = new GameObject("EfTurrets");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<EfTurrets.ModBehaviour>();
        Debug.Log("[EfTurrets] loaded " + EfTurrets.ModBehaviour.Version);
    }
}
