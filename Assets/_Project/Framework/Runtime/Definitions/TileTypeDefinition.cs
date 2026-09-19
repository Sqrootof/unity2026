using UnityEngine;

// 一种关卡元素的定义。工具和运行时都读它。
// terrain 类型现在代表"材质"（外观）；柱高由编辑器单独控制。

namespace Sokoban3D.Framework
{
    [CreateAssetMenu(menuName = "Sokoban3D/Tile Type", fileName = "TileType_")]
    public class TileTypeDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public int sortOrder = 0;

        // "terrain" | "mechanism"（机关）
        public string category = "terrain";

        // 机关的写入层："object" 可推动 / "marker" 静态标记
        public string storeKind = "marker";

        // 正式外观；留空则用占位方块
        public GameObject prefab;

        // 占位方块的颜色与高度（对象/标记用；1 = 整格）
        public Color color = Color.gray;
        public float placeholderHeight = 1f;

        // 地形材质（可选）：留空则用 color 上色；以后可指向自定义材质球
        public Material surfaceMaterial;

        // 兼容保留
        public bool blocking = true;
    }
}
