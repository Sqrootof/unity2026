using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 关卡编辑器 v1：调色板 + 网格绘制 + 放置/擦除 + JSON 存取。
// 打开：菜单 Sokoban3D / 关卡编辑器

namespace Sokoban3D.Framework.EditorTools
{
    public class LevelEditorWindow : EditorWindow
    {
        [SerializeField] TileTypeRegistry _registry;
        [SerializeField] float _cellSize = 1f;
        [SerializeField] string _category = "terrain";
        [SerializeField] ToolMode _mode = ToolMode.Paint;
        [SerializeField] int _blockHeight = 1;
        [SerializeField] string _blockMaterialId = "gray_light";
        [SerializeField] int _drawRadius = 48;
        [SerializeField] TerrainBrush _terrainBrush = TerrainBrush.Block;
        [SerializeField] float _lineWidth = 2f;
        [SerializeField] int _mechColor = 1;
        [SerializeField] int _mechHeight = 1;

        public enum TerrainBrush { Block, Base }

        public const int MaxSize = 100;

        // 静态会话：关窗重开后未保存内容仍可恢复
        static LevelAsset s_asset;
        static string s_path;
        static bool s_dirty;
        static bool s_reopen;
        static bool s_hasSaved;
        static string s_saved;
        static readonly List<LevelData> s_undo = new List<LevelData>();
        static readonly List<LevelData> s_redo = new List<LevelData>();

        bool CanUndo { get { return s_undo.Count > 0; } }
        bool CanRedo { get { return s_redo.Count > 0; } }

        LevelAsset _asset { get { return s_asset; } set { s_asset = value; } }
        string _path { get { return s_path; } set { s_path = value; } }
        bool _dirty { get { return s_dirty; } set { s_dirty = value; } }

        readonly Dictionary<long, TerrainEntry> _cellIndex = new Dictionary<long, TerrainEntry>();
        readonly List<LevelIssue> _issues = new List<LevelIssue>();
        bool _solveTested;
        string _solveText;
        Vector2 _levelScroll;
        Vector2 _scroll;
        Vector2 _issueScroll;
        GUIStyle _errStyle, _warnStyle, _infoStyle;

        public enum ToolMode { Paint, Erase, Rect, Pick }

        TileTypeDefinition _brush;
        Int3 _rectStart;
        Int3 _rectEnd;
        bool _rectActive;

        [MenuItem("Sokoban3D/关卡编辑器")]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>("关卡编辑器");
            w.minSize = new Vector2(200f, 200f);
            w.Show();
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        void OnDestroy()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!_dirty) return;

            int r = EditorUtility.DisplayDialogComplex("未保存的改动",
                "关卡有未保存的改动，是否保存？", "保存", "不保存", "取消");
            if (r == 0) SaveLevel();
            else if (r == 2)
            {
                s_reopen = true;
                EditorApplication.delayCall += ReopenIfNeeded;
            }
        }

        static void ReopenIfNeeded()
        {
            if (!s_reopen) return;
            s_reopen = false;
            GetWindow<LevelEditorWindow>("关卡编辑器").Show();
        }

        void OnGUI()
        {
            HandleSaveShortcut();
            EnsureRegistry();
            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, false, false);

            DrawRegistryField();
            DrawViewControls();

            if (_asset == null)
            {
                DrawLevelList();
                EditorGUILayout.HelpBox("从上面的列表点选一个关卡，或点「新建」。\n场景视图：左键按当前模式操作（放置/擦除），右键留给 Unity 转视角。", MessageType.Info);
            }
            else
            {
                DrawLevelInfo();
                DrawPalette();
                DrawIssues();
            }

            EditorGUILayout.EndScrollView();
        }

        static readonly string LevelsDir = "Assets/_Project/Content/Levels";

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("文件 ▾", EditorStyles.toolbarButton, GUILayout.Width(60f))) ShowFileMenu();
            if (GUILayout.Button("编辑 ▾", EditorStyles.toolbarButton, GUILayout.Width(60f))) ShowEditMenu();

            GUILayout.FlexibleSpace();
            if (_asset == null)
            {
                EditorGUILayout.LabelField("(未选取关卡)", EditorStyles.miniLabel);
            }
            else
            {
                string name = string.IsNullOrEmpty(_path) ? "(未保存)" : System.IO.Path.GetFileNameWithoutExtension(_path);
                EditorGUILayout.LabelField(name + (_dirty ? " *" : ""), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        void ShowFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新建..."), false, NewLevelDialog);

            var paths = LevelPaths();
            if (paths.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("打开/(没有关卡)"));
            }
            else
            {
                foreach (var p in paths)
                {
                    string path = p;
                    string label = System.IO.Path.GetFileNameWithoutExtension(p);
                    menu.AddItem(new GUIContent("打开/" + label), _path == path, () =>
                    {
                        if (ConfirmDiscard()) OpenLevelPath(path);
                    });
                }
            }

            if (_asset == null)
            {
                menu.AddDisabledItem(new GUIContent("保存"));
                menu.AddDisabledItem(new GUIContent("另存..."));
            }
            else
            {
                menu.AddItem(new GUIContent("保存"), false, () => SaveLevel());
                menu.AddItem(new GUIContent("另存..."), false, () => SaveLevelAs());
            }

            menu.ShowAsContext();
        }

        void ShowEditMenu()
        {
            var menu = new GenericMenu();

            if (_asset == null || !CanUndo)
            {
                menu.AddDisabledItem(new GUIContent("撤销"));
            }
            else
            {
                menu.AddItem(new GUIContent("撤销"), false, DoUndo);
            }

            if (_asset == null || !CanRedo)
            {
                menu.AddDisabledItem(new GUIContent("重做"));
            }
            else
            {
                menu.AddItem(new GUIContent("重做"), false, DoRedo);
            }
            menu.AddSeparator("");

            if (_asset == null)
            {
                menu.AddDisabledItem(new GUIContent("校验"));
                menu.AddDisabledItem(new GUIContent("裁切外围虚空格"));
            }
            else
            {
                menu.AddItem(new GUIContent("校验"), false, () =>
                {
                    RefreshIssues();
                    Debug.Log("[关卡编辑器] 校验：共 " + _issues.Count + " 条");
                });
                menu.AddItem(new GUIContent("裁切外围虚空格"), false, TrimOuterVoid);
            }

            menu.ShowAsContext();
        }

        void DoUndo()
        {
            if (_asset == null || s_undo.Count == 0) return;

            s_redo.Add(Clone(_asset.data));
            _asset.data = s_undo[s_undo.Count - 1];
            s_undo.RemoveAt(s_undo.Count - 1);

            UpdateDirty();
            _solveTested = false;
            SceneView.RepaintAll();
        }

        void DoRedo()
        {
            if (_asset == null || s_redo.Count == 0) return;

            s_undo.Add(Clone(_asset.data));
            _asset.data = s_redo[s_redo.Count - 1];
            s_redo.RemoveAt(s_redo.Count - 1);

            UpdateDirty();
            _solveTested = false;
            SceneView.RepaintAll();
        }

        // 修改关卡数据统一走这里：无实际变化则不记录撤销、不置脏
        void Edit(System.Action mutate)
        {
            if (_asset == null) return;

            var before = Clone(_asset.data);
            mutate();

            string afterJson = LevelIO.ToJson(_asset.data, false);
            if (afterJson == LevelIO.ToJson(before, false)) return;

            s_undo.Add(before);
            s_redo.Clear();
            UpdateDirty(afterJson);
            _solveTested = false;
            SceneView.RepaintAll();
        }

        void UpdateDirty()
        {
            if (_asset == null) { _dirty = false; return; }
            UpdateDirty(LevelIO.ToJson(_asset.data, false));
        }

        void UpdateDirty(string currentJson)
        {
            _dirty = !s_hasSaved || currentJson != s_saved;
        }

        static LevelData Clone(LevelData d)
        {
            return LevelIO.FromJson(LevelIO.ToJson(d, false));
        }

        void TrimOuterVoid()
        {
            if (_asset == null) return;

            bool changed = false;
            Edit(() => { changed = LevelOps.TrimOuterVoid(_asset.data); });

            Debug.Log(changed
                ? "[关卡编辑器] 已裁切外围虚空，尺寸变为 " + _asset.data.size.x + " x " + _asset.data.size.z
                : "[关卡编辑器] 没有可裁切的外围虚空");
        }

        List<string> LevelPaths()
        {
            var list = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:TextAsset", new[] { LevelsDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".json")) list.Add(p);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }

        void DrawLevelList()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("关卡列表（" + LevelsDir + "）", EditorStyles.miniLabel);

            var paths = LevelPaths();
            float h = Mathf.Min(132f, 24f * Mathf.Max(1, paths.Count) + 6f);
            _levelScroll = EditorGUILayout.BeginScrollView(_levelScroll, GUILayout.Height(h));

            foreach (var p in paths)
            {
                bool current = _path == p;
                string label = System.IO.Path.GetFileNameWithoutExtension(p);
                if (GUILayout.Toggle(current, current ? label + "  ●" : label, "Button"))
                {
                    if (!current && ConfirmDiscard()) OpenLevelPath(p);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        bool ConfirmDiscard()
        {
            if (!_dirty) return true;

            int r = EditorUtility.DisplayDialogComplex("未保存的改动",
                "当前关卡有未保存的改动，是否保存？", "保存", "不保存", "取消");
            if (r == 0) return SaveLevel();
            if (r == 1) return true;
            return false;
        }

        // ---------- 校验 ----------

        void RefreshIssues()
        {
            _issues.Clear();
            if (_asset != null) _issues.AddRange(LevelValidator.Validate(_asset.data));
        }

        int CountErrors()
        {
            RefreshIssues();
            int n = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].type == LevelIssueType.Error) n++;
            }
            return n;
        }

        GUIStyle IssueStyle(LevelIssueType t)
        {
            if (_errStyle == null)
            {
                _errStyle = new GUIStyle(EditorStyles.label);
                _errStyle.normal.textColor = new Color(1f, 0.42f, 0.42f);
                _warnStyle = new GUIStyle(EditorStyles.label);
                _warnStyle.normal.textColor = new Color(1f, 0.80f, 0.30f);
                _infoStyle = new GUIStyle(EditorStyles.label);
                _infoStyle.normal.textColor = new Color(0.70f, 0.80f, 1f);
            }
            if (t == LevelIssueType.Error) return _errStyle;
            if (t == LevelIssueType.Warning) return _warnStyle;
            return _infoStyle;
        }

        void DrawIssues()
        {
            RefreshIssues();

            int errs = 0, warns = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].type == LevelIssueType.Error) errs++;
                else if (_issues[i].type == LevelIssueType.Warning) warns++;
            }

            EditorGUILayout.Space();
            string summary = (errs == 0 && warns == 0) ? "通过 ✓" : ("错误 " + errs + " / 警告 " + warns);
            EditorGUILayout.LabelField("校验：" + summary, EditorStyles.boldLabel);

            if (_issues.Count > 0)
            {
                float h = Mathf.Min(140f, 20f * _issues.Count + 6f);
                _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.Height(h));

                for (int i = 0; i < _issues.Count; i++)
                {
                    var it = _issues[i];
                    var style = IssueStyle(it.type);
                    string text = (it.type == LevelIssueType.Error ? "✗ " : it.type == LevelIssueType.Warning ? "! " : "i ") + it.message;

                    if (it.hasCell)
                    {
                        if (GUILayout.Button(text + "   " + it.cell, style)) FrameCell(it.cell);
                    }
                    else
                    {
                        GUILayout.Label(text, style);
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            if (errs == 0)
            {
                EditorGUILayout.Space();
                if (GUILayout.Button("检测是否有解")) RunSolver();
                if (_solveTested) EditorGUILayout.LabelField(_solveText);
            }
        }

        void RunSolver()
        {
            var r = LevelSolver.Solve(_asset.data, 300000, out int steps, out int pushes);
            _solveTested = true;

            if (r == SolveResult.Solved) _solveText = "有解：最少 " + steps + " 步 / " + pushes + " 推";
            else if (r == SolveResult.Unsolvable) _solveText = "无解（已穷尽搜索）";
            else _solveText = "未知（搜索量超限，可能仍可解）";
        }

        void FrameCell(Int3 cell)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            Vector3 p = new Vector3(cell.x * _cellSize, cell.y * _cellSize, cell.z * _cellSize);
            sv.LookAt(p, sv.rotation, 6f);
            sv.Repaint();
        }

        void DrawIssueMarkers(float cs)
        {
            for (int i = 0; i < _issues.Count; i++)
            {
                var it = _issues[i];
                if (!it.hasCell) continue;
                Handles.color = it.type == LevelIssueType.Error
                    ? new Color(1f, 0.20f, 0.20f, 1f)
                    : new Color(1f, 0.80f, 0.20f, 1f);
                WireBox(new Vector3(it.cell.x * cs, it.cell.y * cs + cs * 0.5f, it.cell.z * cs),
                        new Vector3(cs * 1.02f, cs * 1.02f, cs * 1.02f));
            }
        }

        void DrawRegistryField()
        {
            EditorGUILayout.Space();
            _registry = (TileTypeRegistry)EditorGUILayout.ObjectField("类型注册表", _registry, typeof(TileTypeRegistry), false);
        }

        // 没指定注册表时自动找项目里唯一的 TileTypeRegistry
        void EnsureRegistry()
        {
            if (_registry != null) return;
            var guids = AssetDatabase.FindAssets("t:TileTypeRegistry");
            if (guids.Length == 0) return;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _registry = AssetDatabase.LoadAssetAtPath<TileTypeRegistry>(path);
        }

        void PlayView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            sv.orthographic = false;
            sv.LookAt(LevelCenter(), Quaternion.Euler(55f, 0f, 0f), 4f);
            sv.Repaint();
        }

        void TopView()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            sv.orthographic = true;
            sv.LookAt(LevelCenter(), Quaternion.Euler(90f, 0f, 0f), 5f);
            sv.Repaint();
        }

        void DrawViewControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("游玩视角")) PlayView();
            if (GUILayout.Button("俯视视角")) TopView();
            EditorGUILayout.EndHorizontal();
        }

        Vector3 LevelCenter()
        {
            if (_asset == null) return Vector3.zero;
            float cx = (_asset.data.size.x - 1) * 0.5f * _cellSize;
            float cz = (_asset.data.size.z - 1) * 0.5f * _cellSize;
            return new Vector3(cx, 0f, cz);
        }

        void DrawToolModes()
        {
            EditorGUILayout.Space();
            _mode = (ToolMode)GUILayout.Toolbar((int)_mode, new[] { "放置", "擦除", "矩形", "吸管" });
        }

        void DrawLevelInfo()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("关卡 ID", _asset.data.id);

            int sx = Mathf.Clamp(EditorGUILayout.DelayedIntField("长 (X)", _asset.data.size.x), 1, MaxSize);
            int sz = Mathf.Clamp(EditorGUILayout.DelayedIntField("宽 (Z)", _asset.data.size.z), 1, MaxSize);
            if (sx != _asset.data.size.x || sz != _asset.data.size.z)
            {
                Edit(() =>
                {
                    _asset.data.size = new Int3(sx, _asset.data.size.y, sz);
                    LevelOps.CropOutOfBounds(_asset.data);
                });
            }

            if (sx * sz > 10000)
            {
                EditorGUILayout.HelpBox("尺寸较大：编辑器只绘制视点附近的格子（半径 " + _drawRadius + "）。", MessageType.Info);
            }

            EditorGUILayout.LabelField("地形 / 机关",
                _asset.data.terrain.Count + " / " + (_asset.data.objects.Count + _asset.data.markers.Count));

            int bh = Mathf.Clamp(EditorGUILayout.DelayedIntField("底面高度", _asset.data.baseHeight), 0, 100);
            if (bh != _asset.data.baseHeight)
            {
                Edit(() =>
                {
                    _asset.data.baseHeight = bh;
                    if (bh == 0) DropUnsupportedMechanisms();
                    ResyncHeights(_asset.data);
                });
            }

            if (_registry != null)
            {
                var ids = new List<string>();
                var names = new List<string>();
                foreach (var t in _registry.types)
                {
                    if (t != null && t.category == "terrain") { ids.Add(t.id); names.Add(t.displayName); }
                }
                if (ids.Count > 0)
                {
                    int idx = Mathf.Max(0, ids.IndexOf(_asset.data.baseMaterial));
                    int newIdx = EditorGUILayout.Popup("底面材质", idx, names.ToArray());
                    if (newIdx != idx)
                    {
                        string mid = ids[newIdx];
                        Edit(() => { _asset.data.baseMaterial = mid; });
                    }
                }
            }
        }

        // 底面高度变 0（无底面）时，失去支撑的机关一并删除
        void DropUnsupportedMechanisms()
        {
            var data = _asset.data;
            for (int i = data.objects.Count - 1; i >= 0; i--)
            {
                if (WalkLevelAt(data, data.objects[i].pos.x, data.objects[i].pos.z) <= 0) data.objects.RemoveAt(i);
            }
            for (int i = data.markers.Count - 1; i >= 0; i--)
            {
                if (WalkLevelAt(data, data.markers[i].pos.x, data.markers[i].pos.z) <= 0) data.markers.RemoveAt(i);
            }
        }

        // 固定顺序：出生点 / 箱子 / 抵达点 / 可解锁墙 / 解锁按钮
        static int MechanismOrder(string id)
        {
            switch (id)
            {
                case "spawn": return 0;
                case "box": return 1;
                case "target": return 2;
                case "unlock_wall": return 3;
                case "button": return 4;
                default: return 100;
            }
        }

        List<TileTypeDefinition> SortedMechanismTypes()
        {
            var list = new List<TileTypeDefinition>();
            foreach (var t in _registry.types)
            {
                if (t != null && t.category == "mechanism") list.Add(t);
            }
            list.Sort((a, b) =>
            {
                int pa = MechanismOrder(a.id);
                int pb = MechanismOrder(b.id);
                if (pa != pb) return pa.CompareTo(pb);
                return a.sortOrder.CompareTo(b.sortOrder);
            });
            return list;
        }

        List<TileTypeDefinition> SortedTerrainTypes()
        {
            var list = new List<TileTypeDefinition>();
            foreach (var t in _registry.types)
            {
                if (t != null && t.category == "terrain") list.Add(t);
            }
            list.Sort((a, b) =>
            {
                int pa = TerrainOrder(a.id);
                int pb = TerrainOrder(b.id);
                if (pa != pb) return pa.CompareTo(pb);
                return a.sortOrder.CompareTo(b.sortOrder);
            });
            return list;
        }

        // 固定顺序：浅灰、深灰、黑；其余排后面
        static int TerrainOrder(string id)
        {
            switch (id)
            {
                case "gray_light": return 0;
                case "gray_dark": return 1;
                case "black": return 2;
                default: return 100;
            }
        }

        static string CategoryLabel(string id)
        {
            switch (id)
            {
                case "terrain": return "地形(Terrain)";
                case "mechanism": return "机关(Mechanism)";
                case "object": return "物件(Object)";
                case "marker": return "标记(Marker)";
                default: return id;
            }
        }

        void DrawPalette()
        {
            if (_registry == null || _registry.types.Count == 0)
            {
                EditorGUILayout.HelpBox("请指定类型注册表（TileRegistry）", MessageType.Warning);
                return;
            }

            DrawToolModes();

            var cats = new List<string>();
            foreach (var t in _registry.types)
            {
                if (t != null && !cats.Contains(t.category)) cats.Add(t.category);
            }
            if (cats.Count == 0) return;

            var labels = new List<string>();
            foreach (var c in cats) labels.Add(CategoryLabel(c));

            int ci = Mathf.Max(0, cats.IndexOf(_category));
            ci = GUILayout.Toolbar(ci, labels.ToArray());
            _category = cats[ci];

            EditorGUILayout.Space();

            if (_category == "terrain")
            {
                _terrainBrush = (TerrainBrush)GUILayout.Toolbar((int)_terrainBrush, new[] { "块(Block)", "底面(Base)" });

                if (_terrainBrush == TerrainBrush.Block)
                {
                    EditorGUILayout.LabelField("柱高", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    foreach (int h in new[] { 1, 2, 3 })
                    {
                        if (GUILayout.Toggle(_blockHeight == h, h.ToString(), "Button", GUILayout.Width(36f)))
                        {
                            _blockHeight = h;
                        }
                    }
                    _blockHeight = Mathf.Max(1, EditorGUILayout.IntField(_blockHeight, GUILayout.Width(46f)));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField("材质", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    var mats = SortedTerrainTypes();
                    for (int i = 0; i < mats.Count; i++)
                    {
                        var t = mats[i];
                        var rect = GUILayoutUtility.GetRect(44f, 44f, GUILayout.Width(44f), GUILayout.Height(44f));
                        if (GUI.Toggle(rect, _blockMaterialId == t.id, new GUIContent("", t.displayName), "Button"))
                        {
                            _blockMaterialId = t.id;
                        }
                        EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f), t.color);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                foreach (var t in SortedMechanismTypes())
                {
                    if (GUILayout.Toggle(_brush == t, "  " + t.displayName, "Button"))
                    {
                        _brush = t;
                    }
                }

                if (_brush != null && _brush.id == "unlock_wall")
                {
                    _mechHeight = Mathf.Clamp(EditorGUILayout.DelayedIntField("墙高度", _mechHeight), 1, 20);
                }

                if (_brush != null && (_brush.id == "button" || _brush.id == "unlock_wall"))
                {
                    var names = new string[MechanismColors.Count];
                    for (int k = 1; k <= MechanismColors.Count; k++) names[k - 1] = "颜色" + k;
                    _mechColor = Mathf.Clamp(EditorGUILayout.Popup("颜色", _mechColor - 1, names) + 1, 1, MechanismColors.Count);
                }
            }
        }

        // ---------- 场景交互 ----------

        void OnSceneGui(SceneView sv)
        {
            if (_asset == null) return;

            HandleSaveShortcut();

            sv.wantsMouseMove = true;

            var data = _asset.data;
            float cs = _cellSize;

            DrawGrid(data, cs);
            DrawBase(data, cs, sv.pivot);
            DrawPlaced(data, cs, sv.pivot);
            RefreshIssues();
            DrawIssueMarkers(cs);
            HandleMouse(sv, data, cs);

            if (Event.current.type == EventType.MouseMove) sv.Repaint();
        }

        void DrawGrid(LevelData data, float cs)
        {
            float gy = Mathf.Max(1, data.baseHeight) * cs; // 底面（柱高 1 或隐含底面）
            float x0 = -0.5f * cs;
            float z0 = -0.5f * cs;
            float x1 = (data.size.x - 0.5f) * cs;
            float z1 = (data.size.z - 0.5f) * cs;

            Handles.color = new Color(1f, 1f, 1f, 0.18f);
            for (int x = 0; x <= data.size.x; x++)
            {
                float lx = (x - 0.5f) * cs;
                Handles.DrawLine(new Vector3(lx, gy, z0), new Vector3(lx, gy, z1));
            }
            for (int z = 0; z <= data.size.z; z++)
            {
                float lz = (z - 0.5f) * cs;
                Handles.DrawLine(new Vector3(x0, gy, lz), new Vector3(x1, gy, lz));
            }
        }

        // 隐含底面：视点附近逐格绘制，和显式块用同样的线框风格；被挖空的格子画红框
        void DrawBase(LevelData data, float cs, Vector3 pivot)
        {
            bool hasBase = data.baseHeight > 0;
            float total = data.baseHeight * cs;
            Color c = ColorOf(data.baseMaterial);
            c.a = 1f;

            _cellIndex.Clear();
            for (int i = 0; i < data.terrain.Count; i++)
            {
                var t = data.terrain[i];
                _cellIndex[Key(t.pos.x, t.pos.z)] = t;
            }

            int r = Mathf.Max(2, _drawRadius);
            int cx = Mathf.RoundToInt(pivot.x / cs);
            int cz = Mathf.RoundToInt(pivot.z / cs);

            Handles.color = c;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= data.size.x) continue;
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (z < 0 || z >= data.size.z) continue;

                    TerrainEntry e;
                    if (_cellIndex.TryGetValue(Key(x, z), out e))
                    {
                        if (e.type == "void") DrawVoidTile(x, z, cs); // 真虚空：红叉
                        continue; // 显式地形由 DrawPlaced 画
                    }

                    Handles.color = c;
                    if (hasBase)
                    {
                        WireBox(new Vector3(x * cs, total * 0.5f, z * cs),
                                new Vector3(cs * 0.9f, total, cs * 0.9f));
                        BaseCross(x, z, cs);
                    }
                    else
                    {
                        // 假虚空：底面高度=0 的裸格，本质仍是底面 → 底面颜色的叉
                        BaseCross(x, z, cs);
                    }
                }
            }
        }

        // 底面格：只在底部画一个叉，和显式块区分
        void BaseCross(int x, int z, float cs)
        {
            float x0 = (x - 0.45f) * cs, x1 = (x + 0.45f) * cs;
            float z0 = (z - 0.45f) * cs, z1 = (z + 0.45f) * cs;
            Handles.DrawAAPolyLine(_lineWidth, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z1));
            Handles.DrawAAPolyLine(_lineWidth, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z0));
        }

        // 虚空：0 高度处的红色方框 + 叉
        void DrawVoidTile(int x, int z, float cs)
        {
            Handles.color = new Color(1f, 0.25f, 0.25f, 1f);
            float x0 = (x - 0.5f) * cs, x1 = (x + 0.5f) * cs;
            float z0 = (z - 0.5f) * cs, z1 = (z + 0.5f) * cs;
            Handles.DrawAAPolyLine(_lineWidth,
                new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1),
                new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0));
            Handles.DrawAAPolyLine(_lineWidth, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z1));
            Handles.DrawAAPolyLine(_lineWidth, new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z0));
        }

        static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        void DrawPlaced(LevelData data, float cs, Vector3 pivot)
        {
            float r = Mathf.Max(4, _drawRadius) * cs;
            float r2 = r * r;

            foreach (var t in data.terrain)
            {
                if (t.type == "void") continue;
                if (Near(t.pos, pivot, cs, r2)) DrawColumn(t.pos, ColorOf(t.type), cs);
            }
            foreach (var o in data.objects)
            {
                if (!Near(o.pos, pivot, cs, r2)) continue;
                int y = WalkLevelAt(data, o.pos.x, o.pos.z);
                DrawCell(new Int3(o.pos.x, y, o.pos.z), ColorOf(o.type), 1f, cs);
            }

            var prevZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var m in data.markers)
            {
                if (!Near(m.pos, pivot, cs, r2)) continue;
                int y = WalkLevelAt(data, m.pos.x, m.pos.z);

                if (m.type == "button")
                {
                    float rad = 0.175f * cs;
                    WireSphere(new Vector3(m.pos.x * cs, y * cs + rad, m.pos.z * cs), rad, MechanismColors.Get(IntColor(m.color)));
                }
                else if (m.type == "unlock_wall")
                {
                    WireCylinder(new Vector3(m.pos.x * cs, y * cs, m.pos.z * cs), 0.45f * cs, Mathf.Max(1, m.height) * cs, MechanismColors.Get(IntColor(m.color)));
                }
                else
                {
                    DrawMarkerPyramid(new Int3(m.pos.x, y, m.pos.z), ColorOf(m.type), cs);
                }
            }
            Handles.zTest = prevZ;
        }

        static bool Near(Int3 p, Vector3 pivot, float cs, float r2)
        {
            float dx = p.x * cs - pivot.x;
            float dz = p.z * cs - pivot.z;
            return dx * dx + dz * dz <= r2;
        }

        static int IntColor(string s)
        {
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }

        void WireSphere(Vector3 c, float r, Color color)
        {
            Handles.color = color;
            Handles.DrawWireDisc(c, Vector3.up, r);
            Handles.DrawWireDisc(c, Vector3.right, r);
            Handles.DrawWireDisc(c, Vector3.forward, r);
        }

        void WireCylinder(Vector3 baseCenter, float radius, float height, Color color)
        {
            Handles.color = color;
            Vector3 top = baseCenter + Vector3.up * height;
            Handles.DrawWireDisc(baseCenter, Vector3.up, radius);
            Handles.DrawWireDisc(top, Vector3.up, radius);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Vector3 off = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Handles.DrawAAPolyLine(_lineWidth, baseCenter + off, top + off);
            }
        }

        // 倒置四棱锥：底在上、尖朝下，像插在地板上的标记
        void DrawMarkerPyramid(Int3 cell, Color color, float cs)
        {
            Vector3 c = new Vector3(cell.x * cs, cell.y * cs, cell.z * cs);
            float half = cs * 0.28f;
            Vector3 apex = c + new Vector3(0f, cs * 0.15f, 0f);
            Vector3 b0 = c + new Vector3(half, cs * 0.95f, half);
            Vector3 b1 = c + new Vector3(-half, cs * 0.95f, half);
            Vector3 b2 = c + new Vector3(-half, cs * 0.95f, -half);
            Vector3 b3 = c + new Vector3(half, cs * 0.95f, -half);

            Handles.color = color;
            Handles.DrawLine(apex, b0);
            Handles.DrawLine(apex, b1);
            Handles.DrawLine(apex, b2);
            Handles.DrawLine(apex, b3);
            Handles.DrawLine(b0, b1);
            Handles.DrawLine(b1, b2);
            Handles.DrawLine(b2, b3);
            Handles.DrawLine(b3, b0);
        }

        // 加粗线框盒（Handles.DrawWireCube 线宽不可控，改用 AA 折线）
        void WireBox(Vector3 center, Vector3 size)
        {
            Vector3 e = size * 0.5f;
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
            }
            int[,] edges =
            {
                {0,1},{2,3},{4,5},{6,7},
                {0,2},{1,3},{4,6},{5,7},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int i = 0; i < 12; i++)
            {
                Handles.DrawAAPolyLine(_lineWidth, c[edges[i, 0]], c[edges[i, 1]]);
            }
        }

        void DrawCell(Int3 cell, Color color, float h, float cs)
        {
            float total = cs * h;
            Handles.color = color;
            WireBox(new Vector3(cell.x * cs, cell.y * cs + total * 0.5f, cell.z * cs),
                    new Vector3(cs * 0.8f, total, cs * 0.8f));
        }

        void DrawColumn(Int3 pos, Color color, float cs)
        {
            float total = (pos.y + 1) * cs;
            Handles.color = color;
            WireBox(new Vector3(pos.x * cs, total * 0.5f, pos.z * cs),
                    new Vector3(cs * 0.9f, total, cs * 0.9f));
        }

        Color ColorOf(string typeId)
        {
            var def = _registry != null ? _registry.Get(typeId) : null;
            return def != null ? def.color : Color.magenta;
        }

        readonly Dictionary<string, Texture2D> _swatches = new Dictionary<string, Texture2D>();

        Texture2D Swatch(Color c)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c);
            Texture2D tex;
            if (_swatches.TryGetValue(key, out tex)) return tex;

            tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _swatches[key] = tex;
            return tex;
        }

        float HeightOf(string typeId)
        {
            var def = _registry != null ? _registry.Get(typeId) : null;
            return def != null ? Mathf.Max(0.1f, def.placeholderHeight) : 1f;
        }

        void HandleMouse(SceneView sv, LevelData data, float cs)
        {
            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            float planeY = Mathf.Max(1, data.baseHeight) * cs;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

            float dist;
            if (!plane.Raycast(ray, out dist)) return;

            Vector3 p = ray.GetPoint(dist);
            var cell = new Int3(Mathf.RoundToInt(p.x / cs), 0, Mathf.RoundToInt(p.z / cs));

            if (cell.x < 0 || cell.x >= data.size.x || cell.z < 0 || cell.z >= data.size.z) return;

            Handles.color = _mode == ToolMode.Erase
                ? new Color(1f, 0.55f, 0.10f, 0.95f)
                : new Color(1f, 0.90f, 0.20f, 0.95f);

            if (_category == "terrain")
            {
                float th = Mathf.Max(1, _blockHeight) * cs;
                WireBox(new Vector3(cell.x * cs, th * 0.5f, cell.z * cs), new Vector3(cs, th, cs));
            }
            else
            {
                float h = _brush != null ? HeightOf(_brush.id) : 0.24f;
                float bh = cs * Mathf.Max(0.2f, h);
                WireBox(new Vector3(cell.x * cs, planeY + bh * 0.5f, cell.z * cs), new Vector3(cs, bh, cs));
            }

            if (_mode == ToolMode.Rect && _rectActive)
            {
                Handles.color = new Color(0.4f, 1f, 0.5f, 0.95f);
                int rx0 = Mathf.Min(_rectStart.x, _rectEnd.x), rx1 = Mathf.Max(_rectStart.x, _rectEnd.x);
                int rz0 = Mathf.Min(_rectStart.z, _rectEnd.z), rz1 = Mathf.Max(_rectStart.z, _rectEnd.z);
                WireBox(new Vector3((rx0 + rx1) * 0.5f * cs, planeY * 0.5f + cs * 0.5f, (rz0 + rz1) * 0.5f * cs),
                        new Vector3((rx1 - rx0 + 1) * cs, cs, (rz1 - rz0 + 1) * cs));
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                switch (_mode)
                {
                    case ToolMode.Paint: Place(cell); break;
                    case ToolMode.Erase: Erase(cell); break;
                    case ToolMode.Rect: _rectStart = cell; _rectEnd = cell; _rectActive = true; break;
                    case ToolMode.Pick: Pick(cell); break;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _mode == ToolMode.Rect && _rectActive)
            {
                _rectEnd = cell;
                sv.Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _mode == ToolMode.Rect && _rectActive)
            {
                _rectActive = false;
                FillRect(_rectStart, _rectEnd);
                e.Use();
            }
        }

        // ---------- 编辑数据 ----------

        void Place(Int3 cell)
        {
            if (_asset == null) return;
            var data = _asset.data;
            int x = cell.x;
            int z = cell.z;

            // 单格的无变化检查（矩形填充不逐个检查，交给 Edit 的统一比较）
            if (_category == "terrain")
            {
                var existing = TerrainAtXZ(data, x, z);
                if (_terrainBrush == TerrainBrush.Base)
                {
                    if (existing == null) return;
                }
                else
                {
                    int colH = Mathf.Max(1, _blockHeight);
                    if (existing != null && existing.type == _blockMaterialId && existing.pos.y == colH - 1) return;
                }
            }
            else
            {
                if (_brush == null) return;
                var eo0 = ObjectAtXZ(data, x, z);
                var em0 = MarkerAtXZ(data, x, z);
                if (_brush.storeKind == "object" && em0 == null && eo0 != null && eo0.type == _brush.id) return;

                string wantColor = (_brush.id == "button" || _brush.id == "unlock_wall") ? _mechColor.ToString() : "";
                int wantHeight = _brush.id == "unlock_wall" ? Mathf.Max(1, _mechHeight) : 1;
                if (_brush.storeKind == "marker" && eo0 == null && em0 != null
                    && em0.type == _brush.id && em0.color == wantColor && em0.height == wantHeight) return;
            }

            Edit(() => ApplyBrush(data, x, z));
        }

        // 在单格应用当前画笔（不做撤销/脏处理）
        void ApplyBrush(LevelData data, int x, int z)
        {
            if (_category == "terrain")
            {
                RemoveTerrainAtXZ(data, x, z);
                if (_terrainBrush == TerrainBrush.Block)
                {
                    int colH = Mathf.Max(1, _blockHeight);
                    data.terrain.Add(new TerrainEntry { type = _blockMaterialId, pos = new Int3(x, colH - 1, z) });
                }
                ResyncHeights(data);
                return;
            }

            if (_brush == null) return;
            int y = WalkLevelAt(data, x, z);
            if (y <= 0) return;

            RemoveObjectsAtXZ(data, x, z);
            RemoveMarkersAtXZ(data, x, z);

            if (_brush.storeKind == "object")
            {
                data.objects.Add(new ObjectEntry { type = _brush.id, pos = new Int3(x, y, z) });
            }
            else
            {
                string color = (_brush.id == "button" || _brush.id == "unlock_wall") ? _mechColor.ToString() : "";
                int height = _brush.id == "unlock_wall" ? Mathf.Max(1, _mechHeight) : 1;
                data.markers.Add(new MarkerEntry
                {
                    type = _brush.id,
                    pos = new Int3(x, y, z),
                    player = _brush.id == "spawn" ? 0 : -1,
                    color = color,
                    height = height
                });
            }
        }

        // 矩形填充（一次编辑、一次撤销）
        void FillRect(Int3 a, Int3 b)
        {
            if (_asset == null) return;
            var data = _asset.data;
            int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
            int z0 = Mathf.Min(a.z, b.z), z1 = Mathf.Max(a.z, b.z);

            Edit(() =>
            {
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++) ApplyBrush(data, x, z);
                }
            });
        }

        // 吸管：同时取该格的机关和地形；非虚空跳到放置，虚空跳到擦除
        void Pick(Int3 cell)
        {
            if (_asset == null) return;
            var data = _asset.data;
            int x = cell.x, z = cell.z;

            // 地形数据（总是取，之后手动切到地形分类也是这格的数据）
            var et = TerrainAtXZ(data, x, z);
            bool explicitVoid = et != null && et.type == "void";
            if (et == null || explicitVoid)
            {
                _terrainBrush = TerrainBrush.Base;
            }
            else
            {
                _terrainBrush = TerrainBrush.Block;
                _blockMaterialId = et.type;
                _blockHeight = et.pos.y + 1;
            }

            // 机关数据（有就取）
            var eo = ObjectAtXZ(data, x, z);
            var em = MarkerAtXZ(data, x, z);
            TileTypeDefinition mechDef = null;
            if (eo != null || em != null)
            {
                mechDef = _registry != null ? _registry.Get(eo != null ? eo.type : em.type) : null;
                if (mechDef != null) _brush = mechDef;

                // 一并取颜色 / 高度
                if (em != null)
                {
                    int col;
                    if (int.TryParse(em.color, out col)) _mechColor = Mathf.Clamp(col, 1, MechanismColors.Count);
                    if (em.type == "unlock_wall") _mechHeight = Mathf.Max(1, em.height);
                }
            }

            // 跳转：真虚空 → 擦除；否则 → 放置（有机关则切到机关分类）
            if (explicitVoid)
            {
                _mode = ToolMode.Erase;
                _category = "terrain";
            }
            else
            {
                _mode = ToolMode.Paint;
                _category = mechDef != null ? "mechanism" : "terrain";
            }
        }

        static TerrainEntry TerrainAtXZ(LevelData data, int x, int z)
        {
            for (int i = 0; i < data.terrain.Count; i++)
            {
                var t = data.terrain[i];
                if (t.pos.x == x && t.pos.z == z) return t;
            }
            return null;
        }

        static ObjectEntry ObjectAtXZ(LevelData data, int x, int z)
        {
            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                if (o.pos.x == x && o.pos.z == z) return o;
            }
            return null;
        }

        static MarkerEntry MarkerAtXZ(LevelData data, int x, int z)
        {
            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                if (m.pos.x == x && m.pos.z == z) return m;
            }
            return null;
        }

        static int WalkLevelAt(LevelData data, int x, int z)
        {
            int top = -1;
            bool has = false;
            bool isVoid = false;
            for (int i = 0; i < data.terrain.Count; i++)
            {
                var e = data.terrain[i];
                if (e.pos.x != x || e.pos.z != z) continue;
                has = true;
                if (e.type == "void") isVoid = true;
                else if (e.pos.y > top) top = e.pos.y;
            }

            if (isVoid) return 0;
            if (has && top >= 0) return top + 1;
            return data.baseHeight;
        }

        // 让所有物件 / 标记的 y 与所在柱高保持一致（柱高改动后数据里的 y 会过期）
        static void ResyncHeights(LevelData data)
        {
            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                o.pos.y = WalkLevelAt(data, o.pos.x, o.pos.z);
            }
            for (int i = 0; i < data.markers.Count; i++)
            {
                var m = data.markers[i];
                m.pos.y = WalkLevelAt(data, m.pos.x, m.pos.z);
            }
        }

        void Erase(Int3 cell)
        {
            if (_asset == null) return;
            var data = _asset.data;
            int x = cell.x;
            int z = cell.z;

            var eo = ObjectAtXZ(data, x, z);
            var em = MarkerAtXZ(data, x, z);
            var et = TerrainAtXZ(data, x, z);

            if (_category == "terrain")
            {
                bool willChange;
                if (eo != null || em != null) willChange = true;
                else if (et != null) willChange = et.type != "void";
                else willChange = true; // 裸格 → 写成显式虚空
                if (!willChange) return;
            }
            else
            {
                if (eo == null && em == null) return;
            }

            Edit(() =>
            {
                if (_category == "terrain")
                {
                    // 地形没了 → 上面的机关失去支撑，一起消失
                    RemoveObjectsAtXZ(data, x, z);
                    RemoveMarkersAtXZ(data, x, z);
                    RemoveTerrainAtXZ(data, x, z);

                    // 显式虚空：标记"这是被挖掉的真虚空"（与裸格区分）
                    data.terrain.Add(new TerrainEntry { type = "void", pos = new Int3(x, 0, z) });
                }
                else
                {
                    // 只擦机关，不动地形
                    RemoveObjectsAtXZ(data, x, z);
                    RemoveMarkersAtXZ(data, x, z);
                }
            });
        }

        static void RemoveTerrainAtXZ(LevelData data, int x, int z)
        {
            for (int i = data.terrain.Count - 1; i >= 0; i--)
            {
                var p = data.terrain[i].pos;
                if (p.x == x && p.z == z) data.terrain.RemoveAt(i);
            }
        }

        static void RemoveObjectsAtXZ(LevelData data, int x, int z)
        {
            for (int i = data.objects.Count - 1; i >= 0; i--)
            {
                var p = data.objects[i].pos;
                if (p.x == x && p.z == z) data.objects.RemoveAt(i);
            }
        }

        static void RemoveMarkersAtXZ(LevelData data, int x, int z)
        {
            for (int i = data.markers.Count - 1; i >= 0; i--)
            {
                var p = data.markers[i].pos;
                if (p.x == x && p.z == z) data.markers.RemoveAt(i);
            }
        }

        // ---------- 文件 ----------

        void NewLevelDialog()
        {
            if (!ConfirmDiscard()) return;

            var w = CreateInstance<NewLevelPopup>();
            w.owner = this;
            w.titleContent = new GUIContent("新建关卡");
            w.minSize = new Vector2(220f, 100f);
            w.maxSize = new Vector2(220f, 100f);
            w.ShowUtility();
        }

        public void CreateLevel(int len, int wid)
        {
            var data = new LevelData();
            data.id = "新建关卡";
            data.size = new Int3(Mathf.Clamp(len, 1, MaxSize), 2, Mathf.Clamp(wid, 1, MaxSize));
            data.baseHeight = 1;
            data.baseMaterial = "gray_light";

            _asset = ScriptableObject.CreateInstance<LevelAsset>();
            _asset.data = data;
            _path = null;
            _dirty = true;
            _solveTested = false;
            s_hasSaved = false;
            s_saved = null;
            s_undo.Clear();
            s_redo.Clear();
            SceneView.RepaintAll();
        }

        void OpenLevelPath(string path)
        {
            var data = LevelIO.Load(path);
            if (data == null)
            {
                Debug.LogError("[关卡编辑器] 读取失败: " + path);
                return;
            }

            _asset = ScriptableObject.CreateInstance<LevelAsset>();
            _asset.data = data;
            _path = path;
            _dirty = false;
            _solveTested = false;
            _cellSize = 1f;
            s_hasSaved = true;
            s_saved = LevelIO.ToJson(data, false);
            s_undo.Clear();
            s_redo.Clear();
            SceneView.RepaintAll();
        }

        bool SaveLevel()
        {
            if (_asset == null) return true;
            if (string.IsNullOrEmpty(_path)) return SaveLevelAs();

            // 让 id 与文件名保持一致
            _asset.data.id = System.IO.Path.GetFileNameWithoutExtension(_path);

            int errs = CountErrors();
            if (errs > 0 && !EditorUtility.DisplayDialog("校验未通过",
                    "有 " + errs + " 个错误，仍要保存吗？", "仍要保存", "取消"))
            {
                return false;
            }

            LevelIO.Save(_path, _asset.data);
            AssetDatabase.Refresh();
            s_saved = LevelIO.ToJson(_asset.data, false);
            s_hasSaved = true;
            _dirty = false;
            Debug.Log("[关卡编辑器] 已保存: " + _path);
            return true;
        }

        void HandleSaveShortcut()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (!(e.control || e.command)) return;

            if (e.keyCode == KeyCode.S) { SaveLevel(); e.Use(); }
            else if (e.keyCode == KeyCode.Z) { DoUndo(); e.Use(); }
            else if (e.keyCode == KeyCode.Y) { DoRedo(); e.Use(); }
        }

        bool SaveLevelAs()
        {
            if (_asset == null) return false;
            string dir = AbsDir(LevelsDir);
            string name = string.IsNullOrEmpty(_asset.data.id) ? "level" : _asset.data.id;
            string abs = EditorUtility.SaveFilePanel("保存关卡", dir, name + ".json", "json");
            if (string.IsNullOrEmpty(abs)) return false;

            _path = ToProjectPath(abs);
            return SaveLevel();
        }

        static string AbsDir(string assetPath)
        {
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length)).Replace('\\', '/');
        }

        static string ToProjectPath(string abs)
        {
            abs = abs.Replace('\\', '/');
            string dp = Application.dataPath.Replace('\\', '/');
            if (abs.StartsWith(dp)) return "Assets" + abs.Substring(dp.Length);
            return abs;
        }
    }

    // 新建关卡的小弹窗：输入长(X)/宽(Z)
    public class NewLevelPopup : EditorWindow
    {
        public LevelEditorWindow owner;
        int _len = 9;
        int _wid = 9;

        void OnGUI()
        {
            EditorGUILayout.Space();
            _len = Mathf.Clamp(EditorGUILayout.IntField("长 (X)", _len), 1, LevelEditorWindow.MaxSize);
            _wid = Mathf.Clamp(EditorGUILayout.IntField("宽 (Z)", _wid), 1, LevelEditorWindow.MaxSize);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("确定"))
            {
                if (owner != null) owner.CreateLevel(_len, _wid);
                Close();
            }
            if (GUILayout.Button("取消")) Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
