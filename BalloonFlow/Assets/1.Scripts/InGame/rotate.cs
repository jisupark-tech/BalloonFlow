using UnityEngine;

public class UIRotateLight : MonoBehaviour
{
    [SerializeField] private RectTransform target;
    [SerializeField] private float speed = 90f; // 초당 각도

    private void Reset()
    {
        target = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (target == null) return;

        target.Rotate(0f, 0f, speed * Time.deltaTime);
    }
}