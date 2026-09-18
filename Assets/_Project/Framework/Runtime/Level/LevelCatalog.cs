using System.Collections.Generic;
using UnityEngine;

// 关卡目录：运行时要加载哪些关卡，按顺序列在这里。
// 由编辑器菜单"Sokoban3D/刷新关卡目录"自动扫描 Content/Levels 生成。

namespace Sokoban3D.Framework
{
    [CreateAssetMenu(menuName = "Sokoban3D/Level Catalog", fileName = "LevelCatalog")]
    public class LevelCatalog : ScriptableObject
    {
        public List<TextAsset> levels = new List<TextAsset>();

        public int Count { get { return levels.Count; } }

        public TextAsset Get(int index)
        {
            if (index < 0 || index >= levels.Count) return null;
            return levels[index];
        }
    }
}
