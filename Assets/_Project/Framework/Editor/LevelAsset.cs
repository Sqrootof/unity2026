using UnityEngine;

// 编辑器里"正在编辑的关卡"的内存宿主。用 ScriptableObject 是为了让 Undo 能记录它。

namespace Sokoban3D.Framework.EditorTools
{
    public class LevelAsset : ScriptableObject
    {
        public LevelData data = new LevelData();
    }
}
