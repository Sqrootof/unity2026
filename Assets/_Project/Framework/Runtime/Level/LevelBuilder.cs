using System.Collections.Generic;
using UnityEngine;

// 把关卡数据变成场景物体。
// 底面 + 显式地形统一按"逐块"合并成 chunk 网格（保留块间缝隙）；
// 物件/标记数量少，仍用独立物体。

namespace Sokoban3D.Framework
{
    public class LevelBuilder : MonoBehaviour
    {
        public TileTypeRegistry registry;
        public TextAsset levelFile;
        public float cellSize = 1f;
        public int chunkSize = 16;
        public float blockScale = 0.94f;   // 地形 / 墙
        public float objectScale = 0.8f;   // 箱子等物件
        public float markerScale = 0.87f;  // 出生点 / 抵达点等标记
        public bool flatColors = false;     // 预览用：无光照纯色

        public GridModel Model { get; private set; }
        public Transform Root { get; private set; }
        public readonly List<BoxView> BoxViews = new List<BoxView>();

        readonly List<Mesh> _tempMeshes = new List<Mesh>();
        readonly List<LockView> _lockViews = new List<LockView>();
        class LockView { public string color; public int x; public int z; public GameObject go; }
        Dictionary<string, Material> _matCache;
        Dictionary<Color, Material> _colorMats;
        static Mesh _cubeMesh;

        struct Col
        {
            public int x;
            public int z;
            public int top;
            public string type;
        }

        public Vector3 CellToWorld(Int3 p)
        {
            return new Vector3(p.x * cellSize, p.y * cellSize, p.z * cellSize);
        }

        public void Build()
        {
            if (levelFile == null)
            {
                Debug.LogError("[LevelBuilder] 没有指定关卡文件");
                return;
            }
            BuildFromData(LevelIO.FromJson(levelFile.text));
        }

        public void BuildFromData(LevelData data)
        {
            Clear();

            Model = new GridModel(data);

            Root = new GameObject("LevelRoot").transform;
            Root.SetParent(transform, false);

            _matCache = new Dictionary<string, Material>();
            _colorMats = new Dictionary<Color, Material>();
            BuildTerrain(data);
            foreach (var m in data.markers)
            {
                if (m.type == "button") SpawnButton(m);
                else if (m.type == "unlock_wall") SpawnLockWall(m);
                // 出生点 / 抵达点：高度取模型实时地形高，避免数据里过期的 y
                else SpawnStatic(m.type, new Int3(m.pos.x, Model.TerrainHeightAt(m.pos.x, m.pos.z), m.pos.z));
            }
            RefreshLocks(Model);

            for (int i = 0; i < data.objects.Count; i++)
            {
                var o = data.objects[i];
                var def = registry != null ? registry.Get(o.type) : null;
                var go = CreateVisual(def, o.type, o.pos);

                var view = go.AddComponent<BoxView>();
                view.Init(Model.Boxes[i], cellSize, PlaceholderLift(def));
                BoxViews.Add(view);
            }
        }

        public void Clear()
        {
            if (Root != null)
            {
                if (Application.isPlaying) Destroy(Root.gameObject);
                else DestroyImmediate(Root.gameObject);
            }

            for (int i = 0; i < _tempMeshes.Count; i++)
            {
                var mesh = _tempMeshes[i];
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }

            _tempMeshes.Clear();
            _lockViews.Clear();
            BoxViews.Clear();
            Model = null;
            Root = null;
        }

        // ---------- 地形：底面 + 显式地形，逐块合并 ----------

        void BuildTerrain(LevelData data)
        {
            int w = data.size.x;
            int d = data.size.z;
            if (w <= 0 || d <= 0) return;

            var explicitSet = new Dictionary<long, TerrainEntry>();
            for (int i = 0; i < data.terrain.Count; i++)
            {
                var t = data.terrain[i];
                if (t.pos.x < 0 || t.pos.x >= w || t.pos.z < 0 || t.pos.z >= d) continue;
                explicitSet[Key(t.pos.x, t.pos.z)] = t;
            }

            int cs2 = Mathf.Max(4, chunkSize);
            int cxs = Mathf.Max(1, (w + cs2 - 1) / cs2);

            var byChunk = new Dictionary<int, List<Col>>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < d; z++)
                {
                    Col col;
                    TerrainEntry e;
                    if (explicitSet.TryGetValue(Key(x, z), out e))
                    {
                        if (e.type == "void") continue; // 挖空的格子不生成
                        col = new Col { x = x, z = z, top = e.pos.y, type = e.type };
                    }
                    else
                    {
                        if (data.baseHeight <= 0) continue;
                        col = new Col { x = x, z = z, top = data.baseHeight - 1, type = data.baseMaterial };
                    }

                    int key = (x / cs2) + (z / cs2) * cxs;
                    List<Col> list;
                    if (!byChunk.TryGetValue(key, out list))
                    {
                        list = new List<Col>();
                        byChunk[key] = list;
                    }
                    list.Add(col);
                }
            }

            foreach (var kv in byChunk) BuildChunk(kv.Value);
        }

        void BuildChunk(List<Col> cols)
        {
            var byType = new Dictionary<string, List<Col>>();
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                List<Col> l;
                if (!byType.TryGetValue(c.type, out l))
                {
                    l = new List<Col>();
                    byType[c.type] = l;
                }
                l.Add(c);
            }

            foreach (var tg in byType) BuildTypeMesh(tg.Key, tg.Value);
        }

        void BuildTypeMesh(string typeId, List<Col> cols)
        {
            var combines = new CombineInstance[cols.Count];
            for (int i = 0; i < cols.Count; i++)
            {
                var c = cols[i];
                float h = (c.top + 1) * cellSize;
                combines[i] = new CombineInstance
                {
                    mesh = CubeMesh(),
                    transform = Matrix4x4.TRS(
                        new Vector3(c.x * cellSize, h * 0.5f, c.z * cellSize),
                        Quaternion.identity,
                        new Vector3(cellSize * blockScale, h, cellSize * blockScale))
                };
            }

            var mesh = new Mesh();
            mesh.CombineMeshes(combines, true, true);
            mesh.RecalculateBounds();
            _tempMeshes.Add(mesh);

            var go = new GameObject("TerrainChunk " + typeId);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(typeId);
        }

        static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        Material MaterialFor(string typeId)
        {
            Material mat;
            if (_matCache != null && _matCache.TryGetValue(typeId, out mat)) return mat;

            var def = registry != null ? registry.Get(typeId) : null;
            if (!flatColors && def != null && def.surfaceMaterial != null)
            {
                mat = def.surfaceMaterial;
            }
            else
            {
                Shader sh = flatColors ? Shader.Find("Unlit/Color") : Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Diffuse");
                mat = new Material(sh);
                mat.color = def != null ? def.color : Color.magenta;
            }

            if (_matCache != null) _matCache[typeId] = mat;
            return mat;
        }

        static Mesh CubeMesh()
        {
            if (_cubeMesh != null) return _cubeMesh;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeMesh = go.GetComponent<MeshFilter>().sharedMesh;
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
            return _cubeMesh;
        }

        // ---------- 标记 / 物件 ----------

        void SpawnStatic(string typeId, Int3 pos)
        {
            var def = registry != null ? registry.Get(typeId) : null;
            CreateVisual(def, typeId, pos);
        }

        GameObject CreateVisual(TileTypeDefinition def, string typeId, Int3 pos)
        {
            GameObject go;
            if (def != null && def.prefab != null)
            {
                go = Instantiate(def.prefab, Root);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(Root, false);

                float h = def != null ? Mathf.Max(0.05f, def.placeholderHeight) : 1f;
                float hs = def != null && def.category == "marker" ? markerScale : objectScale;
                go.transform.localScale = new Vector3(cellSize * hs, cellSize * h, cellSize * hs);

                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = MaterialFor(typeId);

                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }
            }

            go.name = typeId + " " + pos;
            go.transform.localPosition = CellToWorld(pos) + PlaceholderLift(def);
            return go;
        }

        // 按钮：球心落在地表高度上，下半埋进方块 → 露出半球（直径 0.35）
        void SpawnButton(MarkerEntry m)
        {
            float y = Model.TerrainHeightAt(m.pos.x, m.pos.z) * cellSize;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "button " + m.pos;
            go.transform.SetParent(Root, false);
            float d = 0.35f * cellSize;
            go.transform.localScale = new Vector3(d, d, d);
            go.transform.localPosition = new Vector3(m.pos.x * cellSize, y, m.pos.z * cellSize);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = ColorMat(MechanismColors.Get(m.color));
            RemoveCollider(go);
        }

        // 可解锁墙：圆柱（直径 0.9，高 = 配置高度），叠在地形之上
        void SpawnLockWall(MarkerEntry m)
        {
            int wallH = Mathf.Max(1, m.height);
            int terrainH = Model.TerrainHeightAt(m.pos.x, m.pos.z);
            if (terrainH < 0) terrainH = 0;

            float h = wallH * cellSize;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "lock_wall " + m.pos;
            go.transform.SetParent(Root, false);
            go.transform.localScale = new Vector3(0.9f * cellSize, h * 0.5f, 0.9f * cellSize);
            go.transform.localPosition = new Vector3(m.pos.x * cellSize, terrainH * cellSize + h * 0.5f, m.pos.z * cellSize);

            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = ColorMat(MechanismColors.Get(m.color));
            RemoveCollider(go);

            _lockViews.Add(new LockView { color = m.color, x = m.pos.x, z = m.pos.z, go = go });
        }

        // 按模型的可解锁墙状态显隐
        public void RefreshLocks(GridModel model)
        {
            for (int i = 0; i < _lockViews.Count; i++)
            {
                var v = _lockViews[i];
                if (v.go == null) continue;
                bool present = model == null || model.IsWallPresent(v.x, v.z, v.color);
                v.go.SetActive(present);
            }
        }

        Material ColorMat(Color c)
        {
            Material mat;
            if (_colorMats != null && _colorMats.TryGetValue(c, out mat)) return mat;

            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");
            mat = new Material(sh);
            mat.color = c;
            if (_colorMats != null) _colorMats[c] = mat;
            return mat;
        }

        void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        Vector3 PlaceholderLift(TileTypeDefinition def)
        {
            if (def != null && def.prefab != null) return Vector3.zero;
            float h = def != null ? Mathf.Max(0.05f, def.placeholderHeight) : 1f;
            return new Vector3(0f, cellSize * h * 0.5f, 0f);
        }
    }
}
