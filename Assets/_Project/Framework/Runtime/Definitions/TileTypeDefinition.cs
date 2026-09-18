using UnityEngine;

// 一种关卡元素（地板/墙/箱子/…)的定义。工具和运行时都读它。
// 加新元素 = 新建一个 .asset，不用改代码。

namespace Sokoban3D.Framework
{
    [CreateAssetMenu(menuName = "Sokoban3D/Tile Type", fileName = "TileType_")]
    public class TileTypeDefinition : ScriptableObject
    {
        public string id;
        public string displayName;

        // "terrain" | "object" | "marker" | "mechanism"
        public string category = "terrain";

        // 正式外观；留空则用占位方块
        public GameObject prefab;

        // 占位方块的颜色与高度（格子的比例，1 = 整格）
        public Color color = Color.gray;
        public float placeholderHeight = 1f;

        // 是否阻挡通行（地形用）
        public bool blocking = true;
    }
}
