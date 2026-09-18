using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 按住 Shift 显示"当前角色可达的格子"。用一层半透明面片铺在地板上。
// 材质用 Standard 的 Fade 模式手搓，保证任何项目里都能透明显示。

namespace Sokoban3D.Framework
{
    public class ReachHighlighter : MonoBehaviour
    {
        public LevelManager manager;
        public KeyCode holdKey = KeyCode.LeftShift;
        public Color color = new Color(0.30f, 0.70f, 1f, 0.35f);
        public float liftAbove = 0.14f;

        readonly List<GameObject> _pool = new List<GameObject>();
        Transform _root;
        Material _mat;

        void Awake()
        {
            if (manager == null) manager = FindObjectOfType<LevelManager>();
        }

        void Update()
        {
            if (manager == null) manager = FindObjectOfType<LevelManager>();
            if (manager == null || manager.Model == null) return;

            if (Input.GetKey(holdKey)) Show(manager.ComputeReachable());
            else HideAll();
        }

        void Show(List<Int3> cells)
        {
            EnsureRoot();
            EnsurePool(cells.Count);

            for (int i = 0; i < _pool.Count; i++)
            {
                if (i < cells.Count)
                {
                    _pool[i].SetActive(true);
                    _pool[i].transform.localPosition = manager.CellToWorld(cells[i]) + new Vector3(0f, liftAbove, 0f);
                }
                else
                {
                    _pool[i].SetActive(false);
                }
            }
        }

        void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].activeSelf) _pool[i].SetActive(false);
            }
        }

        void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("ReachOverlay").transform;
            _root.SetParent(transform, false);
        }

        void EnsurePool(int count)
        {
            EnsureMaterial();
            if (_mat == null) return;

            while (_pool.Count < count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "ReachCell";
                go.transform.SetParent(_root, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                float s = manager.CellSize * 0.9f;
                go.transform.localScale = new Vector3(s, s, s);

                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                go.SetActive(false);
                _pool.Add(go);
            }
        }

        void EnsureMaterial()
        {
            if (_mat != null) return;

            var sh = Shader.Find("Sprites/Default");
            if (sh == null)
            {
                Debug.LogWarning("[ReachHighlighter] 找不到 Sprites/Default 着色器");
                return;
            }

            _mat = new Material(sh);
            _mat.color = color;
            _mat.renderQueue = 3000;
        }
    }
}
