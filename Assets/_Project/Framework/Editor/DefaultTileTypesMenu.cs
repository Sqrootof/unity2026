using System.IO;
using UnityEditor;
using UnityEngine;

// 一键生成第一章的默认类型定义，省得手动一个个建。

namespace Sokoban3D.Framework.EditorTools
{
    public static class DefaultTileTypesMenu
    {
        const string Dir = "Assets/_Project/Content/Definitions";

        [MenuItem("Sokoban3D/创建默认类型定义")]
        public static void CreateDefaults()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            Create("floor", "地板", "terrain", new Color(0.70f, 0.70f, 0.70f), 0.10f, false);
            Create("wall", "墙", "terrain", new Color(0.25f, 0.25f, 0.25f), 1.00f, true);
            Create("box", "箱子", "object", new Color(0.85f, 0.60f, 0.20f), 1.00f, true);
            Create("target", "抵达点", "marker", new Color(0.20f, 0.80f, 0.35f), 0.12f, false);
            Create("spawn", "出生点", "marker", new Color(0.25f, 0.55f, 0.90f), 0.12f, false);

            var regPath = Dir + "/TileRegistry.asset";
            var reg = AssetDatabase.LoadAssetAtPath<TileTypeRegistry>(regPath);
            if (reg == null)
            {
                reg = ScriptableObject.CreateInstance<TileTypeRegistry>();
                AssetDatabase.CreateAsset(reg, regPath);
            }

            reg.types.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:TileTypeDefinition", new[] { Dir }))
            {
                var d = AssetDatabase.LoadAssetAtPath<TileTypeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null) reg.types.Add(d);
            }

            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Sokoban3D] 默认类型定义已生成：" + Dir);
        }

        static void Create(string id, string display, string category, Color color, float height, bool blocking)
        {
            var path = Dir + "/TileType_" + id + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<TileTypeDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<TileTypeDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            def.id = id;
            def.displayName = display;
            def.category = category;
            def.color = color;
            def.placeholderHeight = height;
            def.blocking = blocking;
            EditorUtility.SetDirty(def);
        }
    }
}
