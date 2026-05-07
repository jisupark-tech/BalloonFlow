using UnityEngine;
using TMPro;
using DG.Tweening;

namespace BalloonFlow
{
    /// <summary>
    /// TopBar GoldPanel의 TxtGold 노드에 자동 부착되어 CurrencyManager.Coins 변동을
    /// EventBus(OnCoinChanged) 구독으로 트윈 갱신하는 self-contained 라벨 컴포넌트.
    /// prefab Inspector 와이어링 의존성을 제거 — popup 들이 EnsureTopBarBinding 으로
    /// 런타임에 AddComponent 하면 자체적으로 동작.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnimatedCoinLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _txtGold;
        [SerializeField] private TMP_Text _txtGoldOutline;
        [SerializeField] private float _animDuration = 0.45f;

        private int _displayedCoins;
        private Tweener _goldTween;

        private void Awake()
        {
            if (_txtGold == null)
                _txtGold = GetComponent<TMP_Text>();

            if (_txtGoldOutline == null)
            {
                Transform outlineTr = transform.Find(gameObject.name + "Outline");
                if (outlineTr == null)
                    outlineTr = transform.Find("TxtGoldOutline");
                if (outlineTr != null)
                    _txtGoldOutline = outlineTr.GetComponent<TMP_Text>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnCoinChanged>(HandleCoinChanged);

            if (CurrencyManager.HasInstance)
            {
                _displayedCoins = CurrencyManager.Instance.Coins;
                Apply(_displayedCoins);
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnCoinChanged>(HandleCoinChanged);
            _goldTween?.Kill();
        }

        private void OnDestroy()
        {
            _goldTween?.Kill();
        }

        private void HandleCoinChanged(OnCoinChanged evt)
        {
            if (evt.delta == 0)
            {
                _goldTween?.Kill();
                _displayedCoins = evt.currentCoins;
                Apply(evt.currentCoins);
                return;
            }
            Animate(evt.currentCoins);
        }

        private void Animate(int target)
        {
            _goldTween?.Kill();
            if (_displayedCoins == target)
            {
                Apply(target);
                return;
            }
            _goldTween = DOTween.To(
                    () => _displayedCoins,
                    v => { _displayedCoins = v; Apply(v); },
                    target,
                    _animDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _displayedCoins = target;
                    Apply(target);
                });
        }

        private void Apply(int value)
        {
            string str = value.ToString("N0");
            if (_txtGold != null) _txtGold.text = str;
            if (_txtGoldOutline != null) _txtGoldOutline.text = str;
        }
    }
}
