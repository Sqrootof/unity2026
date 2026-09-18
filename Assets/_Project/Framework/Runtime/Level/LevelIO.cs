using System.IO;
using System.Text;
using UnityEngine;

// 关卡文件（JSON）读写。UTF-8 无 BOM，保证中文不乱码。

namespace Sokoban3D.Framework
{
    public static class LevelIO
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static LevelData FromJson(string json)
        {
            return JsonUtility.FromJson<LevelData>(json);
        }

        public static string ToJson(LevelData data, bool pretty = true)
        {
            return JsonUtility.ToJson(data, pretty);
        }

        public static LevelData Load(string path)
        {
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void Save(string path, LevelData data)
        {
            File.WriteAllText(path, ToJson(data), Utf8NoBom);
        }
    }
}
