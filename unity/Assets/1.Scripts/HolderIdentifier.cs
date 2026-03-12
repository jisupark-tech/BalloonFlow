using UnityEngine;

namespace BalloonFlow
{
    /// <summary>
    /// Attach to Holder GameObjects to identify them during raycasting.
    /// InputHandler uses Physics.Raycast to find HolderIdentifier components.
    /// </summary>
    /// <remarks>
    /// MUST be in its own file (HolderIdentifier.cs) for Unity prefab serialization.
    /// Unity requires MonoBehaviour class name == file name for script GUID resolution.
    /// </remarks>
    public class HolderIdentifier : MonoBehaviour
    {
        [SerializeField] private int _holderId;

        /// <summary>
        /// The unique identifier for this holder.
        /// </summary>
        public int HolderId => _holderId;

        /// <summary>
        /// Sets the holder ID (used by editor setup).
        /// </summary>
        public void SetHolderId(int id)
        {
            _holderId = id;
        }
    }
}
