using System.Collections.Generic;
using UnityEngine;

// 运行时的网格逻辑模型。纯 C#，不挂 MonoBehaviour，不负责画面。
// 只回答"这格是什么地形、有没有箱子、目标是否全部达成"。

namespace Sokoban3D.Framework
{
    // 运行时的一个箱子（位置会随推动变化）
    public class BoxState
    {
        public string type;
        public Int3 pos;
    }

    public class GridModel
    {
        public readonly Int3 Size;
        public readonly List<BoxState> Boxes = new List<BoxState>();
        public readonly List<MarkerEntry> Markers = new List<MarkerEntry>();

        readonly string[] _terrain;
        readonly Dictionary<Int3, BoxState> _boxAt = new Dictionary<Int3, BoxState>();

        public GridModel(LevelData data)
        {
            Size = data.size;
            _terrain = new string[Mathf.Max(1, Size.x * Size.y * Size.z)];

            foreach (var t in data.terrain)
            {
                if (InBounds(t.pos)) _terrain[Index(t.pos)] = t.type;
            }

            foreach (var o in data.objects)
            {
                var box = new BoxState { type = o.type, pos = o.pos };
                Boxes.Add(box);
                if (!_boxAt.ContainsKey(o.pos)) _boxAt[o.pos] = box;
            }

            Markers.AddRange(data.markers);
        }

        public bool InBounds(Int3 p)
        {
            return p.x >= 0 && p.x < Size.x
                && p.y >= 0 && p.y < Size.y
                && p.z >= 0 && p.z < Size.z;
        }

        int Index(Int3 p)
        {
            return p.x + p.z * Size.x + p.y * Size.x * Size.z;
        }

        // 没写地形的格子一律当作虚空
        public string GetTerrain(Int3 p)
        {
            if (!InBounds(p)) return "void";
            return _terrain[Index(p)] ?? "void";
        }

        public bool IsWalkable(Int3 p)
        {
            return GetTerrain(p) == "floor";
        }

        public bool HasBox(Int3 p)
        {
            return _boxAt.ContainsKey(p);
        }

        public BoxState GetBox(Int3 p)
        {
            BoxState b;
            return _boxAt.TryGetValue(p, out b) ? b : null;
        }

        public void MoveBox(BoxState box, Int3 to)
        {
            _boxAt.Remove(box.pos);
            box.pos = to;
            _boxAt[to] = box;
        }

        // 所有抵达点是否都被箱子盖住
        public bool IsSolved()
        {
            foreach (var m in Markers)
            {
                if (m.type != "target") continue;
                if (!HasBox(m.pos)) return false;
            }
            return true;
        }
    }
}
