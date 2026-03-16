using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BalloonFlow
{
    /// <summary>
    /// First Time User Experience (FTUE) visual manager.
    /// Listens to OnTutorialStepChanged and drives all tutorial UI elements:
    /// dim overlay, highlight frame, directional arrow, and instruction panel.
    /// Contains no game logic — purely cosmetic guidance layer.
    /// </summary>
    /// <remarks>
    /// Layer: Game | Genre: Puzzle | Role: Manager | Phase: 2
    /// DB Reference: No DB match — generated from logicFlow (ux_pages_tutorial)
    /// requires: UIManager (overlay management via SerializeField canvas references)
    /// </remarks>
    public class TutorialManager : SceneSingleton<TutorialManager>
    {
        #region Constants

        private const float DIM_ALPHA = 0.7f;
        private const float FADE_DURATION = 0.25f;
        private const float ARROW_BOB_AMPLITUDE = 10f;
        private const float ARROW_BOB_FREQUENCY = 2f;

        #endregion

        #region Serialized Fields

        [Header("Dim Overlay")]
        [SerializeField] private CanvasGroup _dimOverlay;

        [Header("Highlight Frame")]
        [SerializeField] private RectTransform _highlightFrame;

        [Header("Arrow Indicator")]
        [SerializeField] private RectTransform _arrowIndicator;
        [SerializeField] private Image _arrowImage;

        [Header("Instruction Panel")]
        [SerializeField] private GameObject _instructionPanel;
        [SerializeField] private Text _instructionText;
        [SerializeField] private Text _instructionTextLegacy;   // fallback if TMP not available

        [Header("Highlight Targets")]
        [Tooltip("Map holder index to RectTransform for highlight positioning.")]
        [SerializeField] private RectTransform[] _holderHighlightAnchors;

        [Tooltip("Fallback RectTransform used when a named target is not in the anchor array.")]
        [SerializeField] private RectTransform _boardHighlightAnchor;

        #endregion

        #region Fields

        private Coroutine _dimFadeCoroutine;
        private Coroutine _arrowBobCoroutine;
        private bool _isDimActive;

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            // Ensure all visual elements start hidden
            HideAllVisuals();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnTutorialStepChanged>(HandleTutorialStepChanged);
            EventBus.Subscribe<OnTutorialCompleted>(HandleTutorialCompleted);
            EventBus.Subscribe<OnTutorialStarted>(HandleTutorialStarted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnTutorialStepChanged>(HandleTutorialStepChanged);
            EventBus.Unsubscribe<OnTutorialCompleted>(HandleTutorialCompleted);
            EventBus.Unsubscribe<OnTutorialStarted>(HandleTutorialStarted);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows the highlight frame around the element identified by targetId.
        /// targetId format: "holder_0", "holder_1", "board", "gimmick_hidden", etc.
        /// Also displays the message in the instruction panel.
        /// </summary>
        /// <param name="targetId">Identifier string for the element to highlight.</param>
        /// <param name="message">Instruction text to display.</param>
        public void ShowHighlight(string targetId, string message)
        {
            RectTransform anchor = ResolveHighlightAnchor(targetId);

            if (_highlightFrame != null)
            {
                _highlightFrame.gameObject.SetActive(anchor != null);

                if (anchor != null)
                {
                    // Snap highlight frame to target's position and size
                    _highlightFrame.position = anchor.position;
                    _highlightFrame.sizeDelta = anchor.sizeDelta + new Vector2(16f, 16f);
                }
            }

            ShowInstructionPanel(message);
        }

        /// <summary>
        /// Hides the highlight frame.
        /// </summary>
        public void HideHighlight()
        {
            if (_highlightFrame != null)
            {
                _highlightFrame.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Shows the arrow indicator at the given world/canvas position pointing in direction.
        /// </summary>
        /// <param name="position">Canvas position for the arrow.</param>
        /// <param name="direction">Direction the arrow should face (normalized).</param>
        public void ShowArrow(Vector2 position, Vector2 direction)
        {
            if (_arrowIndicator == null)
            {
                return;
            }

            _arrowIndicator.gameObject.SetActive(true);
            _arrowIndicator.anchoredPosition = position;

            // Rotate arrow to face the direction
            if (direction != Vector2.zero)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                _arrowIndicator.localEulerAngles = new Vector3(0f, 0f, angle);
            }

            // Start bob animation
            if (_arrowBobCoroutine != null)
            {
                StopCoroutine(_arrowBobCoroutine);
            }
            _arrowBobCoroutine = StartCoroutine(BobArrowCoroutine(position, direction));
        }

        /// <summary>
        /// Hides the arrow indicator and stops its animation.
        /// </summary>
        public void HideArrow()
        {
            if (_arrowBobCoroutine != null)
            {
                StopCoroutine(_arrowBobCoroutine);
                _arrowBobCoroutine = null;
            }

            if (_arrowIndicator != null)
            {
                _arrowIndicator.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Enables or disables the dim overlay that darkens the screen.
        /// </summary>
        /// <param name="active">True to show the dim overlay, false to hide it.</param>
        public void SetDimOverlay(bool active)
        {
            if (_dimOverlay == null)
            {
                return;
            }

            _isDimActive = active;

            if (_dimFadeCoroutine != null)
            {
                StopCoroutine(_dimFadeCoroutine);
            }

            float targetAlpha = active ? DIM_ALPHA : 0f;
            _dimFadeCoroutine = StartCoroutine(FadeDimCoroutine(targetAlpha));

            _dimOverlay.blocksRaycasts = active;
            _dimOverlay.interactable = false;
        }

        /// <summary>
        /// Shows the instruction panel with the given text.
        /// </summary>
        /// <param name="text">Instruction text to display.</param>
        public void ShowInstructionPanel(string text)
        {
            if (_instructionPanel != null)
            {
                _instructionPanel.SetActive(true);
            }

            SetInstructionText(text);
        }

        /// <summary>
        /// Hides the instruction panel.
        /// </summary>
        public void HideInstructionPanel()
        {
            if (_instructionPanel != null)
            {
                _instructionPanel.SetActive(false);
            }
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleTutorialStarted(OnTutorialStarted evt)
        {
            // Activate dim overlay when tutorial begins
            SetDimOverlay(true);
        }

        private void HandleTutorialStepChanged(OnTutorialStepChanged evt)
        {
            // The instruction is in the event; the highlight target comes from
            // TutorialController's GetCurrentStep(). We read it here.
            string highlightTarget = string.Empty;

            if (TutorialController.HasInstance)
            {
                TutorialStep step = TutorialController.Instance.GetCurrentStep();
                if (step != null)
                {
                    highlightTarget = step.highlightTarget ?? string.Empty;

                    // Position arrow toward highlight target if one exists
                    if (!string.IsNullOrEmpty(highlightTarget))
                    {
                        RectTransform anchor = ResolveHighlightAnchor(highlightTarget);
                        if (anchor != null)
                        {
                            // Arrow points downward into the highlighted target from above
                            ShowArrow(
                                (Vector2)anchor.anchoredPosition + new Vector2(0f, 80f),
                                Vector2.down);
                        }
                        else
                        {
                            HideArrow();
                        }
                    }
                    else
                    {
                        HideArrow();
                    }
                }
            }

            ShowHighlight(highlightTarget, evt.instruction);
        }

        private void HandleTutorialCompleted(OnTutorialCompleted evt)
        {
            HideAllVisuals();
        }

        #endregion

        #region Private Methods — Utilities

        /// <summary>
        /// Resolves a target ID string to a RectTransform for highlight placement.
        /// </summary>
        private RectTransform ResolveHighlightAnchor(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            // "holder_N" pattern
            if (targetId.StartsWith("holder_"))
            {
                string indexStr = targetId.Substring("holder_".Length);
                if (int.TryParse(indexStr, out int index))
                {
                    if (_holderHighlightAnchors != null && index >= 0 && index < _holderHighlightAnchors.Length)
                    {
                        return _holderHighlightAnchors[index];
                    }
                }
                return null;
            }

            // Named targets
            if (targetId == "board" || targetId == "holder_queue")
            {
                return _boardHighlightAnchor;
            }

            // Gimmick targets — also use board anchor as a general fallback
            if (targetId.StartsWith("gimmick_"))
            {
                return _boardHighlightAnchor;
            }

            return null;
        }

        private void SetInstructionText(string text)
        {
            if (_instructionText != null)
            {
                _instructionText.text = text ?? string.Empty;
            }
            else if (_instructionTextLegacy != null)
            {
                _instructionTextLegacy.text = text ?? string.Empty;
            }
        }

        private void HideAllVisuals()
        {
            HideHighlight();
            HideArrow();
            HideInstructionPanel();

            if (_dimOverlay != null)
            {
                _dimOverlay.alpha = 0f;
                _dimOverlay.blocksRaycasts = false;
                _dimOverlay.interactable = false;
            }

            _isDimActive = false;
        }

        #endregion

        #region Coroutines

        private IEnumerator FadeDimCoroutine(float targetAlpha)
        {
            if (_dimOverlay == null)
            {
                yield break;
            }

            float startAlpha = _dimOverlay.alpha;
            float elapsed = 0f;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_DURATION);
                _dimOverlay.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            _dimOverlay.alpha = targetAlpha;
            _dimFadeCoroutine = null;
        }

        private IEnumerator BobArrowCoroutine(Vector2 basePosition, Vector2 direction)
        {
            if (_arrowIndicator == null)
            {
                yield break;
            }

            float elapsed = 0f;

            while (_arrowIndicator.gameObject.activeSelf)
            {
                elapsed += Time.deltaTime;
                float offset = Mathf.Sin(elapsed * Mathf.PI * ARROW_BOB_FREQUENCY) * ARROW_BOB_AMPLITUDE;
                _arrowIndicator.anchoredPosition = basePosition + direction.normalized * offset;
                yield return null;
            }
        }

        #endregion
    }
}
