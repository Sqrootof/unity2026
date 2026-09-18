using UnityEngine;

// 半俯视跟随相机。Q/E 旋转（松手吸附到 90° 整数倍），滚轮缩放。
// 朝向由 pitch/yaw 直接算出，不做"看向目标"的插值，避免移动时抖动。

namespace Sokoban3D.Framework
{
    public class CameraController : MonoBehaviour
    {
        public Transform target;
        public float pitch = 60f;
        public float yaw = 0f;
        public float distance = 8f;
        public float minDistance = 4f;
        public float maxDistance = 20f;
        public float rotateSpeed = 120f;
        public float snapTime = 0.12f;

        // 0 = 硬跟随（推荐，配合角色自身的插值已足够平滑）；大于 0 则做指数平滑
        public float followSmooth = 0f;

        public bool IsRotating { get; private set; }

        Vector3 _pivot;
        bool _hasPivot;
        float _targetYaw;
        float _yawVelocity;
        bool _snapping;

        void LateUpdate()
        {
            float rot = 0f;
            if (Input.GetKey(KeyCode.Q)) rot += 1f;
            if (Input.GetKey(KeyCode.E)) rot -= 1f;

            if (rot != 0f)
            {
                yaw += rot * rotateSpeed * Time.deltaTime;
                IsRotating = true;
                _snapping = false;
                _yawVelocity = 0f;
            }
            else if (IsRotating)
            {
                if (!_snapping)
                {
                    _targetYaw = Mathf.Round(yaw / 90f) * 90f;
                    _snapping = true;
                }

                yaw = Mathf.SmoothDampAngle(yaw, _targetYaw, ref _yawVelocity, snapTime);

                if (Mathf.Abs(Mathf.DeltaAngle(yaw, _targetYaw)) < 0.05f)
                {
                    yaw = _targetYaw;
                    _yawVelocity = 0f;
                    IsRotating = false;
                    _snapping = false;
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance = Mathf.Clamp(distance - scroll * 5f, minDistance, maxDistance);
            }

            if (target == null) return;

            if (!_hasPivot)
            {
                _pivot = target.position;
                _hasPivot = true;
            }

            _pivot = followSmooth > 0f
                ? Vector3.Lerp(_pivot, target.position, 1f - Mathf.Exp(-Time.deltaTime / followSmooth))
                : target.position;

            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = _pivot - (look * Vector3.forward) * distance;
            transform.rotation = look;
        }
    }
}
