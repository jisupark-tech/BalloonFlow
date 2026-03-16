using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(LayoutElement))]
public class ResponsiveButtonSize : MonoBehaviour
{
    [Header("Size Limits")]
    [SerializeField] private float minSize = 120f;
    [SerializeField] private float maxSize = 220f;

    [Header("Square Button")]
    [SerializeField] private bool keepSquare = true;

    private RectTransform rectTransform;
    private LayoutElement layoutElement;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        layoutElement = GetComponent<LayoutElement>();
        ApplySize();
    }

    private void OnEnable()
    {
        ApplySize();
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplySize();
    }

    private void ApplySize()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (layoutElement == null) layoutElement = GetComponent<LayoutElement>();

        float currentWidth = rectTransform.rect.width;

        // 현재 계산된 폭을 최소/최대 범위로 제한
        float clampedSize = Mathf.Clamp(currentWidth, minSize, maxSize);

        // Layout 기준값 갱신
        layoutElement.minWidth = minSize;
        layoutElement.minHeight = minSize;

        layoutElement.preferredWidth = clampedSize;
        layoutElement.preferredHeight = keepSquare ? clampedSize : layoutElement.preferredHeight;

        // 실제 RectTransform 크기도 보정
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, clampedSize);

        if (keepSquare)
        {
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, clampedSize);
        }
    }
}