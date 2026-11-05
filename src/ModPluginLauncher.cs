using UnityEngine;

// The game looks this type up by its exact global name ("ModPluginLauncher") and calls launcher().
public class ModPluginLauncher : BaseModLauncher
{
    public override void launcher()
    {
        base.launcher();
        if (EfTurrets.ModBehaviour.Instance != null) return;
        var go = new GameObject("EfTurrets");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<EfTurrets.ModBehaviour>();
        Debug.Log("[EfTurrets] loaded " + EfTurrets.ModBehaviour.Version);
    }
}
