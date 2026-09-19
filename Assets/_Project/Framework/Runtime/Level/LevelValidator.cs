using System.Collections.Generic;
using UnityEngine;

// 关卡结构校验：不依赖编辑器，纯逻辑。编辑器与以后的 CI 都能用。

namespace Sokoban3D.Framework
{
    public enum LevelIssueType { Error, Warning, Info }

    public class LevelIssue
    {
        public LevelIssueType type;
        public string message;
        public bool hasCell;
        public Int3 cell;
    }

    public static class LevelValidator
    {
        public static List<LevelIssue> Validate(LevelData data)
        {
            var issues = new List<LevelIssue>();
            if (data == null)
            {
                issues.Add(new LevelIssue { type = LevelIssueType.Error, message = "关卡数据为空" });
                return issues;
            }

            int sx = data.size.x;
            int sz = data.size.z;

            var allTerrain = new HashSet<long>();
            var solidTerrain = new HashSet<long>();

            for (int i = 0; i < data.terrain.Count; i++)
            {
                var t = data.terrain[i];
                if (t.pos.x < 0 || t.pos.x >= sx || t.pos.z < 0 || t.pos.z >= sz)
                {
                    issues.Add(Cell(LevelIssueType.Error, "地形越界", t.pos));
                    continue;
                }

                long k = Key(t.pos.x, t.pos.z);
                if (!allTerrain.Add(k))
                {
                    issues.Add(Cell(LevelIssueType.Error, "同一格有多个地形", t.pos));
                }
                if (t.type != "void") solidTerrain.Add(k);
            }

            CheckMechanisms(data, issues, sx, sz, solidTerrain);

            // 出生点
            var spawns = new List<MarkerEntry>();
            for (int i = 0; i < data.markers.Count; i++)
            {
                if (data.markers[i].type == "spawn") spawns.Add(data.markers[i]);
            }

            if (spawns.Count == 0)
            {
                issues.Add(new LevelIssue { type = LevelIssueType.Error, message = "没有出生点" });
            }
            else
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < spawns.Count; i++)
                {
                    if (!seen.Add(spawns[i].player))
                    {
                        issues.Add(Cell(LevelIssueType.Warning, "出生点玩家编号重复：" + spawns[i].player, spawns[i].pos));
                    }
                }
            }

            // 箱子 / 抵达点
            int boxes = 0, targets = 0;
            for (int i = 0; i < data.objects.Count; i++)
            {
                if (data.objects[i].type == "box") boxes++;
            }
            for (int i = 0; i < data.markers.Count; i++)
            {
                if (data.markers[i].type == "target") targets++;
            }

            if (boxes < targets)
            {
                issues.Add(new LevelIssue
                {
                    type = LevelIssueType.Error,
                    message = "箱子数(" + boxes + ") 少于抵达点数(" + targets + ")，无法全部覆盖"
                });
            }
            if (targets == 0)
            {
                issues.Add(new LevelIssue { type = LevelIssueType.Warning, message = "没有抵达点，无法通关" });
            }

            // 机制颜色配套
            var buttonColors = new HashSet<string>();
            var lockColors = new HashSet<string>();
            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                if (m.type == "button") buttonColors.Add(m.color);
                else if (m.type == "unlock_wall") lockColors.Add(m.color);
            }

            foreach (var c in buttonColors)
            {
                if (!lockColors.Contains(c))
                {
                    issues.Add(new LevelIssue { type = LevelIssueType.Warning, message = "解锁按钮(颜色" + c + ")没有对应的可解锁墙" });
                }
            }
            foreach (var c in lockColors)
            {
                if (!buttonColors.Contains(c))
                {
                    issues.Add(new LevelIssue { type = LevelIssueType.Warning, message = "可解锁墙(颜色" + c + ")没有对应的解锁按钮，无法开启" });
                }
            }

            return issues;
        }

        static void CheckMechanisms(LevelData data, List<LevelIssue> issues, int sx, int sz, HashSet<long> solidTerrain)
        {
            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                if (o.pos.x < 0 || o.pos.x >= sx || o.pos.z < 0 || o.pos.z >= sz)
                {
                    issues.Add(Cell(LevelIssueType.Error, "机关越界（" + o.type + "）", o.pos));
                }
                else if (!HasSupport(o.pos.x, o.pos.z, solidTerrain, data.baseHeight))
                {
                    issues.Add(Cell(LevelIssueType.Error, "机关悬空，没有支撑（" + o.type + "）", o.pos));
                }
            }

            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                if (m.pos.x < 0 || m.pos.x >= sx || m.pos.z < 0 || m.pos.z >= sz)
                {
                    issues.Add(Cell(LevelIssueType.Error, "机关越界（" + m.type + "）", m.pos));
                }
                else if (!HasSupport(m.pos.x, m.pos.z, solidTerrain, data.baseHeight))
                {
                    issues.Add(Cell(LevelIssueType.Error, "机关悬空，没有支撑（" + m.type + "）", m.pos));
                }
            }
        }

        static bool HasSupport(int x, int z, HashSet<long> solidTerrain, int baseHeight)
        {
            if (solidTerrain.Contains(Key(x, z))) return true;
            return baseHeight > 0;
        }

        static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        static LevelIssue Cell(LevelIssueType type, string msg, Int3 c)
        {
            return new LevelIssue { type = type, message = msg, hasCell = true, cell = c };
        }
    }
}
