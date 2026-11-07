using UnityEngine;

namespace EfTurrets
{
    // One class for every tower of the mod. Targeting, firing rhythm, bullets and damage are the game's own
    // (Defense_BaseTurret, driven by the cloned table row); only the picture and the muzzle are different:
    // the head is not rotated, the frame for the nearest of 24 directions is drawn instead, and the muzzle marks
    // in that frame are already in screen position.
    // Sprite layout (make_assets.py): action 1 = base, 2+d = firing towards d, 26+d = idle towards d; launchers with
    // pitch levels (TowerSpec.Pitch) add 50+(j-1)*48+d = firing and 74+(j-1)*48+d = idle at level j.
    public class EfTurret : Defense_BaseTurret
    {
        public const int ActionBase = 1, ActionFire = 2, ActionIdle = 26, Directions = 24;
        TowerSpec spec;
        // test hooks (SelfTest): run without power, count shots per tower
        public static bool TestPower;
        public static string TestPowerId;   // when set, only this tower gets the test power
        public static readonly System.Collections.Generic.Dictionary<string, int> TestShots = new System.Collections.Generic.Dictionary<string, int>();
        public static readonly System.Collections.Generic.Dictionary<string, int> TestEffects = new System.Collections.Generic.Dictionary<string, int>();
        // counted on the game's simulation thread, read by the self-test on the main thread
        public static void CountShot(string id) { if (id == null) return; lock (TestShots) { TestShots.TryGetValue(id, out int n); TestShots[id] = n + 1; } }
        public static void CountEffect(string id, int k = 1) { if (id == null) return; lock (TestEffects) { TestEffects.TryGetValue(id, out int n); TestEffects[id] = n + k; } }
        public static int Shots(string id) { lock (TestShots) return TestShots.TryGetValue(id, out int n) ? n : 0; }
        public static int Effects(string id) { lock (TestEffects) return TestEffects.TryGetValue(id, out int n) ? n : 0; }
        public BuildAnim_Player AnimPlayer => buildPlayer;

        public override bool isSupportElectriced() => TestPower ? (TestPowerId == null || TestPowerId == buildDef?.ID) : base.isSupportElectriced();

        public static BaseBuild Create(BaseDef bean)
        {
            try
            {
                if (!(bean is F_DefenseDef) || EfData.Find(bean.ID) == null) return null;
                var go = new GameObject("EfTurret_" + bean.ID);
                var spec = EfData.Find(bean.ID);
                EfTurret t = spec.Kind != null ? go.AddComponent<EfSpecialTurret>() : go.AddComponent<EfTurret>();
                t.configAnim<EfAnim>();
                return t;
            }
            catch (System.Exception e) { Debug.LogError("[EfTurrets] create " + bean?.ID + ": " + e); return null; }
        }

        TowerSpec Spec => spec ?? (spec = EfData.Find(buildDef?.ID));

        // 0 = screen up, clockwise, 15 degrees apart. curAngle is the game's: 0 = right, 90 = up (counter-clockwise).
        public int Direction
        {
            get
            {
                float cw = (90f - curAngle) % 360f; if (cw < 0) cw += 360f;
                return Mathf.RoundToInt(cw / 15f) % Directions;
            }
        }

        public float Angle => curAngle;

        // Launchers (TowerSpec.Pitch): level when idle; while aiming they tilt up to the launch angle for the target's
        // distance at PitchSpeed and only fire once they are there. The picture is the nearest rendered level.
        public float Pitch;
        public const float PitchSpeed = 90f;
        public float PitchFor(Vector2 from, Vector2 to)
        {
            var s = Spec; if (s?.Pitch == null) return 0f;
            float r = Mathf.Max(32f, s.Range * 32f);
            return Mathf.Lerp(s.PitchMin, s.PitchMax, Mathf.Clamp01((to - from).magnitude / r));
        }
        int PitchLevel
        {
            get
            {
                var p = Spec?.Pitch; if (p == null || p.Length < 2) return 0;
                return Mathf.Clamp(Mathf.RoundToInt(Pitch / (p[1] - p[0])), 0, p.Length - 1);
            }
        }
        int IdleAction(int d) { int j = PitchLevel; return j == 0 ? ActionIdle + d : 74 + (j - 1) * 48 + d; }
        int FireAction(int d) { int j = PitchLevel; return j == 0 ? ActionFire + d : 50 + (j - 1) * 48 + d; }
        bool StepPitch(float want)
        {
            float before = Pitch;
            Pitch = Mathf.MoveTowards(Pitch, want, PitchSpeed * TimeInfo.elapsedTime);
            if (before != Pitch) repaint();
            return Mathf.Abs(Pitch - want) < 0.5f;
        }

        protected override bool caseAim()
        {
            bool turned = base.caseAim();
            if (Spec?.Pitch == null) return turned;
            var t = targetRole;
            float want = t == null ? 0f : PitchFor(new Vector2(base.localPos.x, base.localPos.y), new Vector2(t.localPos.x, t.localPos.y));
            return StepPitch(want) && turned;
        }

        public override void logic()
        {
            base.logic();
            if (Spec?.Pitch != null && state == 1) StepPitch(0f);   // back to level when there is nothing to shoot
        }

        public override void paint()
        {
            anim.setRotate(0f);
            buildPlayer.paintActionFrame(ActionBase, 0, 0f, 0f, 0f, centerRotate: false, 1f, 1f);
            if (state == 3) buildPlayer.paintAction(0f, 0f);
            else buildPlayer.paintActionFrame(IdleAction(Direction), 0, 0f, 0f, 0f, centerRotate: false, 1f, 1f);
            buildPlayer.updatePaint();
        }

        protected override void doShoot()
        {
            setState(3);
            buildPlayer.setAction(FireAction(Direction), -1);
        }

        protected override void shootBullets(CatCollisionArea area)
        {
            shootCount++;
            CountShot(buildDef?.ID);
            Vector3 p = base.localPos;
            BulletManager.getInstance().addDefenseBullet(this, targetRole, p.x + area.centerX(), p.y + area.centerY(), curAngle);
        }

        bool TooClose(SpriteObject target, float x, float y)
        {
            var s = Spec; if (s == null || s.MinRangeCells <= 0 || target == null) return false;
            Vector3 t = target.localPos; float dx = t.x - x, dy = t.y - y, m = s.MinRangeCells * 32f;
            return dx * dx + dy * dy < m * m;
        }

        // No line-of-sight check (0.4.3). The game skips a target when the line to it crosses a cell that blocks bullets,
        // and every turret blocks (B_Defense BulletPassable 0): in a row of towers the ones behind never saw a monster
        // beating on the end of the row. The rest is the game's canAim / checkTargetAvailable unchanged; the shot itself
        // is not affected (BulletManager only lets cover right next to the target take a bullet, and turrets have
        // Penetration 0).
        protected override bool canAim(SpriteObject target, float turretX, float turretY, float checkRangeSqr)
        {
            if (target == null || TooClose(target, turretX, turretY)) return false;
            if (target is BaseHero hero)
            {
                if (hero.heroBean.isFallDown() || hero.heroBean.isDead() || hero.heroBean.isBuffing_休克()) return false;
            }
            else if (target is BaseAnimal || target is BaseMonster)
            {
                var r = (BaseRole)target;
                if (r.roleBean.isFallDown() || r.roleBean.isDead() || !RoleAlliance.isHostileAlliance(this, target)) return false;
            }
            else if (target is Defense_BaseTurret)
            {
                if (!RoleAlliance.isHostileAlliance(this, target)) return false;
            }
            else return false;
            Vector3 t = target.localPos;
            float dx = turretX - t.x, dy = turretY - t.y;
            if (dx * dx + dy * dy >= checkRangeSqr) return false;
            doAim(target);
            return true;
        }

        protected override void checkTargetAvailable()
        {
            var t = targetRole;
            if (t == null || !AliveForTurret(t)) { doStand(); return; }
            float range = defenseBean.getDef().Range;
            var c = t.getColliderRect();
            Vector3 p = base.localPos;
            float dx = p.x - c.centleX(), dy = p.y - c.centleY();
            if (dx * dx + dy * dy >= range * range) { doStand(); return; }
            if (state != 1 && TooClose(t, p.x, p.y)) doStand();
        }

        static bool AliveForTurret(SpriteObject t)
        {
            if (t is BaseRole r && (r.roleBean.isDead() || r.roleBean.isFallDown())) return false;
            if (t is BaseHero h && h.heroBean.isBuffing_休克()) return false;
            if (t is BaseBuild b && b.buildBean.isDead()) return false;
            return true;
        }
    }

    // Loads the tower's own sprite file instead of the shared B_Defense one.
    public class EfAnim : BuildAnim_Player
    {
        public override void setDef(BaseBuild _build, BaseDef dat)
        {
            base.setDef(_build, dat);
            string path = EfSprites.PlayerPath(dat?.ID);
            if (path == null) { Debug.LogError("[EfTurrets] no sprite for " + dat?.ID); return; }
            PlayerUnitls.initPlayerNode(buildPlayer, new PlayerInfo { player = path }, useURPLit: true, GameStatics.mipmapEnable);
            resetPlayerInfo();
            buildPlayer.setAction(correctPlayerInfo.actionId, -1);
            repaint();
        }
    }
}
