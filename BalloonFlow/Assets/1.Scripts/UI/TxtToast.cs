using UnityEngine;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    [RequireComponent(typeof(CanvasGroup))]
    public class TxtToast : MonoBehaviour
    {
        public const string POOL_KEY = "TxtToast";

        [SerializeField] private TMP_Text _txtMain;
        [SerializeField] private TMP_Text _txtOutline;

        [Header("[Timing]")]
        [Tooltip("등장 애니메이션 시간(초)")]
        [SerializeField] private float _slideInDuration = 0.35f;
        [Tooltip("표시 유지 시간(초)")]
        [SerializeField] private float _holdDuration = 0.5f;
        [Tooltip("퇴장 애니메이션 시간(초)")]
        [SerializeField] private float _slideOutDuration = 0.3f;
        [Tooltip("슬라이드 이동 거리(px)")]
        [SerializeField] private float _slideOffset = 120f;

        private CanvasGroup _cg;
        private RectTransform _rt;
        private Sequence _seq;
        private bool _poolRegistered;

        public void SetText(string message)
        {
            if (_txtMain != null) _txtMain.text = message;
            if (_txtOutline != null) _txtOutline.text = message;
        }

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
            _rt = GetComponent<RectTransform>();
        }

        private void Start()
        {
            Play();
        }

        private void OnDisable()
        {
            _seq?.Kill();
            _seq = null;
        }

        public void Play()
        {
            _seq?.Kill();

            Vector2 targetPos = _rt.anchoredPosition;
            Vector2 startPos = targetPos + Vector2.down * _slideOffset;
            Vector2 exitPos = targetPos + Vector2.down * _slideOffset;

            _rt.anchoredPosition = startPos;
            _cg.alpha = 0f;

            _seq = DOTween.Sequence();
            _seq.Append(_rt.DOAnchorPos(targetPos, _slideInDuration).SetEase(Ease.OutCubic));
            _seq.Join(_cg.DOFade(1f, _slideInDuration).SetEase(Ease.OutCubic));
            _seq.AppendInterval(_holdDuration);
            _seq.Append(_cg.DOFade(0f, _slideOutDuration).SetEase(Ease.InCubic));
            _seq.OnComplete(ReturnToPool);
            _seq.SetUpdate(true);
        }

        private void ReturnToPool()
        {
            _seq = null;
            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
            {
                ObjectPoolManager.Instance.Return(POOL_KEY, gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        public static void EnsurePool()
        {
            if (!ObjectPoolManager.HasInstance) return;
            if (ObjectPoolManager.Instance.HasPool(POOL_KEY)) return;

            var prefab = Resources.Load<GameObject>("Popup/TxtToast");
            if (prefab == null) return;
            ObjectPoolManager.Instance.CreatePool(POOL_KEY, prefab, 2);
        }

        public static TxtToast Spawn(Transform parent, string message, Vector2 anchoredPos)
        {
            EnsurePool();

            GameObject go = null;
            if (ObjectPoolManager.HasInstance && ObjectPoolManager.Instance.HasPool(POOL_KEY))
                go = ObjectPoolManager.Instance.Get(POOL_KEY);

            if (go == null)
            {
                var prefab = Resources.Load<GameObject>("Popup/TxtToast");
                if (prefab == null) return null;
                go = Instantiate(prefab);
            }

            go.transform.SetParent(parent, false);
            go.SetActive(true);

            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = anchoredPos;

            var toast = go.GetComponent<TxtToast>();
            if (toast != null)
            {
                toast.SetText(message);
                toast.Play();
            }

            return toast;
        }
    }
}
