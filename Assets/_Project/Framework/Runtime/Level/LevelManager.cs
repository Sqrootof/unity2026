using System.Collections.Generic;
using UnityEngine;

// 关卡总控：搭关卡、生成玩家、处理输入与规则、判胜、撤销、重置。

namespace Sokoban3D.Framework
{
    public class LevelManager : MonoBehaviour
    {
        public LevelBuilder builder;
        public float moveDuration = 0.12f;
        public float pushDuration = 0.24f;
        public float fallDuration = 0.18f;   // 从高柱走到更低的柱顶（掉落）
        public float holdDelay = 0.28f;   // 按住多久才开始连续移动（低于此值单击只走一格）
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
        Int3 _heldDir;
        float _holdTime;
        bool _holding;
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
                    Int3 n;
                    if (!TryNeighbor(c, Dirs[i], out n)) continue;
                    if (_reachVisited.Contains(n)) continue;
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
            RefreshWalls();
            RefreshBoxHints();
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

            Int3 dir = ReadDirection();
            bool hasDir = !(dir.x == 0 && dir.y == 0 && dir.z == 0);

            if (!hasDir)
            {
                _holding = false;
                _holdTime = 0f;

                if (_autoWalking)
                {
                    StepAutoWalk();
                    return;
                }

                if (Input.GetMouseButtonDown(0)) TryClickMove();
                return;
            }

            _autoWalking = false;

            if (!_holding || dir.x != _heldDir.x || dir.y != _heldDir.y || dir.z != _heldDir.z)
            {
                // 新方向：立即走一格（单击必定只走一格）
                _holding = true;
                _heldDir = dir;
                _holdTime = 0f;
                TryMove(dir);
                return;
            }

            // 同一方向按住：超过阈值才开始连续步进
            _holdTime += Time.deltaTime;
            if (_holdTime >= holdDelay) TryMove(dir);
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

            float planeY = _playerCell.y * builder.cellSize;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            float dist;
            if (!plane.Raycast(ray, out dist)) return;

            Int3 cell = WorldToCell(ray.GetPoint(dist));
            if (!Model.InXZ(cell.x, cell.z) || Model.TopAt(cell.x, cell.z) <= 0) return;
            if (!BuildPath(_playerCell, cell)) return;

            _pathIndex = 0;
            _autoWalking = true;
            StepAutoWalk();
        }

        Int3 WorldToCell(Vector3 world)
        {
            Vector3 lp = builder.transform.InverseTransformPoint(world);
            float cs = builder.cellSize;
            int x = Mathf.RoundToInt(lp.x / cs);
            int z = Mathf.RoundToInt(lp.z / cs);
            return new Int3(x, Model.TopAt(x, z), z);
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
                    Int3 n;
                    if (!TryStepBack(c, Dirs[i], out n)) continue;
                    if (_pathPrev.ContainsKey(n)) continue;
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
            _playerMover.duration = next.y < _playerCell.y ? fallDuration : moveDuration;
            ApplyPlayerStep(next);
        }

        void TryMove(Int3 dir)
        {
            Int3 flat = _playerCell + dir;                 // 同层邻格（y 不变）
            if (!Model.InXZ(flat.x, flat.z)) return;

            int top = Model.TopAt(flat.x, flat.z);         // 顶面（含箱子 +1 / 墙 +n）
            if (top <= 0) return;                          // 虚空

            var box = Model.BoxStateAtXZ(flat.x, flat.z);

            // 更高：若该格箱子底面与脚下齐平 → 推箱
            if (top > _playerCell.y)
            {
                if (box == null || box.pos.y != _playerCell.y) return;

                Int3 beyond = flat + dir;
                if (!Model.InXZ(beyond.x, beyond.z)) return;
                if (Model.BoxStateAtXZ(beyond.x, beyond.z) != null) return;

                int destFloor = Model.FloorAt(beyond.x, beyond.z);
                if (destFloor <= 0 || destFloor > _playerCell.y) return;   // 虚空 / 更高推不动

                Int3 boxTo = new Int3(beyond.x, destFloor, beyond.z);
                _history.Add(Capture());

                bool fell = boxTo.y < _playerCell.y;
                _playerMover.duration = pushDuration;
                var bv = BoxViewOf(box);
                if (bv != null)
                {
                    var bm = bv.GetComponent<GridMover>();
                    if (bm != null) bm.duration = fell ? fallDuration : pushDuration;
                }
                Model.MoveBox(box, boxTo);
                if (bv != null) bv.Refresh();
                Pushes++;

                ApplyPlayerStep(flat);                     // 玩家站进箱子原格（同层）
                return;
            }

            // 齐平（含踩上箱子顶 / 墙顶）或更低（掉下去）
            _history.Add(Capture());
            _playerMover.duration = top < _playerCell.y ? fallDuration : moveDuration;
            ApplyPlayerStep(new Int3(flat.x, top, flat.z));
        }

        // 从 from 朝 dir 走一步的落点（不含推箱）：顶面 <= 脚下即可（齐平走上去 / 更低掉下去）。
        // 供连续移动 / Shift 可达 / 点击寻路共用。
        bool TryNeighbor(Int3 from, Int3 dir, out Int3 next)
        {
            next = from;
            Int3 flat = from + dir;
            if (!Model.InXZ(flat.x, flat.z)) return false;

            int top = Model.TopAt(flat.x, flat.z);
            if (top <= 0 || top > from.y) return false;    // 虚空 / 更高都去不了
            next = new Int3(flat.x, top, flat.z);
            return true;
        }

        // 仅"可原路返回"的一步：走过去后，反向也能立刻走回来。
        // 下落这种单向移动不算（掉下去爬不回来）。用于点击自动寻路。
        bool TryStepBack(Int3 from, Int3 dir, out Int3 next)
        {
            next = from;
            if (!TryNeighbor(from, dir, out next)) return false;
            Int3 back;
            if (!TryNeighbor(next, new Int3(-dir.x, -dir.y, -dir.z), out back)) return false;
            return back == from;
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

            RefreshWalls();
            SettlePlayer();
            RefreshBoxHints();
        }

        // 脚下的支撑没了（如可解锁墙消失）→ 掉到当前顶面
        void SettlePlayer()
        {
            if (Model == null) return;
            int top = Model.TopAt(_playerCell.x, _playerCell.z);
            if (top <= 0 || top >= _playerCell.y) return;

            _playerCell = new Int3(_playerCell.x, top, _playerCell.z);
            _playerMover.duration = fallDuration;
            _playerMover.MoveTo(PlayerWorld(_playerCell));
            _busy = true;

            Model.SetActors(new[] { _playerCell });
            if (builder != null) builder.RefreshLocks(Model);
        }

        BoxView BoxViewOf(BoxState s)
        {
            for (int i = 0; i < _boxViews.Count; i++)
            {
                if (_boxViews[i].State == s) return _boxViews[i];
            }
            return null;
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
            RefreshWalls();
            RefreshBoxHints();
        }

        Snapshot Capture()
        {
            var s = new Snapshot();
            s.player = _playerCell;
            s.solved = _solved;
            for (int i = 0; i < _boxViews.Count; i++) s.boxes.Add(_boxViews[i].State.pos);
            return s;
        }

        // 用当前角色位置重算可解锁墙的显隐（箱子位置模型内部已知）
        void RefreshWalls()
        {
            if (Model == null) return;
            Model.SetActors(new[] { _playerCell });
            SettleBoxes();
            if (builder != null) builder.RefreshLocks(Model);
        }

        // 箱子脚下的支撑没了（如可解锁墙消失）→ 掉到当前支撑面
        void SettleBoxes()
        {
            for (int i = 0; i < _boxViews.Count; i++)
            {
                var b = _boxViews[i].State;
                int f = Model.FloorAt(b.pos.x, b.pos.z);
                if (f <= 0 || f >= b.pos.y) continue;

                Model.MoveBox(b, new Int3(b.pos.x, f, b.pos.z));
                var bm = _boxViews[i].GetComponent<GridMover>();
                if (bm != null) bm.duration = fallDuration;
                _boxViews[i].Refresh();
            }
        }

        // 箱子推进"永远推不动"的格子（且不是抵达点）时变灰
        void RefreshBoxHints()
        {
            if (Model == null) return;
            for (int i = 0; i < _boxViews.Count; i++)
            {
                var p = _boxViews[i].State.pos;
                _boxViews[i].SetHints(IsStaticDead(p), IsTargetCell(p));
            }
        }

        // 箱子能否朝 dir 被推：玩家站位与箱子底面同层，落点同层或更低（可推下台阶）且非虚空、无箱
        bool CanPush(Int3 c, Int3 dir)
        {
            Int3 behind = c - dir;
            if (!Model.InXZ(behind.x, behind.z)) return false;
            if (Model.TopAt(behind.x, behind.z) != c.y) return false;

            Int3 dest = c + dir;
            if (!Model.InXZ(dest.x, dest.z)) return false;
            if (Model.BoxStateAtXZ(dest.x, dest.z) != null) return false;

            int df = Model.FloorAt(dest.x, dest.z);
            return df > 0 && df <= c.y;
        }

        bool IsStaticDead(Int3 c)
        {
            if (IsTargetCell(c)) return false;
            return !CanPush(c, new Int3(1, 0, 0)) && !CanPush(c, new Int3(-1, 0, 0))
                && !CanPush(c, new Int3(0, 0, 1)) && !CanPush(c, new Int3(0, 0, -1));
        }

        bool IsTargetCell(Int3 c)
        {
            for (int i = 0; i < Model.Markers.Count; i++)
            {
                var m = Model.Markers[i];
                if (m.type == "target" && m.pos == c) return true;
            }
            return false;
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
