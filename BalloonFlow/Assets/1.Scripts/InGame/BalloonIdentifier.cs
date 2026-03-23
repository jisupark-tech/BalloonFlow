using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to balloon GameObjects to identify them during dart hit detection.
    /// Animator 연동: Pop(bool) 파라미터로 팡 연출.
    /// </summary>
    /// <remarks>
    /// MUST be in its own file (BalloonIdentifier.cs) for Unity prefab serialization.
    /// Unity requires MonoBehaviour class name == file name for script GUID resolution.
    /// </remarks>
    public class BalloonIdentifier : MonoBehaviour
    {
        [SerializeField] private int _balloonId;
        [SerializeField] private int _color;

        private bool _isPopped;
        [SerializeField]
        private Animator _animator;
        private static readonly int _animPop = Animator.StringToHash("Pop");

        /// <summary>Unique balloon ID.</summary>
        public int BalloonId => _balloonId;

        /// <summary>Balloon color index.</summary>
        public int Color => _color;

        /// <summary>Whether this balloon has been popped.</summary>
        public bool IsPopped => _isPopped;

        /// <summary>Animator 초기화. 외부에서 명시적으로 호출.</summary>
        public void Init()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        /// <summary>Sets balloon properties (used by BalloonController during spawn).</summary>
        public void Initialize(int balloonId, int color)
        {
            _balloonId = balloonId;
            _color = color;
            _isPopped = false;

            Init();

            if (_animator != null)
                _animator.SetBool(_animPop, false);
        }

        /// <summary>Marks this balloon as popped + 팡 애니메이션 트리거.</summary>
        public void MarkPopped()
        {
            _isPopped = true;

            if (_animator != null)
                _animator.SetBool(_animPop, true);
        }
    }
}
