using UnityEngine;

// 简单 HUD：顶部显示关卡/步数/推数，通关时弹提示。用 OnGUI，无需搭 UI。
// 由 LevelManager 自动挂载。

namespace Sokoban3D.Framework
{
    public class LevelHud : MonoBehaviour
    {
        public LevelManager manager;

        GUIStyle _label;
        GUIStyle _center;
        GUIStyle _title;
        GUIStyle _box;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<LevelManager>();
        }

        void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 18;
            _label.normal.textColor = Color.white;

            _center = new GUIStyle(_label);
            _center.alignment = TextAnchor.MiddleCenter;

            _title = new GUIStyle(_label);
            _title.fontSize = 40;
            _title.alignment = TextAnchor.MiddleCenter;

            _box = new GUIStyle(GUI.skin.box);
        }

        void OnGUI()
        {
            if (manager == null) return;
            EnsureStyles();

            GUI.Box(new Rect(0f, 0f, Screen.width, 66f), "", _box);
            GUI.Label(new Rect(16f, 8f, 900f, 28f),
                "关卡 " + (manager.LevelIndex + 1) + "   " + manager.CurrentLevelName, _label);
            GUI.Label(new Rect(16f, 36f, 900f, 28f),
                "步数 " + manager.Steps + "     推数 " + manager.Pushes, _label);

            if (manager.IsSolved)
            {
                float w = 420f, h = 160f;
                var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                GUI.Box(r, "", _box);
                GUI.Label(new Rect(r.x, r.y + 26f, w, 56f), "通关！", _title);
                GUI.Label(new Rect(r.x, r.y + 92f, w, 30f),
                    manager.HasNextLevel ? "按 Enter 进入下一关" : "已是最后一关（按 R 重玩）", _center);
            }
        }
    }
}
