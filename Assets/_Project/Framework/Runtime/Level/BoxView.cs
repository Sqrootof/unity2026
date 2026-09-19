using UnityEngine;

// 一个箱子的表现层。绑定一份 BoxState（逻辑位置），跟着它移动。

namespace Sokoban3D.Framework
{
    public class BoxView : MonoBehaviour
    {
        public BoxState State { get; private set; }

        float _cellSize;
        Vector3 _lift;
        GridMover _mover;
        Renderer _rend;
        Material _inst;
        Color _baseColor;
        bool _dead;
        bool _onTarget;

        static readonly Color DeadColor = new Color(0.45f, 0.45f, 0.45f);
        static readonly Color OnTargetColor = new Color(1f, 0.80f, 0.75f); // 水蜜桃浅红（低饱和）

        public void Init(BoxState state, float cellSize, Vector3 lift)
        {
            State = state;
            _cellSize = cellSize;
            _lift = lift;

            _mover = GetComponent<GridMover>();
            if (_mover == null) _mover = gameObject.AddComponent<GridMover>();
            _mover.SnapTo(WorldOf(State.pos));

            _rend = GetComponent<Renderer>();
            if (_rend != null && _rend.sharedMaterial != null) _baseColor = _rend.sharedMaterial.color;
        }

        // dead = 死格变灰；onTarget = 站在抵达点上变浅水蜜桃红（优先）
        public void SetHints(bool dead, bool onTarget)
        {
            if (_dead == dead && _onTarget == onTarget) return;
            _dead = dead;
            _onTarget = onTarget;

            if (_rend == null || _rend.sharedMaterial == null) return;

            Color c = onTarget ? OnTargetColor : (dead ? DeadColor : _baseColor);

            if (c == _baseColor)
            {
                if (_inst != null) _inst.color = _baseColor;
                return;
            }

            if (_inst == null)
            {
                _inst = new Material(_rend.sharedMaterial);
                _rend.sharedMaterial = _inst;
            }
            _inst.color = c;
        }

        public void Refresh()
        {
            _mover.MoveTo(WorldOf(State.pos));
        }

        public void Snap()
        {
            _mover.SnapTo(WorldOf(State.pos));
        }

        Vector3 WorldOf(Int3 p)
        {
            return new Vector3(p.x * _cellSize, p.y * _cellSize, p.z * _cellSize) + _lift;
        }
    }
}
