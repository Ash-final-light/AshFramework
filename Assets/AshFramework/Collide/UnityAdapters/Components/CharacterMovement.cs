using UnityEngine;

namespace AshFramework.Collide.Unity
{
    public enum CorrectionTiming
    {
        Immediate,
        Deferred
    }

    public enum CorrectionMode
    {
        Snap,
        Gradual
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Character Movement")]
    public sealed class CharacterMovement : MonoBehaviour
    {
        [SerializeField]
        private CollisionBodyBehaviour _body;

        [SerializeField]
        private CollisionLayer _blockingMask = CollisionLayer.WorldSolid;

        [SerializeField]
        [Tooltip("Initial permission to correct position before the manager takes control.")]
        private CorrectionTiming _correctionTiming = CorrectionTiming.Immediate;

        [SerializeField]
        private CorrectionMode _correctionMode = CorrectionMode.Snap;

        [SerializeField]
        [Min(0)]
        [Tooltip("Gradual correction speed in world units per second.")]
        private float _correctionSpeed;

        private bool? _correctionAllowed;

        public CollisionBodyBehaviour Body
        {
            get => _body;
            set
            {
                if (_body != value)
                {
                    _body = value;
                    CorrectionRevision++;
                }
            }
        }

        public CollisionLayer BlockingMask
        {
            get => _blockingMask;
            set => _blockingMask = value;
        }

        /// <summary>Initial correction permission. Runtime changes go through the correction manager.</summary>
        public CorrectionTiming Timing
        {
            get => _correctionTiming;
            set => _correctionTiming = value;
        }

        public bool CanCorrectPosition => _correctionAllowed ?? (_correctionTiming == CorrectionTiming.Immediate);

        public CorrectionMode Mode
        {
            get => _correctionMode;
            set
            {
                if (_correctionMode != value)
                {
                    _correctionMode = value;
                    CorrectionRevision++;
                }
            }
        }

        public float CorrectionSpeed
        {
            get => _correctionSpeed;
            set => _correctionSpeed = value;
        }

        internal int CorrectionRevision
        {
            get;
            private set;
        }

        private void OnDisable()
        {
            CorrectionRevision++;
        }

        internal void SetCorrectionAllowed(bool allowed)
        {
            _correctionAllowed = allowed;
        }

        internal static void WritePosition(CollisionBodyBehaviour body, Vector3 position)
        {
            body.transform.position = position;
        }
    }
}
