using System.Collections.Generic;
using UnityEngine;

// 所有可用元素的清单。工具照它出调色板，运行时照它找外观。

namespace Sokoban3D.Framework
{
    [CreateAssetMenu(menuName = "Sokoban3D/Tile Registry", fileName = "TileRegistry")]
    public class TileTypeRegistry : ScriptableObject
    {
        public List<TileTypeDefinition> types = new List<TileTypeDefinition>();

        public TileTypeDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] != null && types[i].id == id) return types[i];
            }
            return null;
        }
    }
}
