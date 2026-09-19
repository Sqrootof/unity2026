using System.IO;
using UnityEditor;
using UnityEngine;

// 一键生成默认定义：地形=材质（浅灰/深灰/黑），机关=箱子/抵达点/出生点。
// 会顺带清理旧的 floor/wall 定义。

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

            Create("gray_light", "浅灰", "terrain", new Color(0.70f, 0.70f, 0.70f), 1.00f, false, 0, "marker");
            Create("gray_dark", "深灰", "terrain", new Color(0.25f, 0.25f, 0.25f), 1.00f, false, 1, "marker");
            Create("black", "黑", "terrain", new Color(0.10f, 0.10f, 0.10f), 1.00f, false, 2, "marker");

            Create("box", "箱子(Box)", "mechanism", new Color(0.85f, 0.60f, 0.20f), 1.00f, true, 0, "object");
            Create("target", "抵达点(Target)", "mechanism", new Color(0.20f, 0.80f, 0.35f), 0.12f, false, 1, "marker");
            Create("spawn", "出生点(Spawn)", "mechanism", new Color(0.25f, 0.55f, 0.90f), 0.12f, false, 2, "marker");
            Create("button", "解锁按钮(Button)", "mechanism", new Color(0.90f, 0.35f, 0.35f), 0.20f, false, 3, "marker");
            Create("unlock_wall", "可解锁墙(LockWall)", "mechanism", new Color(0.90f, 0.55f, 0.20f), 1.00f, false, 4, "marker");

            // 清理旧的 floor/wall 定义
            DeleteIfExists("TileType_floor.asset");
            DeleteIfExists("TileType_wall.asset");

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
            Debug.Log("[Sokoban3D] 默认定义已生成：" + Dir);
        }

        static void Create(string id, string display, string category, Color color, float height, bool blocking, int sortOrder, string storeKind)
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
            def.sortOrder = sortOrder;
            def.storeKind = storeKind;
            EditorUtility.SetDirty(def);
        }

        static void DeleteIfExists(string fileName)
        {
            var path = Dir + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<TileTypeDefinition>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
