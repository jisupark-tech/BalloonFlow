using TMPro;
using UnityEngine;

[ExecuteAlways]
public class CurvedTextTMP : MonoBehaviour
{
    [SerializeField] private TMP_Text textMeshPro;

    [Tooltip("위로 둥근 돔 형태 기본 커브. 양 끝은 가파르게 올라가고 중앙은 평평한 아치.")]
    [SerializeField]
    private AnimationCurve curve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 120f),
        new Keyframe(0.5f, 30f, 0f, 0f),
        new Keyframe(1f, 0f, -120f, 0f)
    );

    [SerializeField] private float curveScale = 1f;

    [Tooltip("글자 간 추가 간격 (픽셀). +면 벌어지고, -면 좁혀짐.")]
    [SerializeField] private float spacing = 0f;

    [Tooltip("곡선을 매핑할 기준 가로폭 RectTransform (보통 배경 이미지). 비우면 TMP 자신의 RectTransform 사용.")]
    [SerializeField] private RectTransform referenceRect;

    private void Reset()
    {
        textMeshPro = GetComponent<TMP_Text>();
    }

    // ROLLBACK_CURVEDTEXT_DIRTY_GUARD_20260617: START
    // 기존 LateUpdate 는 매 프레임 ForceMeshUpdate + 전체 버텍스 재커브를 수행했다. 텍스트/폭/커브가
    // 안 바뀌면 매번 '같은 결과'를 다시 만드는 낭비이고, 더 큰 문제는 이게 매 프레임 그 캔버스를 dirty 시켜
    // UICamera(Overlay) 패스가 'UI 정지 상태'에서도 매 프레임 재렌더되게 만든다(프로파일 UICamera 2ms 상시).
    // → 입력(텍스트/기준폭/TMP 재빌드 플래그)이 바뀐 프레임에만 재적용. 정적 타이틀/배너는 1회 bake 후 스킵.
    //   OnEnable 에서 강제 1회 재적용(팝업 재활성·외부 ForceMeshUpdate 로 mesh 가 평평해진 경우 복구).
    // 롤백: _applied/_appliedText/_appliedWidth 필드 + OnEnable + LateUpdate 상단 가드 제거(기존 무조건 실행).
    private bool _applied;
    private string _appliedText;
    private float _appliedWidth = -1f;

    private void OnEnable()
    {
        _applied = false; // 재활성 시 mesh 가 재생성됐을 수 있으므로 다음 LateUpdate 에서 강제 재적용.
    }

    private void LateUpdate()
    {
        if (textMeshPro == null)
            return;

        RectTransform guardRect = referenceRect != null ? referenceRect : textMeshPro.rectTransform;
        float guardWidth = guardRect.rect.width;
        if (_applied
            && !textMeshPro.havePropertiesChanged
            && _appliedText == textMeshPro.text
            && Mathf.Approximately(_appliedWidth, guardWidth))
        {
            return; // 변경 없음 — 매 프레임 재커브/캔버스 dirty 스킵.
        }
        _applied = true;
        _appliedText = textMeshPro.text;
        _appliedWidth = guardWidth;
        // ROLLBACK_CURVEDTEXT_DIRTY_GUARD_20260617: END

        textMeshPro.ForceMeshUpdate();

        var textInfo = textMeshPro.textInfo;
        int characterCount = textInfo.characterCount;

        if (characterCount == 0)
            return;

        RectTransform refRT = referenceRect != null ? referenceRect : textMeshPro.rectTransform;
        float refWidth = Mathf.Max(0.001f, refRT.rect.width);
        float halfRefWidth = refWidth * 0.5f;

        int visibleCount = 0;
        for (int i = 0; i < characterCount; i++)
            if (textInfo.characterInfo[i].isVisible) visibleCount++;

        float spacingCenter = (visibleCount - 1) * 0.5f;
        int visibleIndex = 0;

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

            float spacingOffsetX = (visibleIndex - spacingCenter) * spacing;
            float adjustedX = charMidBaseline.x + spacingOffsetX;

            float normalizedX = Mathf.Clamp01((adjustedX + halfRefWidth) / refWidth);
            float yOffset = curve.Evaluate(normalizedX) * curveScale;

            float tangentDelta = 0.001f;
            float y1Curve = curve.Evaluate(Mathf.Clamp01(normalizedX + tangentDelta)) * curveScale;
            float y0Curve = curve.Evaluate(Mathf.Clamp01(normalizedX - tangentDelta)) * curveScale;
            float angle = Mathf.Atan2(y1Curve - y0Curve, tangentDelta * refWidth) * Mathf.Rad2Deg;

            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(adjustedX, charMidBaseline.y + yOffset, 0f),
                Quaternion.Euler(0f, 0f, angle),
                Vector3.one
            );

            for (int j = 0; j < 4; j++)
                vertices[vertexIndex + j] = matrix.MultiplyPoint3x4(vertices[vertexIndex + j]);

            visibleIndex++;
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textMeshPro.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}
