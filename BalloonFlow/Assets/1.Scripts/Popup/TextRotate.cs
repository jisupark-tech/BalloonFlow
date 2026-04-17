using TMPro;
using UnityEngine;

[ExecuteAlways]
public class CurvedTextTMP : MonoBehaviour
{
    [SerializeField] private TMP_Text textMeshPro;
    [SerializeField]
    private AnimationCurve curve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 30f),
        new Keyframe(1f, 0f)
    );

    [SerializeField] private float curveScale = 1f;

    [Tooltip("곡선을 매핑할 기준 가로폭 RectTransform (보통 배경 이미지). 비우면 TMP 자신의 RectTransform 사용.")]
    [SerializeField] private RectTransform referenceRect;

    private void Reset()
    {
        textMeshPro = GetComponent<TMP_Text>();
    }

    private void LateUpdate()
    {
        if (textMeshPro == null)
            return;

        textMeshPro.ForceMeshUpdate();

        var textInfo = textMeshPro.textInfo;
        int characterCount = textInfo.characterCount;

        if (characterCount == 0)
            return;

        RectTransform refRT = referenceRect != null ? referenceRect : textMeshPro.rectTransform;
        float refWidth = Mathf.Max(0.001f, refRT.rect.width);
        float halfRefWidth = refWidth * 0.5f;

        for (int i = 0; i < characterCount; i++)
        {
            if (!textInfo.characterInfo[i].isVisible)
                continue;

            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;

            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            Vector3 charMidBaseline = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;

            for (int j = 0; j < 4; j++)
                vertices[vertexIndex + j] -= charMidBaseline;

            float normalizedX = Mathf.Clamp01((charMidBaseline.x + halfRefWidth) / refWidth);
            float yOffset = curve.Evaluate(normalizedX) * curveScale;

            float tangentDelta = 0.001f;
            float y1Curve = curve.Evaluate(Mathf.Clamp01(normalizedX + tangentDelta)) * curveScale;
            float y0Curve = curve.Evaluate(Mathf.Clamp01(normalizedX - tangentDelta)) * curveScale;
            float angle = Mathf.Atan2(y1Curve - y0Curve, tangentDelta * refWidth) * Mathf.Rad2Deg;

            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(charMidBaseline.x, charMidBaseline.y + yOffset, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one
            );

            for (int j = 0; j < 4; j++)
                vertices[vertexIndex + j] = matrix.MultiplyPoint3x4(vertices[vertexIndex + j]);
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textMeshPro.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}
