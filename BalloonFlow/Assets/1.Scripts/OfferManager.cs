using System;
using System.Collections.Generic;
using UnityEngine;

namespace BalloonFlow
{
    // ─────────────────────────────────────────────────────────────────
    // Data class
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Describes a timed shop offer.</summary>
    [System.Serializable]
    public class Offer
    {
        public string offerId;
        public string offerType;        // "discount" | "bundle" | "booster"
        public string displayName;
        public string description;
        public int    originalPrice;
        public int    discountedPrice;
        public float  durationSeconds;
        public bool   isActive;

        // Runtime-only (not serialized in PlayerPrefs directly — stored as ticks long)
        [NonSerialized] public DateTime expiresAt;
    }

    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages timed offers and product-exposure policy.
    /// Triggers offers on level completion, board failure (declined continue), and shop open.
    /// Enforces a 20-minute cooldown between offer popups and a 3-per-session cap.
    /// </summary>
    /// <remarks>
    /// Layer: Domain | Genre: Puzzle | Role: Manager | Phase: 3
    /// domain_owner: BM
    /// DB Reference: No DB match — generated from L3 YAML logicFlow
    /// </remarks>
    public class OfferManager : Singleton<OfferManager>
    {
        #region Constants

        private const float  OfferCooldownSeconds  = 1200f;  // 20 minutes
        private const int    MaxOffersPerSession   = 3;
        private const string PrefsLastOfferTimeTicks = "BalloonFlow_LastOfferTicks";
        private const string PopupOfferSuffix       = "popup_offer_";

        #endregion

        #region Fields

        private readonly List<Offer> _offerCatalogue = new List<Offer>();
        private readonly List<Offer> _activeOffers   = new List<Offer>();

        private int   _offersShownThisSession;
        private float _lastOfferRealtime;   // Time.realtimeSinceStartup at last shown offer

        #endregion

        #region Lifecycle

        protected override void OnSingletonAwake()
        {
            _offersShownThisSession = 0;
            _lastOfferRealtime      = -OfferCooldownSeconds; // allow immediate show at startup

            BuildCatalogue();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Subscribe<OnBoardFailed>(HandleBoardFailed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OnLevelCompleted>(HandleLevelCompleted);
            EventBus.Unsubscribe<OnBoardFailed>(HandleBoardFailed);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns all currently active (not expired, not dismissed) offers.
        /// </summary>
        public Offer[] GetActiveOffers()
        {
            PurgeExpiredOffers();
            return _activeOffers.ToArray();
        }

        /// <summary>
        /// Shows an offer popup by ID (if available and policy allows).
        /// </summary>
        /// <param name="offerId">Offer identifier.</param>
        public void ShowOffer(string offerId)
        {
            if (!IsCooldownElapsed())
            {
                Debug.Log("[OfferManager] ShowOffer blocked by cooldown.");
                return;
            }

            if (_offersShownThisSession >= MaxOffersPerSession)
            {
                Debug.Log("[OfferManager] Session offer cap reached.");
                return;
            }

            Offer offer = FindOfferInCatalogue(offerId);
            if (offer == null)
            {
                Debug.LogWarning($"[OfferManager] Offer not found: {offerId}");
                return;
            }

            ActivateOffer(offer);

            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ShowPopup(PopupOfferSuffix + offerId, priority: 30);
            }

            _lastOfferRealtime = Time.realtimeSinceStartup;
            _offersShownThisSession++;

            Debug.Log($"[OfferManager] Offer shown: {offerId}");
        }

        /// <summary>
        /// Dismisses an active offer without purchasing.
        /// </summary>
        /// <param name="offerId">Offer identifier.</param>
        public void DismissOffer(string offerId)
        {
            Offer offer = FindActiveOffer(offerId);
            if (offer == null) return;

            offer.isActive = false;
            _activeOffers.Remove(offer);

            if (PopupManager.HasInstance)
            {
                PopupManager.Instance.ClosePopup(PopupOfferSuffix + offerId);
            }

            Debug.Log($"[OfferManager] Offer dismissed: {offerId}");
        }

        /// <summary>
        /// Returns true if the offer is currently available (in catalogue and not active/expired).
        /// </summary>
        /// <param name="offerId">Offer identifier.</param>
        public bool IsOfferAvailable(string offerId)
        {
            if (FindActiveOffer(offerId) != null) return false; // already shown
            return FindOfferInCatalogue(offerId) != null;
        }

        /// <summary>
        /// Returns the remaining time for an active offer.
        /// Returns TimeSpan.Zero if the offer is not active or has expired.
        /// </summary>
        /// <param name="offerId">Offer identifier.</param>
        public TimeSpan GetTimeRemaining(string offerId)
        {
            Offer offer = FindActiveOffer(offerId);
            if (offer == null) return TimeSpan.Zero;

            TimeSpan remaining = offer.expiresAt - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        #endregion

        #region Private Methods — Catalogue

        private void BuildCatalogue()
        {
            _offerCatalogue.Clear();

            _offerCatalogue.Add(new Offer
            {
                offerId         = "offer_discount_coins",
                offerType       = "discount",
                displayName     = "Coin Bonanza",
                description     = "Double coins on your next purchase!",
                originalPrice   = 499,
                discountedPrice = 249,
                durationSeconds = 3600f,  // 1 hour
                isActive        = false
            });

            _offerCatalogue.Add(new Offer
            {
                offerId         = "offer_bundle_starter",
                offerType       = "bundle",
                displayName     = "Comeback Bundle",
                description     = "Coins + Shuffle + Hand booster",
                originalPrice   = 999,
                discountedPrice = 599,
                durationSeconds = 7200f,  // 2 hours
                isActive        = false
            });

            _offerCatalogue.Add(new Offer
            {
                offerId         = "offer_booster_shuffle",
                offerType       = "booster",
                displayName     = "Shuffle Sale",
                description     = "3x Shuffle booster at half price",
                originalPrice   = 4500,
                discountedPrice = 2250,
                durationSeconds = 1800f,  // 30 minutes
                isActive        = false
            });

            _offerCatalogue.Add(new Offer
            {
                offerId         = "offer_booster_color",
                offerType       = "booster",
                displayName     = "Color Burst Sale",
                description     = "Color Remove booster, limited offer",
                originalPrice   = 2900,
                discountedPrice = 1450,
                durationSeconds = 1800f,
                isActive        = false
            });
        }

        #endregion

        #region Private Methods — Offer Management

        private void ActivateOffer(Offer offer)
        {
            // Clone for active tracking so catalogue remains pristine.
            Offer active = new Offer
            {
                offerId         = offer.offerId,
                offerType       = offer.offerType,
                displayName     = offer.displayName,
                description     = offer.description,
                originalPrice   = offer.originalPrice,
                discountedPrice = offer.discountedPrice,
                durationSeconds = offer.durationSeconds,
                isActive        = true,
                expiresAt       = DateTime.UtcNow.AddSeconds(offer.durationSeconds)
            };

            _activeOffers.Add(active);
        }

        private void PurgeExpiredOffers()
        {
            _activeOffers.RemoveAll(o => !o.isActive || DateTime.UtcNow >= o.expiresAt);
        }

        private bool IsCooldownElapsed()
        {
            return (Time.realtimeSinceStartup - _lastOfferRealtime) >= OfferCooldownSeconds;
        }

        private Offer FindOfferInCatalogue(string offerId)
        {
            if (string.IsNullOrEmpty(offerId)) return null;
            foreach (var o in _offerCatalogue)
            {
                if (o.offerId == offerId) return o;
            }
            return null;
        }

        private Offer FindActiveOffer(string offerId)
        {
            if (string.IsNullOrEmpty(offerId)) return null;
            foreach (var o in _activeOffers)
            {
                if (o.offerId == offerId) return o;
            }
            return null;
        }

        #endregion

        #region Private Methods — Event Handlers

        private void HandleLevelCompleted(OnLevelCompleted evt)
        {
            // Celebration offer: show bundle after level clear.
            if (IsOfferAvailable("offer_bundle_starter"))
            {
                ShowOffer("offer_bundle_starter");
            }
        }

        private void HandleBoardFailed(OnBoardFailed evt)
        {
            // Offer appears when continue popup would be declined or max continues reached.
            // Triggered after a short delay to let the continue popup surface first.
            // If continue is still available, the offer shows as a secondary suggestion.
            if (IsOfferAvailable("offer_booster_shuffle"))
            {
                ShowOffer("offer_booster_shuffle");
            }
            else if (IsOfferAvailable("offer_discount_coins"))
            {
                ShowOffer("offer_discount_coins");
            }
        }

        #endregion
    }
}
