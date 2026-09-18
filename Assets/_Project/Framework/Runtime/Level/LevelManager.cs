using System.Collections.Generic;
using UnityEngine;

// 关卡总控：搭关卡、生成玩家、处理输入与规则、判胜、撤销、重置。

namespace Sokoban3D.Framework
{
    public class LevelManager : MonoBehaviour
    {
        public LevelBuilder builder;
        public float moveDuration = 0.12f;
        public Transform cameraTransform;
        public CameraController cameraController;
        public LevelCatalog catalog;

        public GridModel Model { get; private set; }
        public int Steps { get; private set; }
        public int Pushes { get; private set; }
        public bool IsSolved { get { return _solved; } }
        public int LevelIndex { get { return _levelIndex; } }
        public bool HasNextLevel { get { return catalog != null && _levelIndex + 1 < catalog.Count; } }
        public string CurrentLevelName { get { return builder != null && builder.levelFile != null ? builder.levelFile.name : "?"; } }
        public float CellSize { get { return builder != null ? builder.cellSize : 1f; } }

        public Vector3 CellToWorld(Int3 p)
        {
            return builder != null ? builder.CellToWorld(p) : Vector3.zero;
        }

        // 每一步的状态快照，用于逐步撤销
        class Snapshot
        {
            public Int3 player;
            public bool solved;
            public readonly List<Int3> boxes = new List<Int3>();
        }

        readonly List<BoxView> _boxViews = new List<BoxView>();
        readonly List<Snapshot> _history = new List<Snapshot>();
        readonly HashSet<Int3> _reachVisited = new HashSet<Int3>();
        readonly Queue<Int3> _reachQueue = new Queue<Int3>();
        readonly List<Int3> _reachResult = new List<Int3>();
        static readonly Int3[] Dirs = { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) };
        Int3 _playerCell;
        GridMover _playerMover;
        Transform _playerTransform;
        int _levelIndex;
        bool _busy;
        bool _solved;

        readonly List<Int3> _path = new List<Int3>();
        readonly Dictionary<Int3, Int3> _pathPrev = new Dictionary<Int3, Int3>();
        readonly Queue<Int3> _pathQueue = new Queue<Int3>();
        int _pathIndex;
        bool _autoWalking;

        void Start()
        {
            if (GetComponent<LevelHud>() == null) gameObject.AddComponent<LevelHud>();
            if (GetComponent<ReachHighlighter>() == null) gameObject.AddComponent<ReachHighlighter>();
            if (builder == null) builder = GetComponent<LevelBuilder>();
            if (cameraController == null && Camera.main != null) cameraController = Camera.main.GetComponent<CameraController>();
            if (cameraController != null) cameraTransform = cameraController.transform;
            else if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            if (catalog != null && catalog.Count > 0)
            {
                _levelIndex = 0;
                ApplyLevelFile();
            }

            Build();
            LogLevel();
        }

        void ApplyLevelFile()
        {
            var asset = catalog.Get(_levelIndex);
            if (asset != null && builder != null) builder.levelFile = asset;
        }

        void LogLevel()
        {
            string name = builder != null && builder.levelFile != null ? builder.levelFile.name : "?";
            Debug.Log("[LevelManager] 关卡 " + (_levelIndex + 1) + "/" + (catalog != null ? catalog.Count : 1) + " : " + name);
        }

        public void LoadLevel(int index)
        {
            if (catalog == null || catalog.Count == 0) return;
            _levelIndex = Mathf.Clamp(index, 0, catalog.Count - 1);
            ApplyLevelFile();
            Build();
            LogLevel();
        }

        public void NextLevel()
        {
            if (HasNextLevel) LoadLevel(_levelIndex + 1);
            else Debug.Log("[LevelManager] 已是最后一关");
        }

        // 不推箱子的前提下，当前角色能走到的所有格子（含起点）
        public List<Int3> ComputeReachable()
        {
            _reachResult.Clear();
            if (Model == null) return _reachResult;

            _reachVisited.Clear();
            _reachQueue.Clear();
            _reachQueue.Enqueue(_playerCell);
            _reachVisited.Add(_playerCell);

            while (_reachQueue.Count > 0)
            {
                var c = _reachQueue.Dequeue();
                _reachResult.Add(c);

                for (int i = 0; i < Dirs.Length; i++)
                {
                    var n = c + Dirs[i];
                    if (_reachVisited.Contains(n)) continue;
                    if (!Model.IsWalkable(n)) continue;
                    if (Model.HasBox(n)) continue;
                    _reachVisited.Add(n);
                    _reachQueue.Enqueue(n);
                }
            }
            return _reachResult;
        }

        public void Build()
        {
            if (builder == null)
            {
                Debug.LogError("[LevelManager] 没有 LevelBuilder");
                return;
            }

            builder.Build();
            Model = builder.Model;
            if (Model == null) return;

            _boxViews.Clear();
            _boxViews.AddRange(builder.BoxViews);
            foreach (var bv in _boxViews)
            {
                var m = bv.GetComponent<GridMover>();
                if (m != null) m.duration = moveDuration;
            }

            _history.Clear();
            Steps = 0;
            Pushes = 0;
            _autoWalking = false;
            _path.Clear();
            _pathIndex = 0;
            SpawnPlayer();
            _busy = false;
            _solved = false;
        }

        public void ResetLevel()
        {
            Build();
            Debug.Log("[LevelManager] 重置");
        }

        void SpawnPlayer()
        {
            Int3 spawn = new Int3(0, 0, 0);
            foreach (var m in Model.Markers)
            {
                if (m.type == "spawn")
                {
                    spawn = m.pos;
                    break;
                }
            }
            _playerCell = spawn;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Player";
            go.transform.SetParent(builder.Root, false);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            float s = builder.cellSize;
            go.transform.localScale = new Vector3(s * 0.8f, s * 0.5f, s * 0.8f);

            _playerMover = go.AddComponent<GridMover>();
            _playerMover.duration = moveDuration;
            _playerMover.SnapTo(PlayerWorld(_playerCell));

            _playerTransform = go.transform;
            if (cameraController != null) cameraController.target = _playerTransform;
        }

        Vector3 PlayerWorld(Int3 p)
        {
            return builder.CellToWorld(p) + new Vector3(0f, builder.cellSize * 0.5f, 0f);
        }

        void Update()
        {
            if (_busy)
            {
                if (_playerMover.IsMoving || !BoxesIdle()) return;
                _busy = false;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                LoadLevel(_levelIndex - 1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                LoadLevel(_levelIndex + 1);
                return;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                ResetLevel();
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Undo();
                return;
            }

            if (_solved)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                {
                    NextLevel();
                }
                return;
            }

            bool manual = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                       || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
            if (manual)
            {
                _autoWalking = false;
                Int3 dir = ReadDirection();
                if (dir.x == 0 && dir.y == 0 && dir.z == 0) return;
                TryMove(dir);
                return;
            }

            if (_autoWalking)
            {
                StepAutoWalk();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                TryClickMove();
            }
        }

        Int3 ReadDirection()
        {
            if (cameraController != null && cameraController.IsRotating) return new Int3(0, 0, 0);

            float h = 0f, v = 0f;
            if (Input.GetKey(KeyCode.W)) v += 1f;
            if (Input.GetKey(KeyCode.S)) v -= 1f;
            if (Input.GetKey(KeyCode.D)) h += 1f;
            if (Input.GetKey(KeyCode.A)) h -= 1f;
            if (h == 0f && v == 0f) return new Int3(0, 0, 0);

            Transform cam = cameraTransform != null ? cameraTransform : transform;
            Int3 fwd = SnapCardinal(cam.forward);
            Int3 right = SnapCardinal(cam.right);

            if (Mathf.Abs(v) >= Mathf.Abs(h) && v != 0f) return v > 0f ? fwd : new Int3(0, 0, 0) - fwd;
            return h > 0f ? right : new Int3(0, 0, 0) - right;
        }

        static Int3 SnapCardinal(Vector3 v)
        {
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
                return new Int3(v.x >= 0f ? 1 : -1, 0, 0);
            return new Int3(0, 0, v.z >= 0f ? 1 : -1);
        }

        // 点击地面自动走过去（不推箱；不可达则不动作）
        void TryClickMove()
        {
            var cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 500f)) return;

            Int3 cell = WorldToCell(hit.point);
            if (!Model.IsWalkable(cell) || Model.HasBox(cell)) return;
            if (!BuildPath(_playerCell, cell)) return;

            _pathIndex = 0;
            _autoWalking = true;
            StepAutoWalk();
        }

        Int3 WorldToCell(Vector3 world)
        {
            Vector3 lp = builder.transform.InverseTransformPoint(world);
            float cs = builder.cellSize;
            return new Int3(Mathf.RoundToInt(lp.x / cs), 0, Mathf.RoundToInt(lp.z / cs));
        }

        // BFS 最短路径，路径不含起点
        bool BuildPath(Int3 start, Int3 goal)
        {
            _path.Clear();
            if (start == goal) return false;

            _pathPrev.Clear();
            _pathQueue.Clear();
            _pathQueue.Enqueue(start);
            _pathPrev[start] = start;

            bool found = false;
            while (_pathQueue.Count > 0)
            {
                var c = _pathQueue.Dequeue();
                if (c == goal) { found = true; break; }

                for (int i = 0; i < Dirs.Length; i++)
                {
                    var n = c + Dirs[i];
                    if (_pathPrev.ContainsKey(n)) continue;
                    if (!Model.IsWalkable(n)) continue;
                    if (Model.HasBox(n)) continue;
                    _pathPrev[n] = c;
                    _pathQueue.Enqueue(n);
                }
            }

            if (!found) return false;

            var cur = goal;
            while (cur != start)
            {
                _path.Add(cur);
                cur = _pathPrev[cur];
            }
            _path.Reverse();
            return _path.Count > 0;
        }

        void StepAutoWalk()
        {
            if (!_autoWalking) return;
            if (_pathIndex >= _path.Count)
            {
                _autoWalking = false;
                return;
            }

            Int3 next = _path[_pathIndex];
            _pathIndex++;
            _history.Add(Capture());
            ApplyPlayerStep(next);
        }

        void TryMove(Int3 dir)
        {
            Int3 to = _playerCell + dir;
            if (!Model.IsWalkable(to)) return;

            var pushed = BoxViewAt(to);
            Int3 beyond = to + dir;
            if (pushed != null)
            {
                if (!Model.IsWalkable(beyond) || BoxViewAt(beyond) != null) return;
            }

            _history.Add(Capture());

            if (pushed != null)
            {
                Model.MoveBox(pushed.State, beyond);
                pushed.Refresh();
                Pushes++;
            }

            ApplyPlayerStep(to);
        }

        void ApplyPlayerStep(Int3 to)
        {
            _playerCell = to;
            _playerMover.MoveTo(PlayerWorld(_playerCell));
            _busy = true;
            Steps++;

            if (Model.IsSolved())
            {
                _solved = true;
                Debug.Log("[LevelManager] 通关！");
            }
        }

        void Undo()
        {
            if (_history.Count == 0) return;

            _autoWalking = false;
            var s = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);

            _playerCell = s.player;
            _playerMover.SnapTo(PlayerWorld(_playerCell));

            for (int i = 0; i < _boxViews.Count; i++)
            {
                Model.MoveBox(_boxViews[i].State, s.boxes[i]);
                _boxViews[i].Snap();
            }

            _solved = s.solved;
            _busy = false;
        }

        Snapshot Capture()
        {
            var s = new Snapshot();
            s.player = _playerCell;
            s.solved = _solved;
            for (int i = 0; i < _boxViews.Count; i++) s.boxes.Add(_boxViews[i].State.pos);
            return s;
        }

        BoxView BoxViewAt(Int3 cell)
        {
            for (int i = 0; i < _boxViews.Count; i++)
            {
                var s = _boxViews[i].State;
                if (s.pos.x == cell.x && s.pos.y == cell.y && s.pos.z == cell.z) return _boxViews[i];
            }
            return null;
        }

        bool BoxesIdle()
        {
            for (int i = 0; i < _boxViews.Count; i++)
            {
                var m = _boxViews[i].GetComponent<GridMover>();
                if (m != null && m.IsMoving) return false;
            }
            return true;
        }
    }
}
