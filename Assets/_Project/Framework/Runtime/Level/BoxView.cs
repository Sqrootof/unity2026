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

        public void Init(BoxState state, float cellSize, Vector3 lift)
        {
            State = state;
            _cellSize = cellSize;
            _lift = lift;

            _mover = GetComponent<GridMover>();
            if (_mover == null) _mover = gameObject.AddComponent<GridMover>();
            _mover.SnapTo(WorldOf(State.pos));
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
