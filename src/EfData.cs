using System.Collections.Generic;

namespace EfTurrets
{
    // What the mod adds to the game's tables. Rows are cloned from a vanilla row of the same kind (Template) and the
    // fields below replace the clone's values. Ids are plain numbers outside the game's ranges, so the tables can refer
    // to them ("10_7101"); the game's own mod rows would get a "package_" prefix that breaks those references.
    public class TowerSpec
    {
        public string Id, Template, Name, Desc, Tech;
        public int Tier, Size = 2, Hp, Atk, MaxBullet = 1, Range, Distance, DamageRange = -1, AimType = -1, Workload, SortId;
        public float Reload = -1f, Rotation = 150f;
        public string Consume, Materials, Accuracy;
        public int MinRangeCells;   // mortar: no shots closer than this (game cells)
        // special towers (EfSpecialTurret); Kind null = the game's own bullets
        public string Kind;
        public float PoolSeconds = 6f, Radius, Duration, Slow, SlowSeconds = 1f, Vulnerable = 1f, Heal, Repair;   // Radius in cells
        public int PushCells, Bounces, TickAtk;
        // the game bullet the tower fires (Eff_Defense/effects/<...>; null keeps the template's), its flight and native effects
        public string Bullet, Muzzle, Hit; public float BulletSpeed = -1f, Drag = -1f, Shocktime = -1f;
    }

    public class TechSpec
    {
        public string Id, Name, Detail, Pre, Path, Materials, Template = "def-1";
        public float X, Y, Level;
        public int Icon = -1;   // frame in the game's Icon_Technology (borrowed from a neighbouring defence node)
        public List<string> Towers = new List<string>();
    }

    public static class EfData
    {
        public const string UnlockPrefix = "9";   // tech unlock item id = "9" + tower id

        public static readonly List<TowerSpec> Towers = new List<TowerSpec>
        {
            new TowerSpec { Id = "7101", Template = "10", Tier = 1, Tech = "ef-def-1", Name = "铳械塔", SortId = 60,
                Desc = "基础自动炮塔，双管交替连射能量弹，压制轻装步兵。",
                Hp = 2000, Atk = 44, MaxBullet = 4, Reload = 3.5f, Range = 24, Distance = 29, Consume = "80", Workload = 30,
                Materials = "12_6_25|12_15_30|12_16_40|12_58_4" },
            new TowerSpec { Id = "7102", Template = "30", Tier = 1, Tech = "ef-def-1", Name = "榴弹塔", SortId = 61,
                Desc = "发射榴弹，落点小范围爆炸，适合对付成群的敌人。",
                Hp = 2500, Atk = 75, DamageRange = 64, MaxBullet = 1, Reload = 2.4f, Range = 18, Distance = 22, Consume = "150", Workload = 45,
                Materials = "12_16_30|12_58_3|12_59_2|12_25_1" },
            new TowerSpec { Id = "7201", Template = "11", Tier = 2, Tech = "ef-def-2", Name = "扩装铳械塔", SortId = 60,
                Desc = "加装弹仓的铳械塔，射程和威力都有提升。",
                Hp = 3500, Atk = 56, MaxBullet = 4, Reload = 3.4f, Range = 26, Distance = 31, Consume = "100", Workload = 40,
                Materials = "12_60_40|12_61_8|12_24_10" },
            new TowerSpec { Id = "7301", Template = "12", Tier = 3, Tech = "ef-def-3", Name = "“暴雨”铳械塔", SortId = 60,
                Desc = "铳械塔的顶级型号，每轮六连射，火力密集。",
                Hp = 4500, Atk = 70, MaxBullet = 6, Reload = 3.4f, Range = 28, Distance = 33, Consume = "120", Workload = 50,
                Materials = "12_65_30|12_38_3|12_41_1|12_20_10" },
            new TowerSpec { Id = "7302", Template = "32", Tier = 3, Tech = "ef-def-3", Name = "高爆榴弹塔", SortId = 61,
                Desc = "装填高爆榴弹，爆炸范围更大，每轮两发。",
                Hp = 5000, Atk = 130, DamageRange = 96, MaxBullet = 2, Reload = 2.6f, Range = 20, Distance = 24, Consume = "150", Workload = 50,
                Materials = "12_65_25|12_20_30|12_41_1" },
            new TowerSpec { Id = "7303", Template = "22", Tier = 3, Tech = "ef-def-3", Name = "哨戒塔", SortId = 62,
                Desc = "远程精确射击，单发威力大，优先击杀高价值目标。",
                Hp = 6000, Atk = 120, MaxBullet = 1, Reload = 4.0f, Range = 36, Distance = 42, Consume = "150", Workload = 55,
                Materials = "12_65_36|12_41_1|12_38_2|12_22_4" },
            new TowerSpec { Id = "7304", Template = "52", Tier = 3, Tech = "ef-ray-3", Name = "射线塔", SortId = 63,
                Desc = "发射高能射线，瞬间命中目标。",
                Hp = 10000, Atk = 110, Range = 31, Distance = 37, Consume = "200", Workload = 50,
                Materials = "12_65_10|12_20_10|12_37_4|12_38_1|12_22_6" },
            new TowerSpec { Id = "7401", Template = "53", Tier = 4, Tech = "ef-ray-4", Name = "高能射线塔", SortId = 60,
                Desc = "射线塔的强化型号，射程更远，威力更大。",
                Hp = 10000, Atk = 150, Range = 34, Distance = 40, Consume = "250", Workload = 60,
                Materials = "12_65_20|12_39_15|12_35_3|12_38_2" },
            new TowerSpec { Id = "7402", Template = "43", Tier = 4, Tech = "ef-heavy-4", Name = "重锤迫击炮", SortId = 61, Size = 3,
                Desc = "超远程曲射重炮，炮弹落点大范围爆炸。太近的目标打不到。",
                Hp = 6000, Atk = 160, DamageRange = 160, MaxBullet = 1, Reload = 6f, Rotation = 60f, Range = 40, Distance = 46,
                AimType = 3, Consume = "300", Workload = 70, MinRangeCells = 8,
                Materials = "12_65_40|12_39_20|12_41_2|12_42_4" },
            new TowerSpec { Id = "7403", Template = "33", Tier = 4, Tech = "ef-heavy-4", Name = "序章炮塔", SortId = 62, Size = 4,
                Desc = "大型远程重炮，炮弹威力大并带范围伤害。",
                Hp = 8000, Atk = 170, DamageRange = 96, MaxBullet = 1, Reload = 1.8f, Rotation = 80f, Range = 36, Distance = 42,
                Consume = "300", Workload = 80,
                Materials = "12_65_60|12_39_24|12_41_3|12_42_4" },

            // second batch: effects written by the mod (EfSpecialTurret)
            new TowerSpec { Id = "7103", Template = "30", Tier = 1, Tech = "ef-def-1", Name = "激流塔", SortId = 62, Kind = "jet",
                Desc = "喷出高压水流，把前方的敌人推开，不造成伤害。体型巨大的敌人推不动。",
                Hp = 2500, Atk = 0, Range = 10, Distance = 12, Reload = 4f, Radius = 1.6f, PushCells = 3, Consume = "100", Workload = 35,
                Bullet = "bullets\\Plasma", Muzzle = "muzzle\\Laser_Big", Hit = "-1", AimType = 2, Accuracy = "0", BulletSpeed = 22f, Materials = "12_16_30|12_15_20|12_58_4|12_59_1" },
            new TowerSpec { Id = "7202", Template = "40", Tier = 2, Tech = "ef-def-2", Name = "液氮塔", SortId = 61, Kind = "frost",
                Desc = "投射液氮罐，落点结成冰面，冰面上的敌人大幅减速。不造成伤害。",
                Hp = 3500, Atk = 10, Range = 14, Distance = 16, Reload = 4f, Radius = 2.5f, Duration = 3f, Slow = 0.6f, SlowSeconds = 2f,
                Consume = "120", Workload = 40, Bullet = "bullets\\Plasma_Big", Muzzle = "muzzle\\Missile", Hit = "bomb\\Laser", AimType = 3, Accuracy = "0", BulletSpeed = 14f, DamageRange = 160, Drag = 0.6f, Shocktime = 4f, Materials = "12_60_30|12_24_10|12_61_4|12_29_1" },
            new TowerSpec { Id = "7203", Template = "40", Tier = 2, Tech = "ef-def-2", Name = "“酸洗”", SortId = 62, Kind = "acid",
                Desc = "投射酸液罐，范围伤害，落点留下一摊酸液 6 秒。被溅到或踩进酸液的敌人被腐蚀：15 秒内受到的伤害提高 30%。",
                Hp = 3500, Atk = 60, Range = 15, Distance = 17, Reload = 5f, Radius = 2.5f, Duration = 15f, Vulnerable = 1.3f,
                Consume = "120", Workload = 40, Bullet = "bullets\\Missile", Muzzle = "muzzle\\Missile", Hit = "-1", AimType = 3, Accuracy = "0", BulletSpeed = 14f, DamageRange = 160, Materials = "12_60_30|12_24_12|12_29_1" },
            new TowerSpec { Id = "7204", Template = "51", Tier = 2, Tech = "ef-surge-2", Name = "电涌塔", SortId = 63, Kind = "chain",
                Desc = "发射电弧，命中目标后在附近的敌人之间弹射，最多弹射 3 次。",
                Hp = 5000, Atk = 60, Range = 12, Distance = 14, Reload = 3f, Radius = 3f, Bounces = 3, Consume = "150", Workload = 40,
                Bullet = "bullets\\Laser", Muzzle = "muzzle\\Laser", Hit = "bomb\\Plasma", AimType = 2, Accuracy = "0", BulletSpeed = 40f, Materials = "12_60_20|12_56_6|12_29_2|12_22_4" },
            new TowerSpec { Id = "7205", Template = "11", Tier = 2, Tech = "ef-def-2", Name = "医疗塔", SortId = 64, Kind = "medic",
                Desc = "每 5 秒为范围内的己方人员恢复 20% 生命，并修复受损建筑 10% 耐久。",
                Hp = 3000, Atk = 0, Range = 8, Distance = 8, Reload = 5f, Heal = 0.2f, Repair = 0.1f, Consume = "120", Workload = 40,
                Materials = "12_60_20|12_29_2|12_24_8" },
            new TowerSpec { Id = "7305", Template = "42", Tier = 3, Tech = "ef-chem-3", Name = "“毒沼”", SortId = 64, Kind = "poison",
                Desc = "投射毒剂罐，落点形成一片毒沼 5 秒。敌人中毒持续掉血并减速，离开毒沼后仍持续 5 秒。",
                Hp = 5000, Atk = 20, TickAtk = 20, Range = 16, Distance = 18, Reload = 5f, Radius = 3f, Duration = 5f, Slow = 0.5f, SlowSeconds = 1f,
                Consume = "150", Workload = 50, Bullet = "bullets\\Plasma_Big", Muzzle = "muzzle\\Missile", Hit = "bomb\\Plasma_Big", AimType = 3, Accuracy = "0", BulletSpeed = 14f, DamageRange = 192, Drag = 0.5f, Shocktime = 2f, Materials = "12_65_25|12_20_15|12_37_2" },
            new TowerSpec { Id = "7306", Template = "52", Tier = 3, Tech = "ef-chem-3", Name = "全向声波塔", SortId = 65, Kind = "wave",
                Desc = "向目标方向发出扇形声波，使范围内的敌人眩晕 2 秒，不造成伤害。大型敌人不受影响。",
                Hp = 6000, Atk = 0, Range = 12, Distance = 14, Reload = 5f, Duration = 2f, Consume = "150", Workload = 50,
                Materials = "12_65_20|12_38_2|12_29_4" },
            new TowerSpec { Id = "7307", Template = "32", Tier = 3, Tech = "ef-def-3", Name = "AF1“熔甲者”", SortId = 66, Kind = "flame",
                Desc = "近距离喷射火焰，持续灼烧前方的所有敌人。",
                Hp = 6000, Atk = 45, Range = 8, Distance = 9, Reload = 3f, Radius = 1.6f, Consume = "150", Workload = 50,
                Materials = "12_65_25|12_20_20|12_18_30" },
            new TowerSpec { Id = "7404", Template = "53", Tier = 4, Tech = "ef-ray-4", Name = "甲二型“电荷”", SortId = 63, Kind = "charge",
                Desc = "释放强电弧并在目标处留下电球。电弧最多弹射 4 次；电球持续 5 秒，灼伤周围的敌人。",
                Hp = 10000, Atk = 110, TickAtk = 30, Range = 14, Distance = 16, Reload = 4f, Radius = 3f, Bounces = 4, Duration = 5f,
                Consume = "250", Workload = 60, Bullet = "bullets\\Plasma_Big", Muzzle = "muzzle\\Plasma_Big", Hit = "bomb\\Plasma", AimType = 2, Accuracy = "0", BulletSpeed = 36f, Materials = "12_65_20|12_39_12|12_35_2|12_38_3" },
        };

        // Tech nodes sit next to the game's own nodes of the same level. Positions come from tools/tech_layout.py, a
        // search over the game's whole tree (every card and connecting line) so that no card covers another card or a
        // line; the self-test (--tests techtree) opens the game's tree on each node for a screenshot.
        public static readonly List<TechSpec> Techs = new List<TechSpec>
        {
            new TechSpec { Id = "ef-def-1", Name = "工业炮塔基础", Icon = 169, Level = 1, Pre = "part-1", X = 2950, Y = -125,
                Path = "part-1,t:2850.0,-125:l", Materials = "12_16_60|12_6_40",
                Detail = "解锁铳械塔、榴弹塔、激流塔。" },
            new TechSpec { Id = "ef-def-2", Name = "工业炮塔强化", Icon = 170, Level = 2, Pre = "def-2", X = 9850, Y = 700,
                Path = "def-2,r:9750.0,400.0_9750.0,700:l", Materials = "12_60_15|12_24_8|12_62_1",
                Detail = "解锁扩装铳械塔、液氮塔、“酸洗”、医疗塔。" },
            new TechSpec { Id = "ef-surge-2", Name = "电涌技术", Icon = 174, Level = 2, Pre = "226", X = 11300, Y = 800,
                Path = "226,t:11200.0,800:l", Materials = "12_29_1|12_22_2|12_33_1",
                Detail = "解锁电涌塔。" },
            new TechSpec { Id = "ef-def-3", Name = "工业炮塔自动化", Icon = 171, Level = 3, Pre = "def-3", X = 16300, Y = 275,
                Path = "def-3,r:16200.0,300.0_16200.0,275:l", Materials = "12_41_1|12_20_15|12_33_2",
                Detail = "解锁“暴雨”铳械塔、高爆榴弹塔、哨戒塔、AF1“熔甲者”。" },
            new TechSpec { Id = "ef-chem-3", Name = "化学阵地", Icon = 258, Level = 3, Pre = "ef-def-3", X = 16400, Y = 150,
                Path = "ef-def-3,t:16300.0,150:l", Materials = "12_41_1|12_37_2|12_20_10",
                Detail = "解锁“毒沼”、全向声波塔。" },
            new TechSpec { Id = "ef-ray-3", Name = "射线技术", Icon = 261, Level = 3, Pre = "chip-3", X = 17800, Y = 125,
                Path = "chip-3,r:17700.0,0.0_17700.0,125:l", Materials = "12_37_5|12_38_2|12_20_8",
                Detail = "解锁射线塔。" },
            new TechSpec { Id = "ef-heavy-4", Name = "工业重火力", Icon = 259, Level = 4, Pre = "2159", X = 22900, Y = 725,
                Path = "2159,r::l", Materials = "12_42_8|12_39_6|12_27_2",
                Detail = "解锁重锤迫击炮、序章炮塔。" },
            new TechSpec { Id = "ef-ray-4", Name = "高能技术", Icon = 173, Level = 4, Pre = "335", X = 23750, Y = 425,
                Path = "335,t:23650.0,425:l", Materials = "12_35_4|12_38_2|12_39_7",
                Detail = "解锁高能射线塔、甲二型“电荷”。" },
        };

        static Dictionary<string, TowerSpec> byId;
        public static TowerSpec Find(string id)
        {
            if (byId == null) { byId = new Dictionary<string, TowerSpec>(); foreach (var t in Towers) byId[t.Id] = t; }
            return id != null && byId.TryGetValue(id, out var s) ? s : null;
        }
    }
}
