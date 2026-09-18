using UnityEngine;

// 格步进移动的插值表现。逻辑瞬变，视觉平滑。
// 逻辑位置由调用方维护，这里只负责"从当前世界坐标滑到目标世界坐标"。

namespace Sokoban3D.Framework
{
    public class GridMover : MonoBehaviour
    {
        public float duration = 0.12f;

        Vector3 _from;
        Vector3 _to;
        float _t = 1f;
        bool _moving;

        public bool IsMoving { get { return _moving; } }

        public void SnapTo(Vector3 world)
        {
            transform.localPosition = world;
            _from = world;
            _to = world;
            _t = 1f;
            _moving = false;
        }

        public void MoveTo(Vector3 world)
        {
            _from = transform.localPosition;
            _to = world;
            _t = 0f;
            _moving = true;
        }

        void Update()
        {
            if (!_moving) return;

            _t += Time.deltaTime / Mathf.Max(0.0001f, duration);
            if (_t >= 1f)
            {
                _t = 1f;
                _moving = false;
            }
            transform.localPosition = Vector3.Lerp(_from, _to, _t);
        }
    }
}
