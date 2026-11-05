using System;
using System.Collections.Generic;
using UnityEngine;

namespace EfTurrets
{
    // Adds the towers, their tech nodes and the nodes' unlock entries while the game loads its tables
    // (ModPluginManager.loadDefFromAssembly, before the rows are initialised and linked).
    public class EfDefLoader : BaseModDefLoader
    {
        readonly ModXmlManifest cfg;
        public static readonly List<string> Report = new List<string>();

        public EfDefLoader(ModXmlManifest config) : base(config) { cfg = config; }

        public override void initModDef()
        {
            Report.Clear();
            try { EfSprites.Prepare(cfg); } catch (Exception e) { Debug.LogError("[EfTurrets] sprites: " + e); }
            try { AddTowers(); } catch (Exception e) { Debug.LogError("[EfTurrets] towers: " + e); }
            try { AddTechs(); } catch (Exception e) { Debug.LogError("[EfTurrets] techs: " + e); }
            Debug.Log("[EfTurrets] defs: " + string.Join("; ", Report));
        }

        static void Replace<T>(T row) where T : ImplDef<T>
        {
            ImplDef<T>.beans.RemoveAll(b => b.ID == row.ID);
            ImplDef<T>.beans.Add(row);
        }

        void AddTowers()
        {
            int n = 0;
            foreach (var s in EfData.Towers)
            {
                F_DefenseDef r = ImplDef<F_DefenseDef>.getTemplateDef(s.Template);
                if (r == null) { Report.Add("no template " + s.Template + " for " + s.Id); continue; }
                r.ID = s.Id; r.mod = cfg;
                r.Name = s.Name; r.Desc = s.Desc;
                r.ModuleId = 0; r.RawIcon = "ef_" + s.Id + ".png";
                r.TechLevel = s.Tier; r.SortId = s.SortId; r.Apply = true;
                r.Reinstall = 0; r.Uninstall = "-1";
                r.Width = s.Size; r.Height = s.Size;
                r.HP = s.Hp; r.ATK = s.Atk; r.Max_Bullet = s.MaxBullet; r.Rotation = s.Rotation;
                if (s.Reload >= 0) r.Reload = s.Reload;
                r.Range = s.Range; r.Distance = s.Distance;
                if (s.DamageRange >= 0) r.DamageRange = s.DamageRange;
                if (s.AimType >= 0) r.AimType = s.AimType;
                if (s.Accuracy != null) r.Accuracy = s.Accuracy;
                r.Consume = s.Consume; r.Materials = s.Materials;
                if (s.Bullet != null) r.BulletID = s.Bullet;
                if (s.Muzzle != null) r.FireType = s.Muzzle;
                if (s.Hit != null) { r.HitHuman = s.Hit; r.HitLand = s.Hit; r.HitOther = s.Hit; }
                if (s.BulletSpeed >= 0) r.BulletSpeed = s.BulletSpeed;
                if (s.Drag >= 0) r.Drag = s.Drag;
                if (s.Shocktime >= 0) r.Shocktime = s.Shocktime;
                if (s.Workload > 0) r.Workload = s.Workload;
                Replace(r);
                ModIconCombine.additions.RemoveAll(b => b is F_DefenseDef && b.ID == s.Id);
                ModIconCombine.addRow(r);
                n++;
            }
            Report.Add("towers " + n);
        }

        void AddTechs()
        {
            int n = 0, u = 0;
            foreach (var t in EfData.Techs)
            {
                var unlocks = new List<string>();
                foreach (var s in EfData.Towers)
                {
                    if (s.Tech != t.Id) continue;
                    TechnologyUnlockItemDef item = ImplDef<TechnologyUnlockItemDef>.getTemplateDef("2501");
                    if (item == null) { Report.Add("no unlock template 2501"); return; }
                    item.ID = EfData.UnlockPrefix + s.Id; item.mod = cfg;
                    item.SubType = 1; item.AimItem = "10_" + s.Id; item.Name = s.Name; item.Desc = s.Desc;
                    Replace(item); unlocks.Add(item.ID); u++;
                }
                TechnologyDef d = ImplDef<TechnologyDef>.getTemplateDef(t.Template);
                if (d == null) { Report.Add("no tech template " + t.Template); return; }
                d.ID = t.Id; d.mod = cfg; d.Name = t.Name; d.Detail = t.Detail; d.Detail_2 = "";
                d.Building_unlock = string.Join("|", unlocks); d.NonBuilding_unlock = "-1";
                d.Pre_technology = t.Pre; d.PathNode = t.Path; d.HexGridX = t.X; d.HexGridY = t.Y;
                d.S_Materials = t.Materials; d.Level = t.Level;
                if (t.Icon >= 0) d.ModuleId = t.Icon;
                Replace(d); n++;
            }
            Report.Add("techs " + n + ", unlock entries " + u);
        }
    }
}
