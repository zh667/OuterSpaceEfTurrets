using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EfTurrets
{
    // What the special towers do to creatures, built on the game's own pieces:
    //   damage   HurtStruct + beHit, with the tower as the source (aggro and kill credit go to it)
    //   slow     Attr_Move.slowDown (the game's only combat status; replaced, not stacked)
    //   stun     slowDown(1) plus the role's attack recovery timer (BaseRole.backwardDelay): its AI does nothing until it
    //            runs out. Big monsters (tank bug, brood mother, octopus, creator) and very tough ones are immune.
    //   push     moves the role cell by cell away from the tower while the next cell is walkable (as the lift does)
    // Positions are the game's map units (32 per cell, y up).
    public static class EfCombat
    {
        public const float Cell = 32f;

        // localPos: the position the game keeps for its simulation thread (0.9.0020 runs the map logic off the main
        // thread, where Unity objects such as transforms must not be touched)
        public static Vector2 Pos(SpriteObject o) { var p = o.localPos; return new Vector2(p.x, p.y); }

        public static bool Alive(BaseRole r) => r != null && r.roleBean != null && !r.roleBean.isDead() && !r.roleBean.isFallDown();

        // Hostile creatures of the tower: hostile heroes (raiders), prisoners on the player's side, animals, monsters.
        public static List<BaseRole> Enemies(Defense_BaseTurret t, List<BaseRole> into = null)
        {
            long t0 = EfPerf.Now;
            var list = into ?? new List<BaseRole>(); list.Clear();
            try { Collect(t, list); } finally { EfPerf.AddSim(EfPerf.Sim.Enemies, t0); EfPerf.Scanned(list.Count); }
            return list;
        }

        static void Collect(Defense_BaseTurret t, List<BaseRole> list)
        {
            var mm = MapManager.getInstance(); if (mm == null) return;
            sbyte hostile = RoleAlliance.getHostileAlliance(t.getAlliance());
            var hf = mm.heroManager?.getAllianceFaction(hostile);
            // each faction list holds a role once and the lists are of different kinds, so only the prisoners (heroes
            // like the hostile faction's) are checked against what is already there; Contains on every role was
            // quadratic in the number of enemies
            if (hf != null) foreach (var r in hf.getRoles()) Add(t, r, list, false);
            if (t.getAlliance() == 1 && mm.heroManager?.prisoners != null) foreach (var r in mm.heroManager.prisoners) Add(t, r, list, true);
            var af = mm.animalManager?.getAllianceFaction(hostile);
            if (af != null) foreach (var r in af.getRoles()) Add(t, r, list, false);
            var mf = mm.monsterManager?.getAllianceFaction(hostile);
            if (mf != null) foreach (var r in mf.getRoles()) Add(t, r, list, false);
        }

        static void Add(Defense_BaseTurret t, BaseRole r, List<BaseRole> list, bool check)
        {
            if (Alive(r) && RoleAlliance.isHostileAlliance(t, r) && (!check || !list.Contains(r))) list.Add(r);
        }

        public static List<BaseRole> InRadius(List<BaseRole> all, Vector2 c, float radius)
        {
            var o = new List<BaseRole>(); float r2 = radius * radius;
            foreach (var r in all) if ((Pos(r) - c).sqrMagnitude <= r2) o.Add(r);
            return o;
        }

        // Inside a band from 'from' towards 'dir' (unit), 'length' long and 'width' wide.
        public static List<BaseRole> InBand(List<BaseRole> all, Vector2 from, Vector2 dir, float length, float width)
        {
            var o = new List<BaseRole>();
            foreach (var r in all)
            {
                Vector2 d = Pos(r) - from; float along = Vector2.Dot(d, dir); float side = Mathf.Abs(d.x * dir.y - d.y * dir.x);
                if (along >= -Cell * 0.5f && along <= length && side <= width / 2) o.Add(r);
            }
            return o;
        }

        // Inside a circular sector of half angle 'half' (degrees) around 'dir'.
        public static List<BaseRole> InSector(List<BaseRole> all, Vector2 from, Vector2 dir, float radius, float half)
        {
            var o = new List<BaseRole>(); float cos = Mathf.Cos(half * Mathf.Deg2Rad);
            foreach (var r in all)
            {
                Vector2 d = Pos(r) - from; float m = d.magnitude;
                if (m <= radius && (m < 1f || Vector2.Dot(d / m, dir) >= cos)) o.Add(r);
            }
            return o;
        }

        public static void Damage(Defense_BaseTurret t, SpriteObject target, int atk, float extra = 0f)
        {
            if (target == null || atk <= 0) return;
            var def = (F_DefenseDef)t.buildDef;
            var h = HurtStruct.createHurtStruct(HurtStruct.AtkType.ATK_TYPE_远程, null, t.getAlliance(), atk, 0, def.DamageType, extra,
                def.AntiArmor, 0f, 0f, def.soundStruct, t, target);
            h.mainDef = def;
            target.beHit(h);
        }

        public static void Slow(BaseRole r, float amount, float seconds)
        {
            if (!Alive(r) || r.mod_Move == null) return;
            r.mod_Move.slowDown(Mathf.Clamp01(amount), Mathf.Max(1, Mathf.RoundToInt(seconds)));
        }

        static FieldInfo backward;
        public static bool IsBig(BaseRole r)
        {
            if (r is BaseMonster m)
            {
                var md = (m.roleBean as FWMonsterBean)?.getMonsterDef();
                if (md != null && (md.SubType == 3 || md.SubType == 4 || md.SubType == 11 || md.SubType == 12 || md.HP >= 3000)) return true;
            }
            return r.roleBean != null && r.roleBean.getMaxHp() >= 3000;
        }

        public static bool Stun(BaseRole r, float seconds)
        {
            if (!Alive(r) || IsBig(r)) return false;
            if (backward == null) backward = typeof(BaseRole).GetField("backwardDelay", BindingFlags.Instance | BindingFlags.NonPublic);
            (backward?.GetValue(r) as TimeDelay_Game)?.setStep(seconds, 0f);
            Slow(r, 1f, seconds);
            return true;
        }

        // Pushes the role up to 'cells' cells away along dir (or up to 60 degrees off it when that way is blocked);
        // stops at the first cell a person cannot stand on.
        public static int Push(BaseRole r, Vector2 dir, int cells)
        {
            if (!Alive(r) || IsBig(r)) return 0;
            int best = 0; short bx = r.cellX, by = r.cellY;
            foreach (float off in new[] { 0f, 30f, -30f, 60f, -60f })
            {
                float a = off * Mathf.Deg2Rad; var d = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
                int moved = 0; short cx = r.cellX, cy = r.cellY;
                for (int i = 1; i <= cells; i++)
                {
                    short nx = (short)Mathf.RoundToInt(r.cellX + d.x * i), ny = (short)Mathf.RoundToInt(r.cellY + d.y * i);
                    if (!MapTools.isCellInRealMapRect(nx, ny) || !PassableHandler.isPersonPassable(nx, ny, r)) break;
                    cx = nx; cy = ny; moved = i;
                }
                if (moved > best) { best = moved; bx = cx; by = cy; }
                if (best == cells) break;
            }
            if (best == 0) return 0;
            try
            {
                r.doInterruptAction();
                r.mod_Move?.stopMove();
                MapGrid.getInstance().releasePlance(r, r.cellX, r.cellY);
                r.setCell(bx, by);
                r.roleBean?.setCell(bx, by);
                MapGrid.getInstance().tryPlace_Role(r, bx, by);
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] push: " + e.Message); }
            return best;
        }

        public static void HealRole(BaseRole r, float fraction)
        {
            if (r?.roleBean == null || r.roleBean.isDead()) return;
            float add = r.roleBean.getMaxHp() * fraction;
            if (r.attrDriven != null) r.attrDriven.addHp(add); else r.roleBean.addHp(add);
        }
    }

    // Lasting effects a tower left behind (poison cloud, ice field, electric orb) and lasting states on creatures
    // (poisoned, corroded). The tower that made them ticks them from its own logic, so they stop with the game clock
    // and disappear with the tower.
    public class EfLasting
    {
        public class Zone { public Vector2 at; public float radius, left, tick, fxTick; public string kind; }
        public class State { public BaseRole role; public float left, tick, lastHp, fxAt; public string kind; }

        public readonly List<Zone> zones = new List<Zone>();
        public readonly List<State> states = new List<State>();
        public const float Tick = 0.5f;

        public State Mark(BaseRole r, string kind, float seconds)
        {
            foreach (var s in states) if (s.role == r && s.kind == kind) { s.left = Mathf.Max(s.left, seconds); return s; }
            var n = new State { role = r, kind = kind, left = seconds, tick = Tick, lastHp = (float)r.roleBean.attr_Hp.hp };
            states.Add(n); return n;
        }

        public void Clear()
        {
            zones.Clear(); states.Clear();
        }
    }
}
