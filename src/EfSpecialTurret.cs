using System;
using System.Collections.Generic;
using UnityEngine;

namespace EfTurrets
{
    // Towers whose attack needs more than the game's bullets. Targeting, turning, power and the reload pause stay the
    // game's (Defense_BaseTurret). When the firing animation reaches its muzzle frame the tower attacks once:
    //   - the visible shot is a real game bullet (BulletManager.addDefenseBullet with the row's BulletID/AimType),
    //     so flight, impact, splash damage (DamageRange) and slow (Drag/Shocktime) look and work like the game's;
    //   - what the game has no rule for (ice field, corrosion, poison, bouncing arcs, electric orb, push, stun, flame
    //     band, healing) is done here, timed to the bullet's landing, with the game's particle effects (EfFx).
    // The medic has no target and works on a timer.
    public class EfSpecialTurret : EfTurret
    {
        readonly EfLasting lasting = new EfLasting();
        readonly List<BaseRole> enemies = new List<BaseRole>();
        readonly List<KeyValuePair<float, Action>> later = new List<KeyValuePair<float, Action>>();
        bool attacked;
        float medicTimer;
        Vector2 flameDir;

        TowerSpec S => EfData.Find(buildDef?.ID);

        void After(float seconds, Action a) => later.Add(new KeyValuePair<float, Action>(seconds, a));

        protected override void caseShoot()
        {
            if (targetRole == null) { doStand(); return; }
            var p = buildPlayer.player;
            if (!attacked && p.isFrameFirstTime())
            {
                var areas = p.getAction(p.currActionId).getFrame(p.currentFrameID).getCollisionAreas();
                if (areas != null && areas.Length > 0)
                {
                    attacked = true;
                    Vector3 pos = base.localPos;
                    try { Attack(new Vector2(pos.x + areas[0].centerX(), pos.y + areas[0].centerY())); }
                    catch (Exception e) { Debug.LogWarning("[EfTurrets] attack " + buildDef?.ID + ": " + e.Message); }
                }
            }
            if (p.isLastFramed() && attacked) { attacked = false; doFireFinish(); return; }
            repaint();
        }

        protected override void doShoot() { attacked = false; base.doShoot(); }

        protected override void checkStand() { if (S?.Kind == "medic") return; base.checkStand(); }

        public override void logic()
        {
            base.logic();
            float dt = TimeInfo.elapsedTime;
            if (S == null || dt <= 0) return;
            try
            {
                for (int i = later.Count - 1; i >= 0; i--)
                {
                    var e = later[i]; float left = e.Key - dt;
                    if (left > 0) { later[i] = new KeyValuePair<float, Action>(left, e.Value); continue; }
                    later.RemoveAt(i); e.Value();
                }
                TickLasting(dt);
                if (S.Kind == "medic" && isSupportElectriced()) { medicTimer += dt; if (medicTimer >= S.Reload) { medicTimer = 0; Medic(); } }
            }
            catch (Exception e) { Debug.LogWarning("[EfTurrets] tick " + buildDef?.ID + ": " + e.Message); }
        }

        // a real game bullet from 'from' to the target (visual flight, impact effect, the row's damage/splash/slow)
        void Fire(Vector2 from, SpriteObject to)
        {
            if (to == null || to.destroy) return;
            float ang = Mathf.Atan2(EfCombat.Pos(to).y - from.y, EfCombat.Pos(to).x - from.x) * Mathf.Rad2Deg;
            BulletManager.getInstance().addDefenseBullet(this, to, from.x, from.y, ang);
        }

        // flight time of a lobbed or dropped game bullet (Bullet_Throw: distance / (BulletSpeed * 32))
        float Flight(Vector2 from, Vector2 to) => Vector2.Distance(from, to) / Mathf.Max(1f, ((F_DefenseDef)buildDef).BulletSpeed * 32f);

        void Attack(Vector2 muzzle)
        {
            var s = S; var t = targetRole; if (t == null) return;
            Vector2 tp = EfCombat.Pos(t), d = tp - muzzle; float dist = d.magnitude; Vector2 dir = dist > 0.01f ? d / dist : Vector2.up;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, cell = EfCombat.Cell;
            CountShot(buildDef.ID);
            switch (s.Kind)
            {
                case "jet":
                {
                    // three water shots in a row, then the push where they land
                    for (int i = 0; i < 3; i++) { int k = i; After(0.08f * k, () => Fire(muzzle, targetRole ?? t)); }
                    After(Flight(muzzle, tp), () =>
                    {
                        for (int i = 1; i <= 3; i++) EfFx.Particle(EfFx.EmpBurst, muzzle + dir * (dist * i / 3f), 0f, 1.2f);
                        EfCombat.Enemies(this, enemies);
                        foreach (var r in EfCombat.InBand(enemies, muzzle, dir, s.Range * cell, s.Radius * cell))
                        {
                            Vector2 away = EfCombat.Pos(r) - EfCombat.Pos(this); away = away.sqrMagnitude > 1 ? away.normalized : dir;
                            if (EfCombat.Push(r, away, s.PushCells) > 0) { CountEffect(buildDef.ID); EfFx.Particle(EfFx.EmpBurst, EfCombat.Pos(r), 0f, 1.2f); }
                        }
                    });
                    break;
                }
                case "flame":
                {
                    // fire spray marching out along the band, burning what it reaches
                    float len = s.Range * cell;
                    for (int i = 0; i < 6; i++)
                    {
                        float f = (i + 1) / 6f; Vector2 at = muzzle + dir * (len * f); float when = 0.05f * i;
                        After(when, () => EfFx.Particle(EfFx.FlameBlob, at, ang));
                    }
                    After(0.3f, () => EfFx.Particle(EfFx.FlameHit, muzzle + dir * len, ang));
                    // the ground it swept stays scorched for a while (the game's own burn mark)
                    for (int i = 1; i <= 3; i++) { Vector2 at = muzzle + dir * (len * (0.25f + 0.25f * i)); After(0.1f * i, () => EfGround.Add("burn", at, cell * 1.3f, 8f)); }
                    flameDir = dir;
                    lasting.zones.Add(new EfLasting.Zone { kind = "flame", at = muzzle, radius = len, left = 0.75f, tick = 0f });
                    break;
                }
                case "wave":
                {
                    EfFx.Particle(EfFx.EmpBurst, EfCombat.Pos(this), 0f, 1.5f);
                    // the wave front: shock bursts marching out over the sector
                    for (int ring = 1; ring <= 3; ring++)
                        for (int side = -1; side <= 1; side++)
                        {
                            float a = (ang + side * 25f) * Mathf.Deg2Rad, r = s.Range * cell * (0.3f * ring);
                            Vector2 at = EfCombat.Pos(this) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                            After(0.08f * ring, () => EfFx.Particle(EfFx.EmpBurst, at, 0f, 1.2f));
                        }
                    EfCombat.Enemies(this, enemies);
                    foreach (var r in EfCombat.InSector(enemies, EfCombat.Pos(this), dir, s.Range * cell, 40f))
                        if (EfCombat.Stun(r, s.Duration)) { EfFx.Particle(EfFx.PlasmaBurst, EfCombat.Pos(r)); CountEffect(buildDef.ID); }
                    break;
                }
                case "chain":
                case "charge":
                {
                    // the arc hops from target to target: one game bullet per hop, each from the previous target
                    EfCombat.Enemies(this, enemies);
                    var hit = new List<SpriteObject> { t }; SpriteObject cur = t; Vector2 from = muzzle; float when = 0f;
                    for (int i = 0; i <= s.Bounces && cur != null; i++)
                    {
                        var a = from; var b = cur; After(when, () => Fire(a, b));
                        when += Flight(from, EfCombat.Pos(cur)) + 0.02f; from = EfCombat.Pos(cur); CountEffect(buildDef.ID);
                        BaseRole next = null; float best = s.Radius * cell * s.Radius * cell;
                        foreach (var r in enemies) { if (hit.Contains(r)) continue; float q = (EfCombat.Pos(r) - from).sqrMagnitude; if (q <= best) { best = q; next = r; } }
                        if (next != null) hit.Add(next);
                        cur = next;
                    }
                    if (s.Kind == "charge")
                        After(Flight(muzzle, tp), () =>
                        {
                            lasting.zones.Add(new EfLasting.Zone { kind = "orb", at = tp, radius = 1.5f * cell, left = s.Duration, tick = 0f });
                            EfGround.Add("burn", tp, 1.5f * cell, s.Duration + 3f);
                        });
                    break;
                }
                case "frost":
                case "acid":
                case "poison":
                {
                    Fire(muzzle, t);
                    After(Flight(muzzle, tp), () => Land(tp));
                    break;
                }
            }
        }

        // where a lobbed shell came down (its splash damage and slow are the game bullet's own)
        void Land(Vector2 at)
        {
            var s = S; float radius = s.Radius * EfCombat.Cell;
            switch (s.Kind)
            {
                case "frost":
                    lasting.zones.Add(new EfLasting.Zone { kind = "ice", at = at, radius = radius, left = s.Duration, tick = 0f });
                    EfGround.Add("frost", at, radius, s.Duration + 1.5f);
                    EfFx.Particle(EfFx.EmpBurst, at, 0f, 1.2f);
                    EfCombat.Enemies(this, enemies);
                    foreach (var r in EfCombat.InRadius(enemies, at, radius)) CountEffect(buildDef.ID);   // slowed by the shell itself (Drag)
                    break;
                case "acid":
                    EfFx.Particle(EfFx.AcidHit, at); EfFx.Particle(EfFx.AcidHit, at + new Vector2(24, 10)); EfFx.Particle(EfFx.AcidHit, at + new Vector2(-22, -8));
                    EfCombat.Enemies(this, enemies);
                    foreach (var r in EfCombat.InRadius(enemies, at, radius)) { lasting.Mark(r, "corrode", s.Duration); CountEffect(buildDef.ID); }
                    // the acid stays on the ground as a pool; whatever walks through it is corroded too
                    lasting.zones.Add(new EfLasting.Zone { kind = "acid", at = at, radius = radius, left = s.PoolSeconds, tick = EfLasting.Tick });
                    EfGround.Add("acid", at, radius, s.PoolSeconds + 1.5f);
                    break;
                case "poison":
                    lasting.zones.Add(new EfLasting.Zone { kind = "poison", at = at, radius = radius, left = s.Duration, tick = 0f });
                    EfGround.Add("poison", at, radius, s.Duration + 1.5f);
                    break;
            }
        }

        void TickLasting(float dt)
        {
            var s = S; if (lasting.zones.Count == 0 && lasting.states.Count == 0) return;
            if (lasting.zones.Count > 0) EfCombat.Enemies(this, enemies);
            for (int i = lasting.zones.Count - 1; i >= 0; i--)
            {
                var z = lasting.zones[i]; z.left -= dt; z.tick -= dt; z.fxTick -= dt;
                if (z.tick <= 0)
                {
                    z.tick += EfLasting.Tick;
                    switch (z.kind)
                    {
                        case "ice": foreach (var r in EfCombat.InRadius(enemies, z.at, z.radius)) { EfCombat.Slow(r, s.Slow, s.SlowSeconds); CountEffect(buildDef.ID); } break;
                        case "poison":
                            foreach (var r in EfCombat.InRadius(enemies, z.at, z.radius))
                            {
                                var st = lasting.Mark(r, "poison", s.Duration); EfCombat.Slow(r, s.Slow, s.SlowSeconds); CountEffect(buildDef.ID);
                                if (st.fxAt <= 0) { EfFx.Game(AnimPlayerID.Glo.EFFECT_中毒, EfCombat.Pos(r) + new Vector2(0, 30)); st.fxAt = 3.2f; }
                            }
                            break;
                        case "orb": foreach (var r in EfCombat.InRadius(enemies, z.at, z.radius)) { EfCombat.Damage(this, r, s.TickAtk); CountEffect(buildDef.ID); } break;
                        case "acid": foreach (var r in EfCombat.InRadius(enemies, z.at, z.radius)) { lasting.Mark(r, "corrode", s.Duration); CountEffect(buildDef.ID); } break;
                        case "flame":
                            foreach (var r in EfCombat.InBand(enemies, z.at, flameDir, z.radius, s.Radius * EfCombat.Cell))
                            {
                                EfCombat.Damage(this, r, s.Atk); CountEffect(buildDef.ID);
                                var st = lasting.Mark(r, "burning", 1.5f);
                                if (st.fxAt <= 0) { EfFx.Particle(EfFx.GroundFire, EfCombat.Pos(r), 0f, 1.5f); st.fxAt = 1.5f; }
                            }
                            break;
                    }
                }
                if (z.fxTick <= 0)
                {
                    // the lasting zones keep showing: cold flashes on the ice, smoke puffs in the poison, arcs in the orb
                    switch (z.kind)
                    {
                        case "ice": EfFx.Particle(EfFx.EmpBurst, z.at + Jitter(z.radius * 0.5f), 0f, 1.2f); z.fxTick = 0.6f; break;
                        // (the game's dark smoke bursts read as black blots on light ground: the pool shows the poison)
                        case "orb": EfFx.Particle(EfFx.PlasmaBurst, z.at + Jitter(z.radius * 0.3f)); z.fxTick = 0.4f; break;
                        case "acid": EfFx.Particle(EfFx.AcidHit, z.at + Jitter(z.radius * 0.5f)); z.fxTick = 0.7f; break;
                        default: z.fxTick = 99f; break;
                    }
                }
                if (z.left <= 0) lasting.zones.RemoveAt(i);
            }
            for (int i = lasting.states.Count - 1; i >= 0; i--)
            {
                var st = lasting.states[i]; st.left -= dt; st.tick -= dt; st.fxAt -= dt;
                if (!EfCombat.Alive(st.role) || st.left <= 0) { lasting.states.RemoveAt(i); continue; }
                if (st.tick > 0) continue;
                st.tick += EfLasting.Tick;
                float hp = (float)st.role.roleBean.attr_Hp.hp;
                if (st.kind == "poison") EfCombat.Damage(this, st.role, s.TickAtk);
                else if (st.kind == "corrode")
                {
                    // takes Vulnerable times the damage: add the extra share of what it lost since the last tick
                    float lost = st.lastHp - hp;
                    if (lost > 0) { EfCombat.Damage(this, st.role, Mathf.RoundToInt(lost * (s.Vulnerable - 1f))); EfFx.Particle(EfFx.AcidHit, EfCombat.Pos(st.role)); }
                }
                st.lastHp = (float)st.role.roleBean.attr_Hp.hp;
            }
        }

        static Vector2 Jitter(float r) => new Vector2(EfFx.Rand(-r, r), EfFx.Rand(-r * 0.7f, r * 0.7f));

        // Heals the player's people and repairs the player's buildings within range (the game's heal crosses).
        void Medic()
        {
            var s = S; float range = s.Range * EfCombat.Cell, r2 = range * range; Vector2 me = EfCombat.Pos(this);
            var mm = MapManager.getInstance(); if (mm == null) return;
            int n = 0;
            var faction = mm.heroManager?.getAllianceFaction(getAlliance());
            if (faction != null)
                foreach (var h in faction.getRoles())
                {
                    if (h?.roleBean == null || h.roleBean.isDead() || (EfCombat.Pos(h) - me).sqrMagnitude > r2) continue;
                    if (h.roleBean.attr_Hp.hp >= h.roleBean.getMaxHp()) continue;
                    EfCombat.HealRole(h, s.Heal); EfFx.Game(AnimPlayerID.Glo.EFFECT_医疗, EfCombat.Pos(h)); n++;
                }
            var broken = BuildManager.getInstance()?.getBrokenBuilds();
            if (broken != null)
                foreach (var b in new List<BaseBuild>(broken))
                {
                    if (b == null || b.buildDef == null || b.getAlliance() != getAlliance() || (EfCombat.Pos(b) - me).sqrMagnitude > r2) continue;
                    b.addHp(b.buildDef.HP * s.Repair); EfFx.Game(AnimPlayerID.Glo.EFFECT_医疗, EfCombat.Pos(b)); n++;
                }
            if (n > 0) { CountEffect(buildDef.ID, n); EfFx.Game(AnimPlayerID.Glo.EFFECT_医疗, me); }
            CountShot(buildDef.ID);
        }

        public override void beDestroy()
        {
            lasting.Clear(); later.Clear();
            base.beDestroy();
        }
    }
}
