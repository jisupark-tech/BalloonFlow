using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to Holder GameObjects to identify them during raycasting.
    /// Animator 연동: Deploy(bool) = 배포 시작, end(trigger) = 배포 완료.
    /// </summary>
    /// <remarks>
    /// MUST be in its own file (HolderIdentifier.cs) for Unity prefab serialization.
    /// Unity requires MonoBehaviour class name == file name for script GUID resolution.
    /// </remarks>
    public class HolderIdentifier : MonoBehaviour
    {
        [SerializeField] private int _holderId;

        [SerializeField]
        private Animator _animator;
        private static readonly int _animDeploy = Animator.StringToHash("Deploy");
        private static readonly int _animEnd = Animator.StringToHash("end");

        /// <summary>The unique identifier for this holder.</summary>
        public int HolderId => _holderId;

        /// <summary>Animator 초기화. 외부에서 명시적으로 호출.</summary>
        public void Init()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Sets the holder ID (used by editor setup).</summary>
        public void SetHolderId(int id)
        {
            _holderId = id;
            Init();
        }

        /// <summary>배포 시작 — Deploy=true 애니메이션.</summary>
        public void StartDeploy()
        {
            if (_animator != null)
                _animator.SetBool(_animDeploy, true);
        }

        /// <summary>배포 완료 — end 트리거 + Deploy=false.</summary>
        public void EndDeploy()
        {
            if (_animator != null)
            {
                _animator.SetBool(_animDeploy, false);
                _animator.SetTrigger(_animEnd);
            }
        }

        /// <summary>재사용 시 애니메이터 리셋.</summary>
        public void ResetAnimator()
        {
            if (_animator != null)
            {
                _animator.SetBool(_animDeploy, false);
                _animator.ResetTrigger(_animEnd);
            }
        }
    }
}
