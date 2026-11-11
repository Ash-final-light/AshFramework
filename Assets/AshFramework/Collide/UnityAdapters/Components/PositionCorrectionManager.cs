using System;
using System.Collections.Generic;
using AshFramework.Math;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    /// <summary>显式执行单角色对固定障碍的一次位置修正，并发布最终 Step 的结果。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Position Correction Manager")]
    public sealed class PositionCorrectionManager : MonoBehaviour
    {
        [SerializeField]
        private CollisionWorldBehaviour _world;

        [SerializeField]
        private CollisionBodySettings[] _bodySettings = Array.Empty<CollisionBodySettings>();

        private readonly Dictionary<CollisionBodyId, CollisionLayer> _layers = new Dictionary<CollisionBodyId, CollisionLayer>();
        private bool _processing;

        public CollisionWorldBehaviour World => _world;

        public void Configure(CollisionWorldBehaviour world, CollisionBodySettings[] bodySettings)
        {
            if (_processing)
            {
                throw new InvalidOperationException("Cannot configure the correction manager during a move.");
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            _bodySettings = bodySettings != null ? (CollisionBodySettings[])bodySettings.Clone()
                : throw new ArgumentNullException(nameof(bodySettings));
            _world = world;
        }

        /// <summary>失败时恢复请求前的位置并清空结果。下一次查询通过 Step 重新同步。</summary>
        public bool TryMoveAndCorrect(CharacterMovement character, Vector3 targetPosition,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out string error)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }

            if (contacts == null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }

            if (_processing)
            {
                throw new InvalidOperationException("Nested position correction is forbidden.");
            }

            pairs.Clear();
            contacts.Clear();
            if (!isActiveAndEnabled || _world == null || !_world.isActiveAndEnabled || !_world.IsInitialized)
            {
                error = "Position correction requires an active manager and an initialized world.";
                return false;
            }

            CollisionBodyBehaviour body = character == null ? null : character.Body;
            if (body == null || !character.isActiveAndEnabled || body.World != _world
                || !_world.TryGetBodyId(body, out CollisionBodyId characterId))
            {
                error = "Character must have a body registered in the correction world.";
                return false;
            }

            if (character.Timing != CorrectionTiming.Immediate || character.Mode != CorrectionMode.Snap)
            {
                error = "Only Immediate + Snap position correction is currently supported.";
                return false;
            }

            if (!ShapeGeometryPreparation.IsFinite(targetPosition.x) || !ShapeGeometryPreparation.IsFinite(targetPosition.y)
                || !ShapeGeometryPreparation.IsFinite(targetPosition.z))
            {
                error = "Character target position must be finite.";
                return false;
            }

            Vector3 previousPosition = body.transform.position;
            CollisionLayer blockingMask = character.BlockingMask;
            bool succeeded = false;
            _processing = true;
            try
            {
                CharacterMovement.WritePosition(body, targetPosition);
                if (!StepForCharacter(body, pairs, contacts, out error))
                {
                    return false;
                }

                RefreshLayers();
                FixVector2 displacement = GetCorrection(characterId, blockingMask, contacts);
                if (displacement != FixVector2.Zero)
                {
                    Vector2 offset = displacement.ToUnityVector();
                    Vector3 worldOffset = _world.Plane == CollisionPlane.XY
                        ? new Vector3(offset.x, offset.y, 0) : new Vector3(offset.x, 0, offset.y);
                    CharacterMovement.WritePosition(body, body.transform.position + worldOffset);
                    if (!StepForCharacter(body, pairs, contacts, out error))
                    {
                        return false;
                    }
                }

                succeeded = true;
                return true;
            }
            finally
            {
                _processing = false;
                if (!succeeded)
                {
                    pairs.Clear();
                    contacts.Clear();
                    if (body != null)
                    {
                        CharacterMovement.WritePosition(body, previousPosition);
                    }
                }
            }
        }

        private bool StepForCharacter(CollisionBodyBehaviour body, List<CollisionPair> pairs,
            List<CollisionContact> contacts, out string error)
        {
            if (!_world.Step(pairs, contacts, out error))
            {
                return false;
            }

            if (!_world.IsBodyActive(body))
            {
                error = _world.TryGetBodyId(body, out _)
                    ? "Character did not participate in Step: " + _world.GetBodyError(body)
                    : "Character is no longer registered in the correction world.";
                return false;
            }

            return true;
        }

        private void RefreshLayers()
        {
            _layers.Clear();
            foreach (CollisionBodySettings settings in _bodySettings)
            {
                if (settings == null || !settings.isActiveAndEnabled)
                {
                    continue;
                }

                CollisionBodyBehaviour body = settings.Body;
                if (body != null && body.World == _world && _world.TryGetBodyId(body, out CollisionBodyId id))
                {
                    _layers[id] = settings.Layer;
                }
            }
        }

        private FixVector2 GetCorrection(CollisionBodyId character, CollisionLayer blockingMask,
            List<CollisionContact> contacts)
        {
            Fix64 depth = Fix64.Zero;
            FixVector2 direction = FixVector2.Zero;
            foreach (CollisionContact contact in contacts)
            {
                bool characterIsA = contact.Pair.A == character;
                if (!characterIsA && contact.Pair.B != character)
                {
                    continue;
                }

                CollisionBodyId other = characterIsA ? contact.Pair.B : contact.Pair.A;
                Contact2D geometry = contact.Shapes.Contact;
                if (geometry.Depth > depth && _layers.TryGetValue(other, out CollisionLayer layer)
                    && (layer & blockingMask) != CollisionLayer.None)
                {
                    depth = geometry.Depth;
                    direction = characterIsA ? -geometry.Normal : geometry.Normal;
                }
            }

            return direction * depth;
        }
    }
}
