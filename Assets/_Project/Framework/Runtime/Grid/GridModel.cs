using System.Collections.Generic;
using UnityEngine;

// 运行时的网格逻辑模型（柱体模型）。
// 每个 (x,z) 是一根从 y=0 往上堆的实心柱；角色站在柱顶。
// 「地板」= 矮柱，「墙」= 高柱；可解锁墙按"按钮是否被压 + 墙格是否被占"实时显隐。

namespace Sokoban3D.Framework
{
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

        readonly int _w;
        readonly int _d;
        readonly int[] _height;
        readonly string[] _surface;
        readonly bool[] _has;
        readonly int[] _extra;      // 当前存在的可解锁墙叠加高度
        readonly int _baseHeight;
        readonly string _baseMaterial;
        readonly Dictionary<Int3, BoxState> _boxAt = new Dictionary<Int3, BoxState>();

        readonly List<Lock> _locks = new List<Lock>();
        readonly HashSet<Int3> _actors = new HashSet<Int3>();
        readonly HashSet<string> _pressed = new HashSet<string>();

        // 一把可解锁墙
        public class Lock
        {
            public int x;
            public int z;
            public int height;
            public string color;
            public bool present = true;
        }

        public GridModel(LevelData data)
        {
            Size = data.size;
            _w = Mathf.Max(1, Size.x);
            _d = Mathf.Max(1, Size.z);
            _height = new int[_w * _d];
            _surface = new string[_w * _d];
            _has = new bool[_w * _d];
            _extra = new int[_w * _d];
            _baseHeight = data.baseHeight;
            _baseMaterial = data.baseMaterial;

            foreach (var t in data.terrain)
            {
                if (!InXZ(t.pos.x, t.pos.z)) continue;
                int i = Index(t.pos.x, t.pos.z);
                _has[i] = true;
                _height[i] = (t.type == "void") ? 0 : (t.pos.y + 1);
                _surface[i] = t.type;
            }

            foreach (var m in data.markers)
            {
                if (m.type != "unlock_wall" || !InXZ(m.pos.x, m.pos.z)) continue;
                _locks.Add(new Lock
                {
                    x = m.pos.x,
                    z = m.pos.z,
                    height = Mathf.Max(1, m.height),
                    color = m.color
                });
            }

            foreach (var o in data.objects)
            {
                var p = new Int3(o.pos.x, TerrainHeightAt(o.pos.x, o.pos.z), o.pos.z);
                var box = new BoxState { type = o.type, pos = p };
                Boxes.Add(box);
                if (!_boxAt.ContainsKey(p)) _boxAt[p] = box;
            }

            foreach (var m in data.markers)
            {
                Markers.Add(new MarkerEntry
                {
                    type = m.type,
                    pos = new Int3(m.pos.x, TerrainHeightAt(m.pos.x, m.pos.z), m.pos.z),
                    player = m.player,
                    color = m.color,
                    height = m.height
                });
            }

            RecomputeWalls();
        }

        // 设置当前所有角色所在格（用于按钮触发与墙格占用判定）
        public void SetActors(IEnumerable<Int3> actors)
        {
            _actors.Clear();
            if (actors != null)
            {
                foreach (var a in actors) _actors.Add(a);
            }
            RecomputeWalls();
        }

        // 重算可解锁墙的显隐：被压住的按钮 → 该色墙消失；格被箱/角色占住 → 也不出现
        void RecomputeWalls()
        {
            _pressed.Clear();
            for (int i = 0; i < Markers.Count; i++)
            {
                var m = Markers[i];
                if (m.type != "button") continue;
                int gh = TerrainHeightAt(m.pos.x, m.pos.z);   // 按钮站在地形面上
                if (ActorAtLevel(m.pos.x, m.pos.z, gh) || BoxAtLevel(m.pos.x, m.pos.z, gh)) _pressed.Add(m.color);
            }

            for (int i = 0; i < _extra.Length; i++) _extra[i] = 0;

            for (int i = 0; i < _locks.Count; i++)
            {
                var l = _locks[i];
                bool pressed = _pressed.Contains(l.color);
                int wallTop = TerrainHeightAt(l.x, l.z) + l.height;
                // 角色/箱子与墙的竖直区间重叠 → 墙不出现；站在墙顶（y == wallTop）不算重叠
                bool occupied = ActorBelow(l.x, l.z, wallTop) || BoxBelow(l.x, l.z, wallTop);
                l.present = !pressed && !occupied;
                if (l.present) _extra[Index(l.x, l.z)] += l.height;
            }
        }

        bool ActorAtLevel(int x, int z, int y)
        {
            foreach (var a in _actors)
                if (a.x == x && a.z == z && a.y == y) return true;
            return false;
        }

        bool ActorBelow(int x, int z, int top)
        {
            foreach (var a in _actors)
                if (a.x == x && a.z == z && a.y < top) return true;
            return false;
        }

        bool BoxAtLevel(int x, int z, int y)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i].pos;
                if (b.x == x && b.z == z && b.y == y) return true;
            }
            return false;
        }

        bool BoxBelow(int x, int z, int top)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                var b = Boxes[i].pos;
                if (b.x == x && b.z == z && b.y < top) return true;
            }
            return false;
        }

        bool BoxAtXZ(int x, int z)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                if (Boxes[i].pos.x == x && Boxes[i].pos.z == z) return true;
            }
            return false;
        }

        bool ActorAtXZ(int x, int z)
        {
            foreach (var a in _actors)
            {
                if (a.x == x && a.z == z) return true;
            }
            return false;
        }

        public bool IsWallPresent(int x, int z, string color)
        {
            for (int i = 0; i < _locks.Count; i++)
            {
                var l = _locks[i];
                if (l.x == x && l.z == z && l.color == color) return l.present;
            }
            return false;
        }

        int Index(int x, int z)
        {
            return x + z * _w;
        }

        public bool InXZ(int x, int z)
        {
            return x >= 0 && x < _w && z >= 0 && z < _d;
        }

        public bool IsVoid(int x, int z)
        {
            return HeightAt(x, z) <= 0;
        }

        // 不含可解锁墙的底层地形高
        public int TerrainHeightAt(int x, int z)
        {
            if (!InXZ(x, z)) return 0;
            int i = Index(x, z);
            return _has[i] ? _height[i] : _baseHeight;
        }

        public int HeightAt(int x, int z)
        {
            if (!InXZ(x, z)) return 0;
            return TerrainHeightAt(x, z) + _extra[Index(x, z)];
        }

        // 支撑面：地形 + 现存可解锁墙（箱子/角色站在这上面）
        public int FloorAt(int x, int z)
        {
            return HeightAt(x, z);
        }

        // 可站立顶面：支撑面 + 该格箱子的 1 高（箱子顶 / 墙顶）
        public int TopAt(int x, int z)
        {
            if (!InXZ(x, z)) return 0;
            return HeightAt(x, z) + (BoxAtXZ(x, z) ? 1 : 0);
        }

        public BoxState BoxStateAtXZ(int x, int z)
        {
            for (int i = 0; i < Boxes.Count; i++)
            {
                if (Boxes[i].pos.x == x && Boxes[i].pos.z == z) return Boxes[i];
            }
            return null;
        }

        public string SurfaceAt(int x, int z)
        {
            if (!InXZ(x, z)) return null;
            int i = Index(x, z);
            return _has[i] ? _surface[i] : _baseMaterial;
        }

        public int WalkLevel(int x, int z)
        {
            return HeightAt(x, z);
        }

        public bool IsSolid(Int3 p)
        {
            if (!InXZ(p.x, p.z)) return true;
            int h = HeightAt(p.x, p.z);
            if (h <= 0) return true;
            return p.y >= 0 && p.y < h;
        }

        public bool IsStandable(Int3 p)
        {
            if (!InXZ(p.x, p.z)) return false;
            return TopAt(p.x, p.z) == p.y;
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

        public bool IsSolved()
        {
            foreach (var m in Markers)
            {
                if (m.type != "target") continue;
                if (!BoxAtXZ(m.pos.x, m.pos.z)) return false;
            }
            return true;
        }
    }
}
