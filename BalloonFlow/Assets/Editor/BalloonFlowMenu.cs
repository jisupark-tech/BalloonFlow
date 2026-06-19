#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BalloonFlow.Editor
{
    /// <summary>
    /// BalloonFlow 메뉴 탭 유틸리티.
    /// </summary>
    public static class BalloonFlowMenu
    {
        /// <summary>
        /// 유저 데이터 전체 초기화.
        ///
        /// 1.0 정책: 실제 소스는 Firestore /users/{uid} 단일 doc. PlayerPrefs 는 로컬 캐시일 뿐.
        /// 따라서 둘 다 지워야 다음 부팅에서 신규 유저 흐름 (UserDataService.LoadOrCreateUserAsync) 으로 진입.
        ///
        /// Play 중에 호출하면 Firestore doc 도 즉시 삭제 — Play 를 끄고 다시 켜야 in-memory 매니저가 fresh 상태로 재초기화됨.
        /// Play 중이 아니면 PlayerPrefs 만 삭제하고 Firestore 는 다음 Play 시점에 별도 절차 안내.
        /// </summary>
        [MenuItem("BalloonFlow/Reset User Data", false, 200)]
        private static void ResetUserData()
        {
            if (!EditorUtility.DisplayDialog("Reset User Data",
                "모든 유저 데이터를 초기화합니다.\n\n" +
                "  • PlayerPrefs (로컬 캐시 — 별/패키지/튜토리얼/하트 충전 등)\n" +
                "  • Firestore /users/{uid} doc — 골드/하트/부스터/광고제거/연승\n\n" +
                (EditorApplication.isPlaying
                    ? "현재 Play 중 — Firestore doc 도 즉시 삭제됩니다.\n초기화 후 Play 를 끄고 다시 시작하세요."
                    : "Play 모드가 아닙니다 — PlayerPrefs 만 삭제됩니다.\nFirestore 까지 초기화하려면 Play 진입 후 다시 실행하세요."),
                "초기화", "취소"))
            {
                return;
            }

            ResetPlayerPrefs();

            if (EditorApplication.isPlaying)
            {
                ResetRuntimeManagersFromPlayMode();
            }

            Debug.Log("[BalloonFlow] 유저 데이터 초기화 완료");
            EditorUtility.DisplayDialog("완료",
                "유저 데이터가 초기화되었습니다.\n\n" +
                (EditorApplication.isPlaying
                    ? "Play 를 중지하고 다시 시작하세요 (in-memory 매니저는 자동 reset 안 됨)."
                    : "PlayerPrefs 만 삭제됨. Firestore /users/{uid} 는 그대로 남아있어, " +
                      "다음 Play 시 동일 UID 로 로드되면 데이터가 복원됩니다.\n" +
                      "완전 초기화를 원하면 Play 모드 진입 후 이 메뉴를 한 번 더 실행하세요."),
                "OK");
        }

        private static void ResetPlayerPrefs()
        {
            // 키 enumerate 가 Unity API 에 없음 — DeleteAll 로 일괄 처리.
            // 부작용: BalloonFlow 외 다른 PlayerPrefs key 도 함께 삭제됨 (Editor 한정이라 무해).
            PlayerPrefs.DeleteAll();
            // ROLLBACK_RESET_USERDATA_EXPLICIT_PROGRESS_KEYS_20260619:
            // Keep critical reset truth sources explicit for the same Play session.
            PlayerPrefs.SetInt(LevelManager.PREFS_KEY_HIGHEST_LEVEL, 0);
            PlayerPrefs.SetInt("BalloonFlow_Coins", 0);
            PlayerPrefs.DeleteKey("BF_WS_UnlockPopupShown");
            PlayerPrefs.DeleteKey("BF_WS_RoundPopupShown");
            UserDataService.RequestDebugResetOnNextBoot();
            PlayerPrefs.Save();
            Debug.Log("[BalloonFlow] PlayerPrefs.DeleteAll() 완료");
        }

        /// <summary>Play 중일 때 실행. FirebaseManager + UserDataService 가 살아있다고 가정하고 /users/{uid} doc 삭제.</summary>
        private static void ResetRuntimeManagersFromPlayMode()
        {
            if (UserDataService.HasInstance)
                UserDataService.Instance.ResetCurrentUserDataForDebug(0);
            else
                Debug.LogWarning("[BalloonFlow] UserDataService not ready - Firestore reset skipped");

            if (CurrencyManager.HasInstance)
            {
                CurrencyManager.Instance.ResetForDebugWipe();
                // ROLLBACK_RESET_INMEMORY_UNCONDITIONAL_20260619: _user 리셋(coins=initial) 후 캐시 재동기화 +
                //   OnCoinChanged 재발행 → HUD 골드 라벨 즉시 갱신(잔존 골드 방지).
                CurrencyManager.Instance.RefreshFromUserDataCache();
            }

            if (LifeManager.HasInstance)
                LifeManager.Instance.ResetToInitial();

            // ROLLBACK_WS_DISPLAY_ROOT_HIDE_20260619: WinningStreak in-memory pending(FX/연출 큐) 정리 + OnStateChanged
            //   발행 → 로비 RefreshWinningStreakVisibility 가 게이지 root/버튼을 숨김(레벨 진행도 0 → 미해금).
            //   State(winningStreak)·highestClearedLevel 은 CreateNewUser + PlayerPrefs.DeleteAll 로 이미 0 으로 리셋됨.
            if (WinningStreakManager.HasInstance)
                WinningStreakManager.Instance.ResetForDebug();
        }

        private static async void TryDeleteFirestoreUserDocFromPlayMode()
        {
            if (!FirebaseManager.HasInstance)
            {
                Debug.LogWarning("[BalloonFlow] FirebaseManager 미준비 — Firestore 삭제 skip");
                return;
            }

            string uid = FirebaseManager.Instance.UserId;
            if (string.IsNullOrEmpty(uid))
            {
                Debug.LogWarning("[BalloonFlow] uid 없음 — Firestore 삭제 skip");
                return;
            }

            try
            {
                var db = FirebaseManager.Instance.Db;
                if (db == null)
                {
                    Debug.LogWarning("[BalloonFlow] Firestore 핸들 없음 — 삭제 skip");
                    return;
                }
                await db.Document($"users/{uid}").DeleteAsync();
                Debug.Log($"[BalloonFlow] Firestore /users/{uid} 삭제 완료");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BalloonFlow] Firestore 삭제 실패: {e.Message}");
            }
        }

    }
}
#endif
