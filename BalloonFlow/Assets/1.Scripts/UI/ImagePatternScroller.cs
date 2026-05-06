using UnityEngine;
using UnityEngine.UI;

public class ImagePatternScroller : MonoBehaviour
{
    [SerializeField] private RawImage rawImage;
    [SerializeField] private Vector2 speed = new Vector2(-0.05f, -0.05f);

    private Rect uvRect;

    private void Awake()
    {
        if (rawImage == null)
            rawImage = GetComponent<RawImage>();

        uvRect = rawImage.uvRect;
    }

    private void Update()
    {
        uvRect.position += speed * Time.deltaTime;
        rawImage.uvRect = uvRect;
    }
}