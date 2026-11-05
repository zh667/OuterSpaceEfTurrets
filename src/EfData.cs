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
    }

    public class TechSpec
    {
        public string Id, Name, Detail, Pre, Path, Materials, Template = "def-1";
        public float X, Y, Level;
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
        };

        // Tech nodes sit next to the game's own defence nodes of the same level (positions checked against the tree).
        public static readonly List<TechSpec> Techs = new List<TechSpec>
        {
            new TechSpec { Id = "ef-def-1", Name = "工业炮塔基础", Level = 1, Pre = "part-1", X = 2950, Y = 375,
                Path = "part-1,t:2850.0,375.0:l", Materials = "12_16_60|12_6_40",
                Detail = "解锁铳械塔、榴弹塔。" },
            new TechSpec { Id = "ef-def-2", Name = "工业炮塔强化", Level = 2, Pre = "def-2", X = 9900, Y = 425,
                Path = "def-2,r:9800.0,400.0_9800.0,425.0:l", Materials = "12_60_15|12_24_8|12_62_1",
                Detail = "解锁扩装铳械塔。" },
            new TechSpec { Id = "ef-def-3", Name = "工业炮塔自动化", Level = 3, Pre = "def-3", X = 16250, Y = 275,
                Path = "def-3,r:16150.0,300.0_16150.0,275.0:l", Materials = "12_41_1|12_20_15|12_33_2",
                Detail = "解锁“暴雨”铳械塔、高爆榴弹塔、哨戒塔。" },
            new TechSpec { Id = "ef-ray-3", Name = "射线技术", Level = 3, Pre = "342", X = 17750, Y = 500,
                Path = "342,r:17650.0,250.0_17650.0,500.0:l", Materials = "12_37_5|12_38_2|12_20_8",
                Detail = "解锁射线塔。" },
            new TechSpec { Id = "ef-heavy-4", Name = "工业重火力", Level = 4, Pre = "2159", X = 22850, Y = 725,
                Path = "2159,r::l", Materials = "12_42_8|12_39_6|12_27_2",
                Detail = "解锁重锤迫击炮、序章炮塔。" },
            new TechSpec { Id = "ef-ray-4", Name = "高能射线技术", Level = 4, Pre = "335", X = 24050, Y = 300,
                Path = "335,r::l", Materials = "12_35_4|12_38_2|12_39_7",
                Detail = "解锁高能射线塔。" },
        };

        static Dictionary<string, TowerSpec> byId;
        public static TowerSpec Find(string id)
        {
            if (byId == null) { byId = new Dictionary<string, TowerSpec>(); foreach (var t in Towers) byId[t.Id] = t; }
            return id != null && byId.TryGetValue(id, out var s) ? s : null;
        }
    }
}
