using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 扫描 Content/Levels 下的所有 json，按文件名排序，写进 LevelCatalog。

namespace Sokoban3D.Framework.EditorTools
{
    public static class LevelCatalogMenu
    {
        const string LevelsDir = "Assets/_Project/Content/Levels";
        const string CatalogPath = "Assets/_Project/Content/LevelCatalog.asset";

        [MenuItem("Sokoban3D/刷新关卡目录")]
        public static void RefreshCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var found = new List<TextAsset>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { LevelsDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".json")) continue;

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null) found.Add(asset);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            catalog.levels.Clear();
            catalog.levels.AddRange(found);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Sokoban3D] 关卡目录已刷新，共 " + catalog.levels.Count + " 关");
        }
    }
}
