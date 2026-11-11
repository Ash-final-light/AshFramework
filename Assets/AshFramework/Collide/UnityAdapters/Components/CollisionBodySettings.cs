using System;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    [Flags]
    public enum CollisionLayer
    {
        None = 0,
        Character = 1 << 0,
        WorldSolid = 1 << 1,
        Trigger = 1 << 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CollisionBodyBehaviour))]
    [AddComponentMenu("AshFramework/Collide/Collision Body Settings")]
    public sealed class CollisionBodySettings : MonoBehaviour
    {
        [SerializeField]
        private CollisionLayer _collisionLayer;

        public CollisionBodyBehaviour Body => GetComponent<CollisionBodyBehaviour>();

        public CollisionLayer Layer
        {
            get => _collisionLayer;
            set => _collisionLayer = value;
        }
    }
}
