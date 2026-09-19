using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 推箱子求解器（BFS）。小关卡可精确判定；超出搜索上限返回 Timeout。
// 机制：箱子/玩家压住解锁按钮 → 同色可解锁墙消失；松开且墙格无人/无箱 → 重新出现。
// 玩家可走到更低邻格并掉下去；箱子不会坠落（必须同层推）。

namespace Sokoban3D.Framework
{
    public enum SolveResult { Solved, Unsolvable, Timeout }

    public static class LevelSolver
    {
        class Node
        {
            public Int3 player;
            public Int3[] boxes;
            public Node parent;
        }

        class LockInfo
        {
            public int x, z, height;
            public string color;
        }

        public static SolveResult Solve(LevelData data, int maxNodes, out int steps, out int pushes)
        {
            steps = 0;
            pushes = 0;
            if (data == null) return SolveResult.Unsolvable;

            int w = Mathf.Max(1, data.size.x);
            int d = Mathf.Max(1, data.size.z);

            // 底层地形高（不含可解锁墙）
            var terrainH = new int[w * d];
            for (int i = 0; i < terrainH.Length; i++) terrainH[i] = Mathf.Max(0, data.baseHeight);
            foreach (var t in data.terrain)
            {
                if (t.pos.x < 0 || t.pos.x >= w || t.pos.z < 0 || t.pos.z >= d) continue;
                terrainH[t.pos.x + t.pos.z * w] = (t.type == "void") ? 0 : (t.pos.y + 1);
            }

            var locks = new List<LockInfo>();
            var buttons = new List<KeyValuePair<Int3, string>>();
            Int3 start = default;
            bool hasSpawn = false;
            var targets = new HashSet<Int3>();

            foreach (var m in data.markers)
            {
                if (m.type == "spawn" && !hasSpawn)
                {
                    int sx = Mathf.Clamp(m.pos.x, 0, w - 1);
                    int sz = Mathf.Clamp(m.pos.z, 0, d - 1);
                    start = new Int3(m.pos.x, terrainH[sx + sz * w], m.pos.z);
                    hasSpawn = true;
                }
                else if (m.type == "target") targets.Add(new Int3(m.pos.x, 0, m.pos.z));
                else if (m.type == "button") buttons.Add(new KeyValuePair<Int3, string>(new Int3(m.pos.x, 0, m.pos.z), m.color));
                else if (m.type == "unlock_wall") locks.Add(new LockInfo { x = m.pos.x, z = m.pos.z, height = Mathf.Max(1, m.height), color = m.color });
            }
            if (!hasSpawn || targets.Count == 0) return SolveResult.Unsolvable;

            var boxes = new Int3[data.objects.Count];
            for (int i = 0; i < boxes.Length; i++)
            {
                int bx = Mathf.Clamp(data.objects[i].pos.x, 0, w - 1);
                int bz = Mathf.Clamp(data.objects[i].pos.z, 0, d - 1);
                boxes[i] = new Int3(data.objects[i].pos.x, terrainH[bx + bz * w], data.objects[i].pos.z);
            }

            var visited = new HashSet<string>();
            var queue = new Queue<Node>();
            var root = new Node { player = start, boxes = boxes, parent = null };
            queue.Enqueue(root);
            visited.Add(Key(root, w, d));

            Int3[] dirs = { new Int3(1,0,0), new Int3(-1,0,0), new Int3(0,0,1), new Int3(0,0,-1) };

            int nodes = 0;
            var pressed = new HashSet<string>();

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (++nodes > maxNodes) return SolveResult.Timeout;

                if (Solved(cur, targets))
                {
                    var n = cur;
                    while (n.parent != null)
                    {
                        steps++;
                        if (BoxAt(n.parent, n.player) >= 0) pushes++;
                        n = n.parent;
                    }
                    return SolveResult.Solved;
                }

                // 当前状态的按钮按压（玩家/箱子与按钮同层同格）
                pressed.Clear();
                for (int i = 0; i < buttons.Count; i++)
                {
                    var bp = buttons[i].Key;
                    int by = terrainH[bp.x + bp.z * w];
                    if (cur.player.x == bp.x && cur.player.z == bp.z && cur.player.y == by) pressed.Add(buttons[i].Value);
                    else
                    {
                        for (int j = 0; j < cur.boxes.Length; j++)
                        {
                            var b = cur.boxes[j];
                            if (b.x == bp.x && b.z == bp.z && b.y == by) { pressed.Add(buttons[i].Value); break; }
                        }
                    }
                }

                for (int i = 0; i < dirs.Length; i++)
                {
                    Int3 flat = cur.player + dirs[i];
                    if (flat.x < 0 || flat.x >= w || flat.z < 0 || flat.z >= d) continue;

                    int floor = FloorEff(flat.x, flat.z, terrainH, w, locks, pressed, cur.player, cur.boxes);
                    int bi = BoxIndexAtXZ(cur.boxes, flat);
                    int boxBase = bi >= 0 ? cur.boxes[bi].y : -1;
                    int top = floor + (bi >= 0 ? 1 : 0);        // 顶面（含箱子 +1）
                    if (top <= 0) continue;                     // 虚空

                    Node next;
                    if (top <= cur.player.y)
                    {
                        // 齐平（含踩箱子顶 / 墙顶）或更低（掉下去）
                        next = new Node { player = new Int3(flat.x, top, flat.z), boxes = cur.boxes, parent = cur };
                    }
                    else
                    {
                        // 更高：只有箱子底面与脚下齐平才能推
                        if (bi < 0 || boxBase != cur.player.y) continue;

                        Int3 beyond = flat + dirs[i];
                        if (beyond.x < 0 || beyond.x >= w || beyond.z < 0 || beyond.z >= d) continue;
                        if (BoxIndexAtXZ(cur.boxes, beyond) >= 0) continue;

                        int dfloor = FloorEff(beyond.x, beyond.z, terrainH, w, locks, pressed, cur.player, cur.boxes);
                        if (dfloor <= 0 || dfloor > cur.player.y) continue;   // 虚空 / 更高

                        var nb = (Int3[])cur.boxes.Clone();
                        nb[bi] = new Int3(beyond.x, dfloor, beyond.z);
                        next = new Node { player = new Int3(flat.x, cur.player.y, flat.z), boxes = nb, parent = cur };
                    }

                    string k = Key(next, w, d);
                    if (visited.Add(k)) queue.Enqueue(next);
                }
            }

            return SolveResult.Unsolvable;
        }

        // 目标格的支撑面（地形 + 现存可解锁墙）。站在墙顶/箱顶不算与墙重叠。
        static int FloorEff(int x, int z, int[] terrainH, int w, List<LockInfo> locks,
                            HashSet<string> pressed, Int3 player, Int3[] boxes)
        {
            int idx = x + z * w;
            int h = terrainH[idx];
            for (int i = 0; i < locks.Count; i++)
            {
                var l = locks[i];
                if (l.x != x || l.z != z) continue;
                if (pressed.Contains(l.color)) continue;

                int wallTop = terrainH[idx] + l.height;
                if (player.x == x && player.z == z && player.y < wallTop) continue;

                bool overlap = false;
                for (int j = 0; j < boxes.Length; j++)
                {
                    var b = boxes[j];
                    if (b.x == x && b.z == z && b.y < wallTop) { overlap = true; break; }
                }
                if (overlap) continue;

                h += l.height;
            }
            return h;
        }

        static int BoxIndexAtXZ(Int3[] boxes, Int3 c)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i].x == c.x && boxes[i].z == c.z) return i;
            }
            return -1;
        }

        static int BoxAt(Node n, Int3 p)
        {
            for (int i = 0; i < n.boxes.Length; i++)
            {
                var b = n.boxes[i];
                if (b.x == p.x && b.z == p.z) return i;
            }
            return -1;
        }

        static bool Solved(Node n, HashSet<Int3> targets)
        {
            // 通关条件：每个抵达点都被箱子覆盖（箱子可以多于抵达点）
            foreach (var t in targets)
            {
                bool covered = false;
                for (int i = 0; i < n.boxes.Length; i++)
                {
                    if (n.boxes[i].x == t.x && n.boxes[i].z == t.z) { covered = true; break; }
                }
                if (!covered) return false;
            }
            return true;
        }

        static string Key(Node n, int w, int d0)
        {
            var idx = new int[n.boxes.Length];
            for (int i = 0; i < n.boxes.Length; i++) idx[i] = n.boxes[i].x + n.boxes[i].z * w + n.boxes[i].y * w * d0;
            System.Array.Sort(idx);

            var sb = new StringBuilder();
            sb.Append(n.player.x + n.player.z * w + n.player.y * w * d0);
            for (int i = 0; i < idx.Length; i++) { sb.Append('.'); sb.Append(idx[i]); }
            return sb.ToString();
        }
    }
}
