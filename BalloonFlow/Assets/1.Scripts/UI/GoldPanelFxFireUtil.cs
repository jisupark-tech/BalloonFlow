using UnityEngine;

namespace BalloonFlow
{
    // GoldPanel/FXFire 는 골드 획득 연출(PlayGoldPanelFxFire) 외에는 항상 정지 — 팝업/UI 진입 시 prefab Play-On-Awake 자동 발화 차단.
    /// <remarks>Project-wide convention: flat 'namespace BalloonFlow' — do not nest.</remarks>
    public static class GoldPanelFxFireUtil
    {
        public static void DisableUnderGoldPanel(Transform goldPanel)
        {
            if (goldPanel == null) return;
            DisableNamed(goldPanel, "FXFire");
            DisableNamed(goldPanel, "FxFire");
        }

        public static void DisableUnderTopBarRoot(Transform popupRoot)
        {
            if (popupRoot == null) return;
            Transform topBar = FindChildRecursive(popupRoot, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            DisableUnderGoldPanel(gold);
        }

        private static void DisableNamed(Transform root, string fxName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].name != fxName) continue;
                var systems = all[i].GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < systems.Length; j++)
                    systems[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (all[i].gameObject.activeSelf) all[i].gameObject.SetActive(false);
            }
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName) return child;
                Transform deep = FindChildRecursive(child, childName);
                if (deep != null) return deep;
            }
            return null;
        }
    }
}
