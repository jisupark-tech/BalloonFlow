using System.Collections;
using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Key 풍선이 터질 때 열쇠가 포물선으로 Lock 보관함까지 비행하는 연출.
    /// 비행 완료 시 HolderManager.UnlockHolder 호출.
    /// </summary>
    public class KeyFlightAnimator : SceneSingleton<KeyFlightAnimator>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!HasInstance)
            {
                var go = new GameObject("KeyFlightAnimator");
                go.AddComponent<KeyFlightAnimator>();
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnKeyReleased>(HandleKeyReleased);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnKeyReleased>(HandleKeyReleased);
        }

        private void HandleKeyReleased(OnKeyReleased evt)
        {
            Debug.Log($"[KeyFlightAnimator] Received OnKeyReleased pairId={evt.pairId}");
            if (!HolderManager.HasInstance || !HolderVisualManager.HasInstance) return;

            HolderData[] holders = HolderManager.Instance.GetHolders();
            int targetHolderId = -1;
            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i].lockPairId == evt.pairId && holders[i].isLocked)
                {
                    targetHolderId = holders[i].holderId;
                    break;
                }
            }

            if (targetHolderId < 0)
            {
                // Lock 보관함 못 찾음 → 즉시 해제
                if (HolderManager.HasInstance)
                    HolderManager.Instance.UnlockHolder(evt.pairId);
                return;
            }

            GameObject targetObj = HolderVisualManager.Instance.GetHolderGameObject(targetHolderId);
            if (targetObj == null)
            {
                if (HolderManager.HasInstance)
                    HolderManager.Instance.UnlockHolder(evt.pairId);
                return;
            }

            Vector3 targetPos = targetObj.transform.position + Vector3.up * 0.6f;
            StartCoroutine(FlyKeyCoroutine(evt.keyPosition, targetPos, evt.pairId));
        }

        private IEnumerator FlyKeyCoroutine(Vector3 start, Vector3 end, int pairId)
        {
            GameObject keyPrefab = Resources.Load<GameObject>("Prefabs/Key");
            if (keyPrefab == null)
            {
                if (HolderManager.HasInstance)
                    HolderManager.Instance.UnlockHolder(pairId);
                yield break;
            }

            Vector3 startPos = start + Vector3.up * 0.3f;
            GameObject keyObj = Instantiate(keyPrefab, startPos, Quaternion.identity);

            // Phase 1: 위로 튕김 (0.15초)
            Vector3 bounceTop = startPos + Vector3.up * 1.2f;
            float t = 0f;
            const float bounceDur = 0.15f;
            while (t < bounceDur)
            {
                t += Time.deltaTime;
                float p = t / bounceDur;
                keyObj.transform.position = Vector3.Lerp(startPos, bounceTop, Mathf.Sin(p * Mathf.PI * 0.5f));
                yield return null;
            }

            // Phase 2: 포물선 비행 (0.5초)
            const float flyDur = 0.5f;
            const float arcHeight = 2f;
            t = 0f;
            while (t < flyDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / flyDur);
                Vector3 linear = Vector3.Lerp(bounceTop, end, p);
                float arc = arcHeight * 4f * p * (1f - p);
                keyObj.transform.position = linear + Vector3.up * arc;
                keyObj.transform.Rotate(Vector3.forward, 540f * Time.deltaTime);
                yield return null;
            }

            keyObj.transform.position = end;
            Destroy(keyObj);

            // 잠금 해제
            if (HolderManager.HasInstance)
                HolderManager.Instance.UnlockHolder(pairId);
        }
    }
}
