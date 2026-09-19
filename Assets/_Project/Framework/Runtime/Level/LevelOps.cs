using UnityEngine;

// 关卡数据操作：缩尺寸时清理越界数据、裁切外圈虚空。

namespace Sokoban3D.Framework
{
    public static class LevelOps
    {
        // 删除越界（x>=size.x 或 z>=size.z）的地形/物件/标记
        public static void CropOutOfBounds(LevelData data)
        {
            int nx = data.size.x;
            int nz = data.size.z;

            data.terrain.RemoveAll(t => t.pos.x < 0 || t.pos.x >= nx || t.pos.z < 0 || t.pos.z >= nz);
            data.objects.RemoveAll(o => o.pos.x < 0 || o.pos.x >= nx || o.pos.z < 0 || o.pos.z >= nz);
            data.markers.RemoveAll(m => m.pos.x < 0 || m.pos.x >= nx || m.pos.z < 0 || m.pos.z >= nz);
        }

        // 裁切外圈：任一边整条都是虚空就去掉，并整体平移坐标 / 缩小尺寸。返回是否发生了裁切。
        public static bool TrimOuterVoid(LevelData data)
        {
            bool any = false;

            while (true)
            {
                int sx = data.size.x;
                int sz = data.size.z;
                if (sx <= 1 || sz <= 1) break;

                if (ColumnAllVoid(data, 0)) { RemoveColumn(data, 0); data.size.x--; any = true; continue; }
                if (ColumnAllVoid(data, sx - 1)) { RemoveColumn(data, sx - 1); data.size.x--; any = true; continue; }
                if (RowAllVoid(data, 0)) { RemoveRow(data, 0); data.size.z--; any = true; continue; }
                if (RowAllVoid(data, sz - 1)) { RemoveRow(data, sz - 1); data.size.z--; any = true; continue; }

                break;
            }

            return any;
        }

        static bool IsVoidCell(LevelData data, int x, int z)
        {
            for (int i = 0; i < data.terrain.Count; i++)
            {
                var t = data.terrain[i];
                if (t.pos.x == x && t.pos.z == z) return t.type == "void";
            }
            return data.baseHeight <= 0;
        }

        static bool ColumnAllVoid(LevelData data, int x)
        {
            for (int z = 0; z < data.size.z; z++)
            {
                if (!IsVoidCell(data, x, z)) return false;
            }
            return true;
        }

        static bool RowAllVoid(LevelData data, int z)
        {
            for (int x = 0; x < data.size.x; x++)
            {
                if (!IsVoidCell(data, x, z)) return false;
            }
            return true;
        }

        static void RemoveColumn(LevelData data, int x)
        {
            data.terrain.RemoveAll(t => t.pos.x == x);
            data.objects.RemoveAll(o => o.pos.x == x);
            data.markers.RemoveAll(m => m.pos.x == x);

            for (int i = 0; i < data.terrain.Count; i++)
            {
                var e = data.terrain[i];
                if (e.pos.x > x) { var p = e.pos; p.x--; e.pos = p; }
            }
            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                if (o.pos.x > x) { var p = o.pos; p.x--; o.pos = p; }
            }
            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                if (m.pos.x > x) { var p = m.pos; p.x--; m.pos = p; }
            }
        }

        static void RemoveRow(LevelData data, int z)
        {
            data.terrain.RemoveAll(t => t.pos.z == z);
            data.objects.RemoveAll(o => o.pos.z == z);
            data.markers.RemoveAll(m => m.pos.z == z);

            for (int i = 0; i < data.terrain.Count; i++)
            {
                var e = data.terrain[i];
                if (e.pos.z > z) { var p = e.pos; p.z--; e.pos = p; }
            }
            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                if (o.pos.z > z) { var p = o.pos; p.z--; o.pos = p; }
            }
            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                if (m.pos.z > z) { var p = m.pos; p.z--; m.pos = p; }
            }
        }
    }
}
