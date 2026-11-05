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
        string monsterId;

        protected override string HeartbeatText() => "placed=" + placed.Count + " shots=" + EfTurret.TestShots.Values.Sum();

        protected override IEnumerator RunTests()
        {
            if (req.tests == "freeze-demo") { Log("freezing 120 s"); System.Threading.Thread.Sleep(120000); yield break; }
            yield return new WaitForSecondsRealtime(2);
            Run(CheckDefs);
            Run(CheckTechs);
            if (req.tests == "defs") yield break;

            EfTurret.TestPower = true;
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
                yield return Guard(Fight(t), "fight " + t.buildDef.ID);
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
            var used = new List<int[]>();
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

        bool FindFreeArea(int w, int h, List<int[]> used, BaseDef probe, Dictionary<string, int> why, out int x0, out int y0)
        {
            int rows = MapGlobal.CellRow, lines = MapGlobal.CellLine;
            int cx = rows / 2, cy = lines / 2;
            for (int r = 0; r < Math.Max(rows, lines); r += 2)
                for (int a = 0; a < Math.Max(8, r); a++)
                {
                    double ang = a * 2 * Math.PI / Math.Max(8, r);
                    x0 = cx + (int)(r * Math.Cos(ang)) - w / 2; y0 = cy + (int)(r * Math.Sin(ang)) - h / 2;
                    int xx = x0, yy = y0;
                    if (used.Any(u => xx < u[0] + u[2] && xx + w > u[0] && yy < u[1] + u[3] && yy + h > u[1])) continue;
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
            var s = EfData.Find(t.buildDef.ID);
            var c = new Check { name = "fight " + s.Id + " " + s.Name };
            result.checks.Add(c);
            if (monsterId == null) { c.summary = "no monster kind"; yield break; }
            int shots0 = EfTurret.TestShots.TryGetValue(s.Id, out var v) ? v : 0;
            var spawned = new List<FWMonsterBean>();
            int dist = Math.Max(s.MinRangeCells + 3, Math.Min(s.Range - 4, 10));
            EfTurret.TestPowerId = s.Id;
            Locked(() =>
            {
                for (int a = 0; a < 360 && spawned.Count < 3; a += 15)
                {
                    double ang = a * Math.PI / 180;
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
                int shotsNow = EfTurret.TestShots.TryGetValue(s.Id, out var vv) ? vv : 0;
                if (!shotTaken && shotsNow > shots0 && Time.realtimeSinceStartup - t0 > 2) { shotTaken = true; yield return Shot("fight-" + s.Id); }
            }
            SetClock(0f);
            EfTurret.TestPowerId = "-";
            float hp1 = HpOf(spawned);
            int shots = (EfTurret.TestShots.TryGetValue(s.Id, out var v2) ? v2 : 0) - shots0;
            if (!shotTaken) yield return Shot("fight-" + s.Id);
            Locked(() =>
            {
                foreach (var m in MapManager.getInstance().monsterManager.getMonsters().ToList())
                    if (spawned.Contains(m.monsterBean)) GMCommand.getInstance().Monster_RemoveMoster(m);
            });
            c.passed = shots > 0 && hp1 < hp0;
            c.summary = spawned.Count + " monsters, shots " + shots + ", monster health " + hp0.ToString("0") + " -> " + hp1.ToString("0") + " at " + dist + " cells, direction " + t.Direction;
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
