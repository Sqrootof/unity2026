using System;
using System.Collections.Generic;
using UnityEngine;

// 关卡数据格式定义（工具与运行时共用的数据契约）。
// 一个关卡 = 四张清单：地形 / 物件 / 标记 / 连线。坐标统一 (x, y, z)，第一章 y 恒为 0。

namespace Sokoban3D.Framework
{
    [Serializable]
    public struct Int3
    {
        public int x;
        public int y;
        public int z;

        public Int3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}, {2})", x, y, z);
        }

        public static Int3 operator +(Int3 a, Int3 b)
        {
            return new Int3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Int3 operator -(Int3 a, Int3 b)
        {
            return new Int3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static bool operator ==(Int3 a, Int3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        public static bool operator !=(Int3 a, Int3 b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return obj is Int3 && this == (Int3)obj;
        }

        public override int GetHashCode()
        {
            return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
        }
    }

    [Serializable]
    public class LevelData
    {
        public string id = "1-1";
        public string name = "";
        public int schemaVersion = 1;
        public Int3 size = new Int3(1, 1, 1);

        public List<TerrainEntry> terrain = new List<TerrainEntry>();
        public List<ObjectEntry> objects = new List<ObjectEntry>();
        public List<MarkerEntry> markers = new List<MarkerEntry>();
        public List<LinkEntry> links = new List<LinkEntry>();
    }

    [Serializable]
    public class TerrainEntry
    {
        // "floor" 地板 | "wall" 墙 | "void" 虚空 | 第四章加 "slope"
        public string type = "floor";
        public Int3 pos = new Int3(0, 0, 0);
        public int dir = 0; // 朝向：0=+x, 1=+z, 2=-x, 3=-z（只有坡用，第一章恒为 0）
    }

    [Serializable]
    public class ObjectEntry
    {
        // "box" 箱子 | "slope_box" 坡度箱（第四章）
        public string type = "box";
        public Int3 pos = new Int3(0, 0, 0);
        public int dir = 0;
    }

    [Serializable]
    public class MarkerEntry
    {
        // "spawn" 出生点 | "target" 抵达点 | "button" 按钮（第二章）
        public string type = "target";
        public Int3 pos = new Int3(0, 0, 0);
        public int player = -1; // 只有 spawn 用：0=第一个角色
        public string color = ""; // 只有 button 用，如 "red"
    }

    [Serializable]
    public class LinkEntry
    {
        public Int3 source = new Int3(0, 0, 0); // 触发器（如按钮）所在格
        public Int3 target = new Int3(0, 0, 0); // 被控制的目标（如可解锁墙）所在格
        public string action = "unlock";        // "unlock" | "open" | "close"
    }
}
