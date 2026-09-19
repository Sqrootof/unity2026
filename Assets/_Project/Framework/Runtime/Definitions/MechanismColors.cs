using UnityEngine;

// 机制颜色映射：整数 1..N 对应一组颜色。
// 按钮 / 可解锁墙用整数表示颜色，渲染时按此取色。

namespace Sokoban3D.Framework
{
    public static class MechanismColors
    {
        static readonly Color[] Palette =
        {
            Color.magenta,                    // 0 占位（无效）
            new Color(0.90f, 0.25f, 0.25f),   // 1 红
            new Color(0.25f, 0.45f, 0.90f),   // 2 蓝
            new Color(0.95f, 0.80f, 0.20f),   // 3 黄
            new Color(0.25f, 0.75f, 0.35f),   // 4 绿
            new Color(0.70f, 0.35f, 0.85f),   // 5 紫
            new Color(0.95f, 0.55f, 0.20f),   // 6 橙
        };

        public static int Count { get { return Palette.Length - 1; } }

        public static Color Get(int x)
        {
            if (x >= 1 && x < Palette.Length) return Palette[x];
            return Color.magenta;
        }

        public static Color Get(string colorId)
        {
            int x;
            return int.TryParse(colorId, out x) ? Get(x) : Color.magenta;
        }
    }
}
