using System.Collections.Generic;
using UnityEngine;

// 把关卡数据变成场景里的物体。地形/标记是静态的，箱子挂 BoxView 可移动。

namespace Sokoban3D.Framework
{
    public class LevelBuilder : MonoBehaviour
    {
        public TileTypeRegistry registry;
        public TextAsset levelFile;
        public float cellSize = 1f;

        public GridModel Model { get; private set; }
        public Transform Root { get; private set; }
        public readonly List<BoxView> BoxViews = new List<BoxView>();

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

            Clear();

            var data = LevelIO.FromJson(levelFile.text);
            Model = new GridModel(data);

            Root = new GameObject("LevelRoot").transform;
            Root.SetParent(transform, false);

            foreach (var t in data.terrain) SpawnStatic(t.type, t.pos);
            foreach (var m in data.markers) SpawnStatic(m.type, m.pos);

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
            BoxViews.Clear();
            Model = null;
            Root = null;
        }

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
                go.transform.localScale = new Vector3(cellSize * 0.98f, cellSize * h, cellSize * 0.98f);

                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.material.color = def != null ? def.color : Color.magenta;
            }

            go.name = typeId + " " + pos;
            go.transform.localPosition = CellToWorld(pos) + PlaceholderLift(def);
            return go;
        }

        Vector3 PlaceholderLift(TileTypeDefinition def)
        {
            if (def != null && def.prefab != null) return Vector3.zero;
            float h = def != null ? Mathf.Max(0.05f, def.placeholderHeight) : 1f;
            return new Vector3(0f, cellSize * h * 0.5f, 0f);
        }
    }
}
