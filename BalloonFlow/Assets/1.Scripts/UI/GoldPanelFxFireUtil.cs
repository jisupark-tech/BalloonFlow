using UnityEngine;

namespace BalloonFlow
{
    // GoldPanel/FXFire 는 골드 획득 연출(PlayGoldPanelFxFire) 외에는 항상 정지 — 팝업/UI 진입 시 prefab Play-On-Awake 자동 발화 차단.
    /// <remarks>Project-wide convention: flat 'namespace BalloonFlow' — do not nest.</remarks>
    public static class GoldPanelFxFireUtil
    {
        private const float MIN_CANVAS_SCALE_FACTOR = 0.01f;

        public static float GetCanvasScaleCompensation(Transform root)
        {
            // ROLLBACK_UI_EFFECT_CANVAS_SCALE_COMPENSATION_20260624:
            // UI effects under CanvasScaler used to inherit Canvas.scaleFactor directly. On high-scale
            // devices such as Galaxy Fold this makes coin/light effects look larger and brighter.
            // Use the inverse canvas scale for transient FX roots so authored prefab size is visually stable.
            // Rollback: return 1f from this method.
            if (root == null) return 1f;
            Canvas canvas = root.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.scaleFactor <= MIN_CANVAS_SCALE_FACTOR) return 1f;
            return 1f / canvas.scaleFactor;
        }

        public static void ApplyResolutionInvariantScale(Transform root, Vector3 authoredLocalScale)
        {
            if (root == null) return;
            root.localScale = authoredLocalScale * GetCanvasScaleCompensation(root);
        }

        public static void DisableUnderGoldPanel(Transform goldPanel)
        {
            if (goldPanel == null) return;
            DisableNamed(goldPanel, "FXFire");
            DisableNamed(goldPanel, "FxFire");
        }

        public static void DisableUnderLifePanel(Transform lifePanel)
        {
            if (lifePanel == null) return;
            DisableNamed(lifePanel, "FXFire");
            DisableNamed(lifePanel, "FxFire");
        }

        public static void DisableUnderTopBarRoot(Transform popupRoot)
        {
            if (popupRoot == null) return;
            Transform topBar = FindChildRecursive(popupRoot, "TopBarArea");
            Transform gold = topBar != null ? FindChildRecursive(topBar, "GoldPanel") : null;
            DisableUnderGoldPanel(gold);
            Transform life = topBar != null ? FindChildRecursive(topBar, "LifePanel") : null;
            DisableUnderLifePanel(life);
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
