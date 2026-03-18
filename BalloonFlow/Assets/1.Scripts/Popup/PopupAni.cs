using UnityEngine;
using DG.Tweening;

public class PopupBase : MonoBehaviour
{
    [SerializeField] private CanvasGroup dim;
    [SerializeField] private RectTransform popupWindow;

    void OnEnable()
    {
        PlayOpenAnimation();
    }

    public void PlayOpenAnimation()
    {
        dim.alpha = 0;
        popupWindow.localScale = Vector3.zero;

        Sequence seq = DOTween.Sequence();

        seq.Append(dim.DOFade(1f, 0.2f));
        seq.Join(popupWindow.DOScale(1f, 0.35f).SetEase(Ease.OutBack));
    }
}