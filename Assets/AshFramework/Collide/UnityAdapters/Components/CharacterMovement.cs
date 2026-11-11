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
        private CorrectionTiming _correctionTiming = CorrectionTiming.Immediate;

        [SerializeField]
        private CorrectionMode _correctionMode = CorrectionMode.Snap;

        [SerializeField]
        [Min(0)]
        [Tooltip("Speed reserved for gradual correction, in world units per second.")]
        private float _correctionSpeed;

        public CollisionBodyBehaviour Body
        {
            get => _body;
            set => _body = value;
        }

        public CollisionLayer BlockingMask
        {
            get => _blockingMask;
            set => _blockingMask = value;
        }

        public CorrectionTiming Timing
        {
            get => _correctionTiming;
            set => _correctionTiming = value;
        }

        public CorrectionMode Mode
        {
            get => _correctionMode;
            set => _correctionMode = value;
        }

        public float CorrectionSpeed
        {
            get => _correctionSpeed;
            set => _correctionSpeed = value;
        }

        internal static void WritePosition(CollisionBodyBehaviour body, Vector3 position)
        {
            body.transform.position = position;
        }
    }
}
