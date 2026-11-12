using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AshFramework.Math;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    /// <summary>显式迭代修正单角色与固定障碍的穿透，并发布最终 Step 的结果。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Position Correction Manager")]
    public sealed class PositionCorrectionManager : MonoBehaviour
    {
        [SerializeField]
        private CollisionWorldBehaviour _world;

        [SerializeField]
        private CollisionBodySettings[] _bodySettings = Array.Empty<CollisionBodySettings>();

        [SerializeField]
        [Min(1)]
        private int _maxIterations = 4;

        [SerializeField]
        [Min(0)]
        private float _penetrationTolerance = 0.00001f;

        [SerializeField]
        [Min(1)]
        private int _maxGradualFrames = 60;

        private readonly Dictionary<CollisionBodyId, CollisionLayer> _layers = new Dictionary<CollisionBodyId, CollisionLayer>();
        private readonly ConditionalWeakTable<CollisionBodyBehaviour, GradualProgress> _gradual =
            new ConditionalWeakTable<CollisionBodyBehaviour, GradualProgress>();
        private bool _processing;

        private sealed class GradualProgress
        {
            internal CollisionBodyId BodyId;
            internal CharacterMovement Character;
            internal int CharacterRevision;
            internal int BodyRevision;
            internal long LastFrame = -1;
            internal int ProcessedFrames;
            internal bool Active;

            internal void Stop()
            {
                Active = false;
                ProcessedFrames = 0;
            }
        }

        public CollisionWorldBehaviour World => _world;

        public int MaxIterations
        {
            get => _maxIterations;
            set => _maxIterations = value;
        }

        public float PenetrationTolerance
        {
            get => _penetrationTolerance;
            set => _penetrationTolerance = value;
        }

        public int MaxGradualFrames
        {
            get => _maxGradualFrames;
            set => _maxGradualFrames = value;
        }

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
            return TryMoveAndCorrect(character, targetPosition, pairs, contacts, out _, out error);
        }

        /// <summary>返回 true 表示最终 Step 有效。result 说明穿透是否降至容差内。</summary>
        public bool TryMoveAndCorrect(CharacterMovement character, Vector3 targetPosition,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out PositionCorrectionResult result, out string error)
        {
            EnsureOperationAllowed(pairs, contacts);
            pairs.Clear();
            contacts.Clear();
            result = default;
            if (!TryGetRegisteredCharacter(character, out CollisionBodyBehaviour body, out CollisionBodyId characterId, out error))
            {
                return false;
            }

            if (!IsFinitePosition(targetPosition))
            {
                error = "Character target position must be finite.";
                return false;
            }

            bool allowed = character.CanCorrectPosition;
            if (!TrySnap(character, body, characterId, targetPosition, allowed, pairs, contacts, out result, out error))
            {
                return false;
            }

            character.SetCorrectionAllowed(allowed);
            return true;
        }

        /// <summary>Moves the character and consumes at most one gradual correction budget for the given logic frame.</summary>
        public bool TryMoveAndCorrect(CharacterMovement character, Vector3 targetPosition, long logicalFrame, double fixedDeltaTime,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out PositionCorrectionResult result, out string error)
        {
            if (character != null && character.Mode == CorrectionMode.Snap)
            {
                return TryMoveAndCorrect(character, targetPosition, pairs, contacts, out result, out error);
            }

            return TryProcessGradual(character, targetPosition, false, logicalFrame, fixedDeltaTime, pairs, contacts, out result, out error);
        }

        /// <summary>
        /// Enabling checks and corrects the current position before returning. Closing or repeating the setting
        /// leaves the collision buffers untouched and returns a default result because no Step was run.
        /// </summary>
        public bool TrySetCorrectionAllowed(CharacterMovement character, bool allowed,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out PositionCorrectionResult result, out string error)
        {
            EnsureOperationAllowed(pairs, contacts);
            result = default;
            if (!TryGetRegisteredCharacter(character, out CollisionBodyBehaviour body, out CollisionBodyId characterId, out error))
            {
                pairs.Clear();
                contacts.Clear();
                return false;
            }

            if (!allowed || character.CanCorrectPosition)
            {
                character.SetCorrectionAllowed(allowed);
                if (!allowed && _gradual.TryGetValue(body, out GradualProgress progress))
                {
                    progress.Stop();
                }

                return true;
            }

            pairs.Clear();
            contacts.Clear();
            if (!TrySnap(character, body, characterId, null, true, pairs, contacts, out result, out error))
            {
                return false;
            }

            character.SetCorrectionAllowed(true);
            return true;
        }

        /// <summary>Enabling gradual correction processes the current frame before returning, subject to that frame's budget.</summary>
        public bool TrySetCorrectionAllowed(CharacterMovement character, bool allowed, long logicalFrame, double fixedDeltaTime,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out PositionCorrectionResult result, out string error)
        {
            if (!allowed || character == null || character.CanCorrectPosition || character.Mode != CorrectionMode.Gradual)
            {
                return TrySetCorrectionAllowed(character, allowed, pairs, contacts, out result, out error);
            }

            return TryProcessGradual(character, null, true, logicalFrame, fixedDeltaTime, pairs, contacts, out result, out error);
        }

        /// <summary>Continues an active correction. A duplicate frame or inactive correction leaves the buffers untouched.</summary>
        public bool TryContinueGradualCorrection(CharacterMovement character, long logicalFrame, double fixedDeltaTime,
            List<CollisionPair> pairs, List<CollisionContact> contacts, out PositionCorrectionResult result, out string error)
        {
            return TryProcessGradual(character, null, false, logicalFrame, fixedDeltaTime, pairs, contacts, out result, out error);
        }

        private bool TryProcessGradual(CharacterMovement character, Vector3? targetPosition, bool enabling, long logicalFrame,
            double fixedDeltaTime, List<CollisionPair> pairs, List<CollisionContact> contacts,
            out PositionCorrectionResult result, out string error)
        {
            EnsureOperationAllowed(pairs, contacts);
            result = default;
            if (!TryGetRegisteredCharacter(character, out CollisionBodyBehaviour body, out CollisionBodyId characterId, out error))
            {
                pairs.Clear();
                contacts.Clear();
                return false;
            }

            GradualProgress progress = _gradual.GetValue(body, _ => new GradualProgress());
            if (progress.BodyId != characterId || progress.Character != character || progress.BodyRevision != body.CorrectionRevision
                || progress.CharacterRevision != character.CorrectionRevision)
            {
                progress.Stop();
                if (progress.BodyId != characterId)
                {
                    progress.LastFrame = -1;
                }

                progress.BodyId = characterId;
                progress.Character = character;
                progress.BodyRevision = body.CorrectionRevision;
                progress.CharacterRevision = character.CorrectionRevision;
            }

            bool allowed = enabling || character.CanCorrectPosition;
            bool frameAlreadyProcessed = logicalFrame == progress.LastFrame;
            if (!enabling && !targetPosition.HasValue && (!allowed || !progress.Active || frameAlreadyProcessed))
            {
                return true;
            }

            pairs.Clear();
            contacts.Clear();
            if (character.Mode != CorrectionMode.Gradual || logicalFrame < 0 || logicalFrame < progress.LastFrame
                || (targetPosition.HasValue && !IsFinitePosition(targetPosition.Value)))
            {
                error = "Gradual correction requires Gradual mode, a nondecreasing logic frame and a finite target position.";
                return false;
            }

            double budget = character.CorrectionSpeed * fixedDeltaTime;
            if (allowed && (!ShapeGeometryPreparation.IsFinite(character.CorrectionSpeed) || character.CorrectionSpeed <= 0
                || !(budget > 0) || double.IsInfinity(budget) || _maxGradualFrames < 1))
            {
                error = "Gradual correction requires positive finite speed and frame duration, and a positive frame limit.";
                return false;
            }

            double frameBudget = frameAlreadyProcessed || progress.ProcessedFrames >= _maxGradualFrames ? 0 : budget;
            if (!TryStepAndCorrect(character, body, characterId, targetPosition, allowed, frameBudget, pairs, contacts, out result, out error))
            {
                progress.Stop();
                return false;
            }

            character.SetCorrectionAllowed(allowed);
            if (!allowed)
            {
                progress.Stop();
                return true;
            }

            if (!frameAlreadyProcessed && progress.ProcessedFrames < _maxGradualFrames)
            {
                progress.LastFrame = logicalFrame;
                progress.ProcessedFrames++;
            }

            PositionCorrectionStatus status = result.Status;
            if (status == PositionCorrectionStatus.InProgress && progress.ProcessedFrames >= _maxGradualFrames)
            {
                status = PositionCorrectionStatus.FrameLimit;
            }

            result = new PositionCorrectionResult(status, result.Iterations, result.RemainingDepth, progress.ProcessedFrames);
            progress.Active = status == PositionCorrectionStatus.InProgress;
            if (!progress.Active)
            {
                progress.Stop();
            }

            return true;
        }

        private static bool IsFinitePosition(Vector3 position)
        {
            return ShapeGeometryPreparation.IsFinite(position.x) && ShapeGeometryPreparation.IsFinite(position.y)
                && ShapeGeometryPreparation.IsFinite(position.z);
        }

        private void EnsureOperationAllowed(List<CollisionPair> pairs, List<CollisionContact> contacts)
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
        }

        private bool TryGetRegisteredCharacter(CharacterMovement character, out CollisionBodyBehaviour body,
            out CollisionBodyId characterId, out string error)
        {
            body = character == null ? null : character.Body;
            characterId = default;
            if (!isActiveAndEnabled || _world == null || !_world.isActiveAndEnabled || !_world.IsInitialized)
            {
                error = "Position correction requires an active manager and an initialized world.";
                return false;
            }

            if (body == null || !character.isActiveAndEnabled || body.World != _world
                || !_world.TryGetBodyId(body, out characterId))
            {
                error = "Character must have a body registered in the correction world.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool TrySnap(CharacterMovement character, CollisionBodyBehaviour body, CollisionBodyId characterId,
            Vector3? targetPosition, bool correct, List<CollisionPair> pairs, List<CollisionContact> contacts,
            out PositionCorrectionResult result, out string error)
        {
            result = default;
            if (character.Mode != CorrectionMode.Snap)
            {
                error = "Only Snap is supported without a logic frame. Use the frame overload for Gradual correction.";
                return false;
            }

            return TryStepAndCorrect(character, body, characterId, targetPosition, correct, null, pairs, contacts, out result, out error);
        }

        private bool TryStepAndCorrect(CharacterMovement character, CollisionBodyBehaviour body, CollisionBodyId characterId,
            Vector3? targetPosition, bool correct, double? budget, List<CollisionPair> pairs, List<CollisionContact> contacts,
            out PositionCorrectionResult result, out string error)
        {
            result = default;
            int maxIterations = _maxIterations;
            if (correct && (maxIterations < 1 || !ShapeGeometryPreparation.IsFinite(_penetrationTolerance)
                || _penetrationTolerance < 0 || _penetrationTolerance >= Fix64.MaxValue.ToDouble()))
            {
                error = "Position correction requires a positive iteration limit and a representable nonnegative tolerance.";
                return false;
            }

            Fix64 tolerance = correct ? _penetrationTolerance.ToFix64() : Fix64.Zero;

            Vector3 previousLocalPosition = body.transform.localPosition;
            CollisionLayer blockingMask = character.BlockingMask;
            bool succeeded = false;
            _processing = true;
            try
            {
                if (targetPosition.HasValue)
                {
                    CharacterMovement.WritePosition(body, targetPosition.Value);
                }

                if (!StepForCharacter(body, pairs, contacts, out error))
                {
                    return false;
                }

                RefreshLayers();
                int iterations = 0;
                PositionCorrectionStatus status = correct ? PositionCorrectionStatus.Resolved : PositionCorrectionStatus.Skipped;
                Fix64 depth = GetDeepestContact(characterId, blockingMask, contacts, out FixVector2 direction);
                double remaining = budget.GetValueOrDefault();
                while (correct && depth > tolerance)
                {
                    if (iterations == maxIterations || (budget.HasValue && remaining <= 0))
                    {
                        status = budget.HasValue ? PositionCorrectionStatus.InProgress : PositionCorrectionStatus.IterationLimit;
                        break;
                    }

                    if (!TryPush(body, direction, depth, budget.HasValue, ref remaining))
                    {
                        status = budget.HasValue && iterations > 0 && remaining < depth.ToDouble()
                            ? PositionCorrectionStatus.InProgress : PositionCorrectionStatus.NoProgress;
                        break;
                    }

                    if (!StepForCharacter(body, pairs, contacts, out error))
                    {
                        return false;
                    }

                    iterations++;
                    depth = GetDeepestContact(characterId, blockingMask, contacts, out direction);
                }

                result = new PositionCorrectionResult(status, iterations, depth);
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
                    if (body != null && !body.transform.localPosition.Equals(previousLocalPosition))
                    {
                        body.transform.localPosition = previousLocalPosition;
                    }
                }
            }
        }

        private bool TryPush(CollisionBodyBehaviour body, FixVector2 direction, Fix64 depth, bool limited, ref double remaining)
        {
            Vector3 start = body.transform.position;
            Fix64 distance = limited && remaining < depth.ToDouble() ? remaining.ToFix64() : depth;
            Vector3 target = GetPushTarget(start, direction, distance);
            if (limited && GetPlanarDistance(start, target) > remaining)
            {
                // Find the largest representable step that fits after rounding to a Unity world position.
                long lower = 0;
                long upper = distance.RawValue;
                while (upper - lower > 1)
                {
                    long middle = lower + (upper - lower) / 2;
                    Vector3 candidate = GetPushTarget(start, direction, Fix64.FromRaw(middle));
                    if (GetPlanarDistance(start, candidate) <= remaining)
                    {
                        lower = middle;
                    }
                    else
                    {
                        upper = middle;
                    }
                }

                target = GetPushTarget(start, direction, Fix64.FromRaw(lower));
            }

            if (target.Equals(start))
            {
                return false;
            }

            Vector3 previousLocalPosition = body.transform.localPosition;
            CharacterMovement.WritePosition(body, target);
            Vector3 actual = body.transform.position;
            if (!limited)
            {
                return !actual.Equals(start);
            }

            double used = GetPlanarDistance(start, actual);
            if (!(used > 0 && used <= remaining))
            {
                body.transform.localPosition = previousLocalPosition;
                return false;
            }

            remaining -= used;
            return true;
        }

        private Vector3 GetPushTarget(Vector3 start, FixVector2 direction, Fix64 distance)
        {
            Vector2 offset = (direction * distance).ToUnityVector();
            return start + (_world.Plane == CollisionPlane.XY ? new Vector3(offset.x, offset.y, 0) : new Vector3(offset.x, 0, offset.y));
        }

        private double GetPlanarDistance(Vector3 start, Vector3 end)
        {
            double x = (double)end.x - start.x;
            double y = _world.Plane == CollisionPlane.XY ? (double)end.y - start.y : (double)end.z - start.z;
            return System.Math.Sqrt(x * x + y * y);
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

        private Fix64 GetDeepestContact(CollisionBodyId character, CollisionLayer blockingMask,
            List<CollisionContact> contacts, out FixVector2 direction)
        {
            Fix64 depth = Fix64.Zero;
            direction = FixVector2.Zero;
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

            return depth;
        }
    }
}
