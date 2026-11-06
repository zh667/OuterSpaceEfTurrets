namespace EfTurrets
{
    // The game draws the placement preview of a mod building from its icon, turned by the building's direction
    // (BuildPlanMod.casePaint: direction 2 = 180 degrees, 3/4 = -90/90), so a tower could show up upside down or
    // sideways while being placed. Towers have no direction, so the preview of the mod's towers is kept at the one
    // direction that shows the icon upright.
    public static class EfPlacement
    {
        public static sbyte Upright = 1;   // BuildPlanMod: direction 1 = not turned (checked by --tests plan)

        // Called every frame (ModBehaviour.Update).
        public static void Update()
        {
            var g = MapManager.getInstance()?.controlManager?.controlBuild?.CurGraph;
            var def = g?.rolePlan?.planBean?.getBuildDef();
            if (def == null || def.Type != 10 || EfData.Find(def.ID) == null) return;
            if (g.getBuildDir() != Upright) g.setBuildOri(Upright);
        }
    }
}
