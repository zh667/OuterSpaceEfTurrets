using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EfTurrets.Kit;

namespace EfTurrets
{
    // Checks in the isolated copy of the game (python tools/run_isolated.py [--tests all|defs]):
    //  defs    the towers, tech nodes and unlock entries are in the game's tables, parsed and linked; still locked
    //  place   every tower can be built on the loaded map, loads its own pictures (50 actions) and is drawn
    //  fight   each tower in turn gets hostile monsters in range and must hit them (shots, damage); screenshots
    // Placing and fighting only happen in the test copy's save; power is not needed for the test (TestPower).
    public class SelfTest : SelfTestBase
    {
        protected override string ModName => "EfTurrets";
        protected override string ModVersion => ModBehaviour.Version;

        readonly List<EfTurret> placed = new List<EfTurret>();
        readonly List<int[]> used = new List<int[]>();
        readonly List<Vector2> people = new List<Vector2>();   // the player's people at the start: test sites keep away from them

        // The save may open with the offline report; it would cover every screenshot.
        void CloseDialogs()
        {
            try { foreach (var d in UnityEngine.Object.FindObjectsOfType<OfflineDialog>()) d.closeDialog(); }
            catch (Exception e) { Log("close dialogs: " + e.Message); }
        }
        string monsterId;

        protected override string HeartbeatText() => "placed=" + placed.Count + " shots=" + EfData.Towers.Sum(t => EfTurret.Shots(t.Id));

        protected override IEnumerator RunTests()
        {
            if (req.tests == "freeze-demo") { Log("freezing 120 s"); System.Threading.Thread.Sleep(120000); yield break; }
            yield return new WaitForSecondsRealtime(2);
            Run(CheckDefs);
            Run(CheckTechs);
            if (req.tests == "defs") yield break;

            if (req.tests == "techtree") { yield return Guard(TechTree(), "tech tree"); yield break; }
            if (req.tests == "ground") { yield return Guard(Ground(), "ground marks"); yield break; }
            if (req.tests == "plan") { yield return Guard(Plan(), "placement preview"); yield break; }
            if (req.tests == "power") { yield return Guard(Power(), "power"); yield break; }
            if (req.tests == "fxgallery" || req.tests == "fxgallery2") { yield return Guard(FxGallery(), "fx gallery"); yield break; }
            EfTurret.TestPower = true;
            CloseDialogs();
            Locked(() =>
            {
                var f = MapManager.getInstance().heroManager.getAllianceFaction(1);
                if (f != null) foreach (var h in f.getRoles()) if (h != null) people.Add(new Vector2(h.cellX, h.cellY));
            });
            Run(PlaceAll);
            yield return new WaitForSecondsRealtime(2);
            Run(CheckPictures);
            if (placed.Count > 0)
            {
                Locked(() => CamearControl.getInstance().moveFollow((SpriteObject)placed[0]));
                yield return new WaitForSecondsRealtime(2);
                yield return Shot("row");
            }
            if (req.tests == "place") yield break;

            Run(PickMonster);
            EfTurret.TestPowerId = "-";
            foreach (var t in placed.ToList())
            {
                if (EfData.Find(t.buildDef.ID).Kind == "medic") continue;
                yield return Guard(Fight(t), "fight " + t.buildDef.ID);
            }
            yield return Guard(Medic(), "medic");
            result.checks.Add(new Check { name = "game effects", info = true, passed = true, summary = "played " + EfFx.Spawned + ", still timed at the end " + EfFx.Live + "; ground marks " + EfGround.Added + ", still on the ground " + EfGround.Live });
            result.checks.Add(new Check { name = "ground marks", passed = EfGround.Added > 0, summary = EfGround.Added + " marks laid (acid, poison, ice, scorch)" });
            SetClock(0f);
            yield return Shot("end");
        }

        Check CheckDefs()
        {
            var c = new Check { name = "towers are in the game's table" };
            int ok = 0;
            foreach (var s in EfData.Towers)
            {
                var d = ImplDef<F_DefenseDef>.getDataID(s.Id);
                if (d == null) { c.details.Add(s.Id + " missing"); continue; }
                var bad = new List<string>();
                if (d.Name != s.Name) bad.Add("name " + d.Name);
                if (d.Range != s.Range * 32 + 16) bad.Add("range " + d.Range);
                if (d.ATK != s.Atk) bad.Add("atk " + d.ATK);
                if (d.Width != s.Size || d.Height != s.Size) bad.Add("size " + d.Width + "x" + d.Height);
                if (d.TechLevel != s.Tier) bad.Add("tier " + d.TechLevel);
                if (d.getRewardBean() == null || d.getRewardBean().Count == 0 || d.getRewardBean().Any(m => m.getDef() == null)) bad.Add("materials " + d.Materials);
                if (EfSprites.PlayerPath(s.Id) == null) bad.Add("no sprite");
                c.details.Add(s.Id + " " + d.Name + " T" + d.TechLevel + " " + d.Width + "x" + d.Height + " atk " + d.ATK + " range " + d.Range
                    + " reload " + d.Reload + " bullets " + d.Max_Bullet + " power " + d.Consume + (bad.Count > 0 ? "  BAD: " + string.Join(", ", bad) : ""));
                if (bad.Count == 0) ok++;
            }
            c.passed = ok == EfData.Towers.Count && EfSprites.Problem == null;
            c.summary = ok + "/" + EfData.Towers.Count + " towers correct" + (EfSprites.Problem != null ? "; sprites: " + EfSprites.Problem : "")
                + "; loader: " + string.Join("; ", EfDefLoader.Report);
            return c;
        }

        Check CheckTechs()
        {
            var c = new Check { name = "tech nodes unlock the towers" };
            int ok = 0;
            foreach (var t in EfData.Techs)
            {
                var d = ImplDef<TechnologyDef>.getDataID(t.Id);
                if (d == null) { c.details.Add(t.Id + " missing"); continue; }
                var bad = new List<string>();
                if (d.parentNodes == null || !d.parentNodes.Any(p => p.ID == t.Pre)) bad.Add("not linked to " + t.Pre);
                foreach (var s in EfData.Towers.Where(x => x.Tech == t.Id))
                {
                    if (!d.unlockBuilds.Contains(EfData.UnlockPrefix + s.Id)) bad.Add("does not unlock " + s.Id);
                    var item = ImplDef<TechnologyUnlockItemDef>.getDataID(EfData.UnlockPrefix + s.Id);
                    if (item == null) bad.Add("no unlock entry " + s.Id);
                    var def = ImplDef<F_DefenseDef>.getDataID(s.Id);
                    bool researched = UserBean.getInstance().techonology_isUnLock(t.Id);
                    if (def != null && def.isUnlock() != researched) bad.Add(s.Id + " unlocked=" + def.isUnlock() + " while researched=" + researched);
                }
                c.details.Add(t.Id + " " + d.Name + " at " + d.HexGridX + "," + d.HexGridY + " after " + t.Pre + " unlocks " + d.Building_unlock
                    + (bad.Count > 0 ? "  BAD: " + string.Join(", ", bad) : ""));
                if (bad.Count == 0) ok++;
            }
            c.passed = ok == EfData.Techs.Count;
            c.summary = ok + "/" + EfData.Techs.Count + " nodes correct";
            return c;
        }

        // Builds every tower on its own free patch of the current map (alliance 1 = the player's), spiralling out from
        // the map centre; patches do not overlap and leave room around each tower.
        Check PlaceAll()
        {
            var c = new Check { name = "towers can be built and drawn" };
            var spread = UserBean.getInstance().curStarMap;
            used.Clear();
            var why = new Dictionary<string, int>();
            foreach (var s in EfData.Towers)
            {
                var def = ImplDef<F_DefenseDef>.getDataID(s.Id);
                int w = s.Size + 4;
                if (!FindFreeArea(w, w, used, def, why, out int x0, out int y0)) { c.details.Add(s.Id + ": no free " + w + "x" + w + " patch"); continue; }
                used.Add(new[] { x0, y0, w, w });
                short cx = (short)(x0 + 2 + s.Size / 2), cy = (short)(y0 + 2 + s.Size / 2);
                var bean = BuildGenerator.createBean_Defend(def, 1, s.Id, cx, cy, 0);
                var b = spread.addBuild(bean, cx, cy, 0) as BaseBuild;
                var t = b as EfTurret;
                c.details.Add(s.Id + " at " + cx + "," + cy + " -> " + (b == null ? "null" : b.GetType().Name));
                if (t != null) placed.Add(t);
            }
            c.details.Add("map " + MapGlobal.CellRow + "x" + MapGlobal.CellLine + ", rejected cells: " + string.Join(", ", why.Select(kv => kv.Key + " " + kv.Value)));
            c.passed = placed.Count == EfData.Towers.Count;
            c.summary = placed.Count + "/" + EfData.Towers.Count + " built as EfTurret";
            return c;
        }

        bool FindFreeArea(int w, int h, List<int[]> used, BaseDef probe, Dictionary<string, int> why, out int x0, out int y0, int cx = -1, int cy = -1, bool awayFromPeople = true)
        {
            int rows = MapGlobal.CellRow, lines = MapGlobal.CellLine;
            if (cx < 0) { cx = rows / 2; cy = lines / 2; }
            for (int r = 0; r < Math.Max(rows, lines); r += 2)
                for (int a = 0; a < Math.Max(8, r); a++)
                {
                    double ang = a * 2 * Math.PI / Math.Max(8, r);
                    x0 = cx + (int)(r * Math.Cos(ang)) - w / 2; y0 = cy + (int)(r * Math.Sin(ang)) - h / 2;
                    int xx = x0, yy = y0;
                    if (used.Any(u => xx < u[0] + u[2] && xx + w > u[0] && yy < u[1] + u[3] && yy + h > u[1])) continue;
                    if (awayFromPeople && people.Any(p => Mathf.Abs(p.x - (xx + w / 2)) < 25 && Mathf.Abs(p.y - (yy + h / 2)) < 25)) continue;
                    if (AreaFree(x0, y0, w, h, probe, why)) return true;
                }
            x0 = y0 = 0; return false;
        }

        static bool AreaFree(int x0, int y0, int w, int h, BaseDef probe, Dictionary<string, int> why)
        {
            for (int i = x0; i < x0 + w; i++)
                for (int j = y0; j < y0 + h; j++)
                {
                    string r = null;
                    if (!MapTools.isCellInRealMapRect(i, j)) r = "outside";
                    else r = CellProblem(i, j, probe);
                    if (r != null) { why.TryGetValue(r, out int k); why[r] = k + 1; return false; }
                }
            return true;
        }

        // Why a cell is no good for the test (null = fine): outside the map, fogged, open space outside a ship's hull
        // ("透明" ground), or the game would not let a building stand there.
        static string CellProblem(int i, int j, BaseDef probe)
        {
            if (!MapTools.isCellInRealMapRect(i, j)) return "outside";
            var n = MapGrid.getGridNode(i, j);
            if (n == null) return "no node";
            if (n.isInFog()) return "fog";
            string g = n.groundDef?.Name;
            if (g != null && g.Contains("透明")) return "open space";
            if (!n.canBuild_Build(probe, -1L, 0)) return "cannot build";
            return null;
        }

        Check CheckPictures()
        {
            var c = new Check { name = "towers load their own pictures" };
            int ok = 0;
            foreach (var t in placed)
            {
                var p = t.AnimPlayer?.player; var ag = p?.ag;
                int actions = ag?.actions?.Length ?? -1;
                bool tex = ag?.imgSource != null && ag.imgSource.Length > 0 && ag.imgSource[0] != null;
                string path = ag?.path;
                c.details.Add(t.buildDef.ID + " actions " + actions + " texture " + (tex ? ag.imgSource[0].width + "x" + ag.imgSource[0].height : "missing") + " path " + path);
                if (actions == 50 && tex) ok++;
            }
            c.passed = ok == placed.Count && placed.Count > 0;
            c.summary = ok + "/" + placed.Count + " with 50 actions and a texture";
            return c;
        }

        Check PickMonster()
        {
            var c = new Check { name = "test monster", info = true, passed = true };
            var list = ImplDef<MonsterDef>.beans;
            monsterId = list.Count > 0 ? list[0].ID : null;
            c.summary = "using monster " + monsterId + " (" + (list.Count > 0 ? list[0].Name : "-") + "), " + list.Count + " kinds";
            return c;
        }

        // Spawns three hostile monsters inside the tower's range (beyond the mortar's dead zone), runs the clock and
        // counts the tower's shots and the monsters' lost health.
        IEnumerator Fight(EfTurret t)
        {
            yield return FightOnce(t, 0);
            var last = result.checks[result.checks.Count - 1];
            if (!last.passed)
            {
                // obstacles stop the game's bullets, and the player's people sometimes kill the monsters first: try once
                // more from the other side
                result.checks.RemoveAt(result.checks.Count - 1);
                yield return FightOnce(t, 180);
                result.checks[result.checks.Count - 1].details.Add("second try (first one hit nothing: " + last.summary + ")");
            }
        }

        IEnumerator FightOnce(EfTurret t, int angleOffset)
        {
            var s = EfData.Find(t.buildDef.ID);
            var c = new Check { name = "fight " + s.Id + " " + s.Name };
            result.checks.Add(c);
            if (monsterId == null) { c.summary = "no monster kind"; yield break; }
            int shots0 = EfTurret.Shots(s.Id);
            int effects0 = EfTurret.Effects(s.Id);
            var spawned = new List<FWMonsterBean>();
            int dist = Math.Max(s.MinRangeCells + 3, Math.Min(s.Range - 4, 10));
            EfTurret.TestPowerId = s.Id;
            Locked(() =>
            {
                for (int a = 0; a < 360 && spawned.Count < 3; a += 15)
                {
                    double ang = (a + angleOffset) * Math.PI / 180;
                    short mx = (short)(t.cellX + (int)Math.Round(dist * Math.Cos(ang))), my = (short)(t.cellY + (int)Math.Round(dist * Math.Sin(ang)));
                    if (CellProblem(mx, my, null) != null) continue;
                    spawned.Add(GMCommand.getInstance().Monster_AddMoster(monsterId, 2, mx, my));
                    a += 30;
                }
                CamearControl.getInstance().moveFollow((SpriteObject)t);
            });
            if (spawned.Count == 0) { c.summary = "no free cell for monsters within " + dist + " cells"; EfTurret.TestPowerId = null; yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            float hp0 = HpOf(spawned);
            SetClock(1f);
            float t0 = Time.realtimeSinceStartup; bool shotTaken = false;
            while (Time.realtimeSinceStartup - t0 < 14)
            {
                yield return null;
                int shotsNow = EfTurret.Shots(s.Id);
                if (!shotTaken && shotsNow > shots0 && Time.realtimeSinceStartup - t0 > 2)
                {
                    shotTaken = true; CloseDialogs();
                    if (s.Kind == null) { yield return new WaitForSecondsRealtime(0.12f); yield return Shot("fight-" + s.Id); }
                    else
                    {
                        // a short sequence, so the whole effect can be judged (shells land after up to ~1 s)
                        bool lob = s.Kind == "frost" || s.Kind == "acid" || s.Kind == "poison";
                        float[] at = lob ? new[] { 0.1f, 0.5f, 1.0f, 1.6f } : new[] { 0.08f, 0.25f, 0.45f, 0.8f };
                        float start = Time.realtimeSinceStartup; char tag = 'a';
                        foreach (float w in at)
                        {
                            while (Time.realtimeSinceStartup - start < w) yield return null;
                            yield return Shot("fx-" + s.Id + "-" + tag); tag++;
                        }
                        yield return Shot("fight-" + s.Id);
                    }
                }
            }
            SetClock(0f);
            EfTurret.TestPowerId = "-";
            float hp1 = HpOf(spawned);
            int shots = EfTurret.Shots(s.Id) - shots0;
            if (!shotTaken) yield return Shot("fight-" + s.Id);
            Locked(() =>
            {
                foreach (var m in MapManager.getInstance().monsterManager.getMonsters().ToList())
                    if (spawned.Contains(m.monsterBean)) GMCommand.getInstance().Monster_RemoveMoster(m);
            });
            int effects = EfTurret.Effects(s.Id) - effects0;
            // the player's people join the fight, so lost health alone does not prove the tower did it: special towers
            // count what they did themselves (hits, pushes, slows, stuns); bullet towers count shots and lost health
            c.passed = shots > 0 && (s.Kind == null ? hp1 < hp0 : effects > 0);
            c.summary = (s.Kind ?? "bullets") + ": " + spawned.Count + " monsters, shots " + shots + ", effects " + effects + ", monster health " + hp0.ToString("0") + " -> " + hp1.ToString("0") + " at " + dist + " cells, direction " + t.Direction;
        }

        // Medic: built next to one of the player's people; that person and the medic itself are hurt, then both must
        // be healed / repaired within a few game seconds.
        IEnumerator Medic()
        {
            var c = new Check { name = "medic heals people and repairs buildings" }; result.checks.Add(c);
            BaseHero hero = null; EfTurret medic = null; float hp0 = 0, hpLow = 0, b0 = 0, bLow = 0;
            Locked(() =>
            {
                var f = MapManager.getInstance().heroManager.getAllianceFaction(1);
                hero = f?.getRoles().FirstOrDefault(h => h?.roleBean != null && !h.roleBean.isDead() && !h.roleBean.isFallDown());
                if (hero == null) return;
                var def = ImplDef<F_DefenseDef>.getDataID("7205");
                if (!FindFreeArea(4, 4, used, def, new Dictionary<string, int>(), out int x0, out int y0, hero.cellX, hero.cellY, false)) return;
                used.Add(new[] { x0, y0, 4, 4 });
                short cx = (short)(x0 + 2), cy = (short)(y0 + 2);
                var b = UserBean.getInstance().curStarMap.addBuild(BuildGenerator.createBean_Defend(def, 1, "7205", cx, cy, 0), cx, cy, 0) as EfTurret;
                medic = b;
                if (b == null) return;
                EfCombat.Stun(hero, 14f);   // keep the person in the medic's range during the test
                hp0 = (float)hero.roleBean.attr_Hp.hp;
                hero.roleBean.attr_Hp.hp = hero.roleBean.getMaxHp() * 0.4; hpLow = (float)hero.roleBean.attr_Hp.hp;
                b.subHp(b.buildDef.HP * 0.5f); bLow = (float)b.buildBean.hp; b0 = b.buildDef.HP;
                c.details.Add("hero " + hero.roleBean.getMaxHp() + " max, set to " + hpLow + "; medic at " + cx + "," + cy + ", hero at " + hero.cellX + "," + hero.cellY);
                CamearControl.getInstance().moveFollow((SpriteObject)b);
            });
            if (hero == null || medic == null) { c.summary = hero == null ? "no living person of the player" : "no room for the medic near " + hero.cellX + "," + hero.cellY; yield break; }
            EfTurret.TestPowerId = "7205";
            SetClock(1f);
            float t0 = Time.realtimeSinceStartup; bool shot = false;
            float keep = 0;
            while (Time.realtimeSinceStartup - t0 < 12)
            {
                yield return null;
                if (Time.realtimeSinceStartup - keep > 0.5f)
                {
                    // the person's own jobs walk them away; put them back beside the medic (test only)
                    keep = Time.realtimeSinceStartup;
                    Locked(() => PutBeside(hero, medic));
                }
                if (!shot && Time.realtimeSinceStartup - t0 > 5.5f) { shot = true; CloseDialogs(); yield return Shot("medic"); }
            }
            SetClock(0f); EfTurret.TestPowerId = "-";
            float hp1 = (float)hero.roleBean.attr_Hp.hp, b1 = (float)medic.buildBean.hp;
            float dist = Vector2.Distance(EfCombat.Pos(hero), EfCombat.Pos(medic)) / 32f;
            c.passed = hp1 > hpLow && b1 > bLow;
            c.summary = "person " + hpLow.ToString("0") + " -> " + hp1.ToString("0") + " (" + dist.ToString("0.0") + " cells away), medic " + bLow.ToString("0") + " -> " + b1.ToString("0") + " of " + b0;
        }

        // The game's own effect prefabs, one after another next to a tower the camera follows (3 shots each),
        // to pick effects for the special towers that match the game's look.
        static readonly string[] GalleryF3D = {
            "Eff_Defense/effects/bullets/Plasma", "Eff_Defense/effects/bullets/Plasma_Big", "Eff_Defense/effects/bullets/Laser_Big", "Eff_Defense/effects/bullets/Missile",
            "Eff_Defense/effects/bullets/Cannon", "Eff_Defense/effects/bomb/Plasma", "Eff_Defense/effects/bomb/Plasma_Big", "Eff_Defense/effects/bomb/Laser_Big",
            "Eff_Defense/effects/bomb/Missile_Big", "Eff_Defense/effects/bomb/Cannon_Big", "Eff_Defense/effects/muzzle/Plasma_Big", "Eff_Defense/effects/muzzle/Laser_Big",
            "Eff_Bullets/effects/Spue/Bullet", "Eff_Bullets/effects/Spue/Hit", "Eff_Bullets/effects/grenade/Grenade Bullet", "Eff_Bullets/effects/grenade/Hit",
            "Eff_Bullets/effects/plasma/Plasma Hit", "Eff_Bullets/effects/laser/Laser Hit 1", "Eff_Bullets/effects/laser/LaserMuzzle 1" };
        static readonly string[] GalleryGround = {
            "Eff_Groung/Bomb/Bomb_Emp", "Eff_Groung/Bomb/Bomb_Gr", "Eff_Groung/Fire/Fire", "Eff_Groung/Cold Current/Cold Current", "Eff_Groung/Cold Current/Cold Current2",
            "Eff_Groung/Hot Wave/Hot Wave", "Eff_Groung/Fogging/Fog", "Eff_Groung/Cloud/cloud1", "Eff_Groung/Lightning/Lightning_Boom", "Eff_Groung/AirBomb/AirExplode",
            "Eff_Groung/Snow Cover/snow ground" };

        IEnumerator FxGallery()
        {
            var c = new Check { name = "fx gallery", info = true, passed = true }; result.checks.Add(c);
            CloseDialogs();
            Locked(() =>
            {
                var f = MapManager.getInstance().heroManager.getAllianceFaction(1);
                if (f != null) foreach (var h in f.getRoles()) if (h != null) people.Add(new Vector2(h.cellX, h.cellY));
            });
            EfTurret anchor = null;
            Locked(() =>
            {
                var def = ImplDef<F_DefenseDef>.getDataID("7101");
                if (!FindFreeArea(10, 8, used, def, new Dictionary<string, int>(), out int x0, out int y0)) return;
                short cx = (short)(x0 + 2), cy = (short)(y0 + 4);
                anchor = UserBean.getInstance().curStarMap.addBuild(BuildGenerator.createBean_Defend(def, 1, "7101", cx, cy, 0), cx, cy, 0) as EfTurret;
                if (anchor != null) CamearControl.getInstance().moveFollow((SpriteObject)anchor);
            });
            if (anchor == null) { c.summary = "no place"; yield break; }
            SetClock(1f);
            yield return new WaitForSecondsRealtime(1.5f);
            Vector2 at = EfCombat.Pos(anchor) + new Vector2(96f, 0f);
            int k = 0;
            foreach (var path in (req.tests == "fxgallery2" ? GalleryGround : GalleryF3D.Concat(GalleryGround)))
            {
                string err = null;
                Locked(() => { try { EfFx.Particle(path, at, 0f, 2.5f); if (EfFx.Prefab(path) == null) err = "missing"; } catch (Exception e) { err = e.Message; } });
                c.details.Add(k.ToString("00") + " " + path + (err != null ? "  ERROR " + err : ""));
                float t0 = Time.realtimeSinceStartup; char tag = 'a';
                foreach (float w in new[] { 0.12f, 0.45f, 1.0f })
                {
                    while (Time.realtimeSinceStartup - t0 < w) yield return null;
                    yield return Shot("gal-" + k.ToString("00") + "-" + tag); tag++;
                }
                while (Time.realtimeSinceStartup - t0 < 2.2f) yield return null;
                k++;
            }
            SetClock(0f);
            c.summary = k + " effects shown";
        }

        // Placement preview: the same tower planned in the four building directions (left to right 1..4), then the
        // preview the build menu makes (direction 2) after one frame of EfPlacement.
        IEnumerator Plan()
        {
            var c = new Check { name = "placement preview stays upright" }; result.checks.Add(c);
            CloseDialogs();
            short cx = -1, cy = -1; var dirGraphs = new List<BasePlanGraph>();
            Locked(() =>
            {
                var f = MapManager.getInstance().heroManager.getAllianceFaction(1); if (f != null) foreach (var h in f.getRoles()) if (h != null) people.Add(new Vector2(h.cellX, h.cellY));
                var def = ImplDef<F_DefenseDef>.getDataID("7101");
                if (!FindFreeArea(26, 8, used, def, new Dictionary<string, int>(), out int x0, out int y0)) return;
                cx = (short)(x0 + 3); cy = (short)(y0 + 4);
                for (sbyte d = 1; d <= 4; d++) dirGraphs.Add(PlanCreator.createTempPlanGraph(10009, 10, "7101", (short)(cx + (d - 1) * 6), cy, d));
            });
            if (cx < 0) { c.summary = "no free patch"; yield break; }
            var look = new Vector2(MapTools.cellX2PosCanter((short)(cx + 9)), MapTools.cellY2PosCanter(cy));
            for (int i = 0; i < 6; i++)
            {
                // moving a preview (as the mouse does) makes the game repaint it with its direction
                int k = i;
                Locked(() =>
                {
                    CamearControl.getInstance().moveTowardsMapPoint(look, 1f);
                    for (int j = 0; j < dirGraphs.Count; j++) { dirGraphs[j].setCell((short)(cx + j * 6 + (k % 2)), cy); TurnPreview(dirGraphs[j]); }
                });
                yield return new WaitForSecondsRealtime(0.25f);
            }
            CloseDialogs();
            yield return Shot("plan-dirs");
            BasePlanGraph g = null; sbyte before = 0, after = 0;
            Locked(() =>
            {
                LocalCoreHandle.Plan_CacheClear_LN();
                g = PlanCreator.createTempPlanGraph(10009, 10, "7101", (short)(cx + 9), cy, 2);
                before = g.getBuildDir();
                MapManager.getInstance().controlManager.controlBuild.setClickGraph(g);
            });
            yield return new WaitForSecondsRealtime(1f);
            after = g != null ? g.getBuildDir() : (sbyte)0;
            for (int i = 0; i < 4; i++) { int k = i; Locked(() => { CamearControl.getInstance().moveTowardsMapPoint(look, 1f); g?.setCell((short)(cx + 9 + (k % 2)), cy); if (g != null) TurnPreview(g); }); yield return new WaitForSecondsRealtime(0.25f); }
            after = g != null ? g.getBuildDir() : (sbyte)0;
            CloseDialogs();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("plan-fixed");
            Locked(() => MapManager.getInstance().controlManager.controlBuild.reset());
            c.passed = after == EfPlacement.Upright;
            c.summary = "plan-dirs: directions 1..4 left to right; build menu preview direction " + before + " -> " + after + " (upright = " + EfPlacement.Upright + ")";
        }

        // Ground marks of every kind around a tower, for a screenshot.
        IEnumerator Ground()
        {
            var c = new Check { name = "ground marks show", info = true, passed = true }; result.checks.Add(c);
            CloseDialogs();
            EfTurret t = null;
            Locked(() =>
            {
                var f = MapManager.getInstance().heroManager.getAllianceFaction(1); if (f != null) foreach (var h in f.getRoles()) if (h != null) people.Add(new Vector2(h.cellX, h.cellY));
                var def = ImplDef<F_DefenseDef>.getDataID("7203");
                if (!FindFreeArea(20, 14, used, def, new Dictionary<string, int>(), out int x0, out int y0)) return;
                short cx = (short)(x0 + 10), cy = (short)(y0 + 7);
                t = UserBean.getInstance().curStarMap.addBuild(BuildGenerator.createBean_Defend(def, 1, "7203", cx, cy, 0), cx, cy, 0) as EfTurret;
                if (t == null) return;
                CamearControl.getInstance().moveFollow((SpriteObject)t);
                Vector2 p = EfCombat.Pos(t); float cell = EfCombat.Cell;
                EfGround.Add("acid", p + new Vector2(-6 * cell, 3 * cell), 2.5f * cell, 30f);
                EfGround.Add("poison", p + new Vector2(6 * cell, 3 * cell), 3f * cell, 30f);
                EfGround.Add("frost", p + new Vector2(-6 * cell, -4 * cell), 2.5f * cell, 30f);
                EfGround.Add("burn", p + new Vector2(6 * cell, -4 * cell), 1.3f * cell, 30f);
                EfGround.Add("burn", p + new Vector2(8 * cell, -4 * cell), 1.3f * cell, 30f);
                EfGround.Add("burn", p + new Vector2(0, -6 * cell), 1.5f * cell, 30f);
            });
            if (t == null) { c.summary = "no patch"; yield break; }
            SetClock(1f);
            var look = EfCombat.Pos(t);
            for (int i = 0; i < 6; i++) { Locked(() => CamearControl.getInstance().moveTowardsMapPoint(look, 1f)); yield return new WaitForSecondsRealtime(0.25f); }
            CloseDialogs();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("ground");
            SetClock(0f);
            c.summary = "acid top left, poison top right, ice bottom left, scorch bottom right and below; live " + EfGround.Live;
        }

        // What the game does when the mouse moves a preview: BuildPlanMod.casePaint turns the icon by the direction.
        static void TurnPreview(BasePlanGraph g)
        {
            var f = typeof(BasePlanGraph).GetField("tripBuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var trip = f?.GetValue(g) as BuildPlanBase; if (trip == null) return;
            trip.casePaint(); trip.repaint();
        }

        // Power: one of the mod's towers and one of the game's own turrets on a patch with no power line, generator or
        // battery within 12 cells, monsters in range, no test power. Neither may fire.
        IEnumerator Power()
        {
            var c = new Check { name = "towers do nothing without power" }; result.checks.Add(c);
            EfTurret.TestPower = false; EfTurret.TestPowerId = null;
            CloseDialogs();
            Locked(() => { var f = MapManager.getInstance().heroManager.getAllianceFaction(1); if (f != null) foreach (var h in f.getRoles()) if (h != null) people.Add(new Vector2(h.cellX, h.cellY)); });
            Run(PickMonster);
            EfTurret mine = null; Defense_BaseTurret vanilla = null; short cx = -1, cy = -1;
            Locked(() =>
            {
                var spread = UserBean.getInstance().curStarMap;
                var def = ImplDef<F_DefenseDef>.getDataID("7101"); var vdef = ImplDef<F_DefenseDef>.getDataID("10");
                var why = new Dictionary<string, int>();
                for (int tries = 0; tries < 60; tries++)
                {
                    if (!FindFreeArea(12, 8, used, def, why, out int x0, out int y0)) break;
                    used.Add(new[] { x0, y0, 12, 8 });
                    var near = MapManager.getInstance().buildManager.electricManager.getMainElectric(x0 - 12, y0 - 12, x0 + 24, y0 + 20);
                    if (near != null && near.Count > 0) continue;
                    cx = (short)(x0 + 3); cy = (short)(y0 + 4);
                    mine = spread.addBuild(BuildGenerator.createBean_Defend(def, 1, "7101", cx, cy, 0), cx, cy, 0) as EfTurret;
                    vanilla = spread.addBuild(BuildGenerator.createBean_Defend(vdef, 1, "10", (short)(cx + 5), cy, 0), (short)(cx + 5), cy, 0) as Defense_BaseTurret;
                    break;
                }
            });
            if (mine == null || vanilla == null) { c.summary = "no patch away from power (mine " + (mine != null) + ", game's " + (vanilla != null) + ")"; yield break; }
            var shootField = typeof(Defense_BaseTurret).GetField("shootCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Func<Defense_BaseTurret, int> shots = t => shootField != null ? Convert.ToInt32(shootField.GetValue(t)) : -1;
            SetClock(1f);
            yield return new WaitForSecondsRealtime(2f);
            var spawned = new List<FWMonsterBean>();
            int mine0 = 0, van0 = 0;
            Locked(() =>
            {
                mine0 = shots(mine); van0 = shots(vanilla);
                for (int a = 0; a < 360 && spawned.Count < 3; a += 30)
                {
                    short mx = (short)(cx + 2 + (int)Math.Round(7 * Math.Cos(a * Math.PI / 180))), my = (short)(cy + (int)Math.Round(7 * Math.Sin(a * Math.PI / 180)));
                    if (CellProblem(mx, my, null) != null) continue;
                    spawned.Add(GMCommand.getInstance().Monster_AddMoster(monsterId, 2, mx, my));
                }
                CamearControl.getInstance().moveFollow((SpriteObject)mine);
            });
            yield return new WaitForSecondsRealtime(8f);
            CloseDialogs();
            yield return Shot("power");
            SetClock(0f);
            string State(Defense_BaseTurret t) { var e = (Defense_BaseInElectric)t; return "linked " + e.isLinkElectric() + ", powered " + e.isSupportElectriced() + ", use " + t.buildDef.getElectricConsume_Run() + ", shots " + (shots(t) - (t == mine ? mine0 : van0)); }
            c.details.Add("mod tower 7101 at " + cx + "," + cy + ": " + State(mine));
            c.details.Add("game turret 10 at " + (cx + 5) + "," + cy + ": " + State(vanilla));
            c.details.Add(spawned.Count + " monsters 7 cells away");
            int m = shots(mine) - mine0, v = shots(vanilla) - van0;
            Locked(() => { foreach (var r in MapManager.getInstance().monsterManager.getMonsters().ToList()) if (spawned.Contains(r.monsterBean)) GMCommand.getInstance().Monster_RemoveMoster(r); });
            c.passed = spawned.Count > 0 && !mine.isSupportElectriced() && m == 0;
            c.summary = "unpowered mod tower fired " + m + " times, unpowered game turret " + v + " times";
        }

        // Opens the game's tech tree centred on each of the mod's nodes and takes a screenshot (cards and lines must
        // not overlap anything).
        IEnumerator TechTree()
        {
            var c = new Check { name = "tech tree", info = true, passed = true }; result.checks.Add(c);
            CloseDialogs();
            foreach (var t in EfData.Techs)
            {
                string err = null;
                Locked(() => { try { FairyUIDispatcher.showTecholongy(t.Id); } catch (Exception e) { err = e.Message; } });
                c.details.Add(t.Id + (err != null ? " ERROR " + err : ""));
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("tech-" + t.Id);
                Locked(() => { try { TechonologyView.getInstance()?.closeDialog(); } catch { } });
                yield return new WaitForSecondsRealtime(0.5f);
            }
            c.summary = EfData.Techs.Count + " nodes shown";
        }

        static void PutBeside(BaseRole r, BaseBuild b)
        {
            if (Vector2.Distance(EfCombat.Pos(r), EfCombat.Pos(b)) <= 3 * 32f) return;
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                {
                    short x = (short)(b.cellX + dx), y = (short)(b.cellY + dy);
                    if (!MapTools.isCellInRealMapRect(x, y) || !PassableHandler.isPersonPassable(x, y, r)) continue;
                    r.doInterruptAction(); r.mod_Move?.stopMove();
                    MapGrid.getInstance().releasePlance(r, r.cellX, r.cellY);
                    r.setCell(x, y); r.roleBean?.setCell(x, y); MapGrid.getInstance().tryPlace_Role(r, x, y);
                    return;
                }
        }

        static float HpOf(List<FWMonsterBean> ms)
        {
            float sum = 0;
            foreach (var m in ms) { try { sum += Math.Max(0f, m.getHp()); } catch { } }
            return sum;
        }

        static void FocusOn(BaseBuild a, BaseBuild b, float scale)
        {
            var pa = a.transform.localPosition; var pb = b.transform.localPosition;
            CamearControl.getInstance().setTo((pa.x + pb.x) / 2, (pa.y + pb.y) / 2, scale);
        }

        protected override void BeforeQuit() { EfTurret.TestPower = false; }
    }
}
