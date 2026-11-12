using System.Collections.Generic;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Collide.Tests
{
    public sealed class PositionCorrectionTests
    {
        private readonly List<CollisionPair> _pairs = new List<CollisionPair>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();
        private GameObject _root;
        private CollisionWorldBehaviour _world;
        private PositionCorrectionManager _manager;
        private CharacterMovement _character;
        private CollisionBodyBehaviour _wall;
        private CollisionBodyBehaviour _trigger;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [TestCase(CollisionPlane.XY, true)]
        [TestCase(CollisionPlane.XZ, false)]
        public void MovesOnlyCharacterOnceAndReturnsTangentContacts(CollisionPlane plane, bool characterFirst)
        {
            CreateWorld(plane, characterFirst);
            Vector3 wallPosition = _wall.transform.position;
            Vector3 target = Point(1.5f, 0, 2);
            Vector3 corrected = Point(1, 0, 2);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.EqualTo(1));
            Assert.That(_character.Body.transform.position, Is.EqualTo(corrected));
            Assert.That(_wall.transform.position, Is.EqualTo(wallPosition));
            Assert.That(_pairs.Count, Is.EqualTo(1));
            Assert.That(_contacts.Count, Is.EqualTo(2));
            Assert.That(_contacts[0].Shapes.Contact.Depth, Is.EqualTo(Fix64.Zero));
            Assert.That(_contacts[1].Shapes.Contact.Depth, Is.EqualTo(Fix64.Zero));

            Assert.That(_manager.TryMoveAndCorrect(_character, corrected, _pairs, _contacts, out error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(corrected));
        }

        [Test]
        public void TriggerOnlyContactPreservesTargetAndIsReturned()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 target = Point(-2.5f, 0);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
            Assert.That(_pairs, Is.EqualTo(new[] { new CollisionPair(Id(_character.Body), Id(_trigger)) }));
            Assert.That(_contacts.Count, Is.EqualTo(1));
            Assert.That(_contacts[0].Shapes.Contact.Depth, Is.GreaterThan(Fix64.Zero));
        }

        [Test]
        public void DeepTriggerOverlapDoesNotOverrideWallCorrection()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 target = Point(1.5f, 0);
            _trigger.transform.position = target;
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 0)));
            Assert.That(_pairs, Does.Contain(new CollisionPair(Id(_character.Body), Id(_trigger))));
            Assert.That(_pairs, Does.Contain(new CollisionPair(Id(_character.Body), Id(_wall))));
        }

        [Test]
        public void GradualRequiresLogicFrameBeforeMoving()
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Mode = CorrectionMode.Gradual;
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("Only Snap"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void GradualEnablesImmediatelyAndContinuesFromCurrentPosition(CollisionPlane plane)
        {
            CreateWorld(plane, true);
            _character.Mode = CorrectionMode.Gradual;
            _character.Timing = CorrectionTiming.Deferred;
            _character.CorrectionSpeed = 2;
            Vector3 target = Point(1.5f, 0, 2);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Skipped));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Half));

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, 100, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(_character.CanCorrectPosition, Is.True);
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1.25f, 0, 2)));
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That(result.ProcessedFrames, Is.EqualTo(1));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.FromRatio(1, 4)));

            Assert.That(_manager.TryContinueGradualCorrection(_character, 100, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1.25f, 0, 2)));
            Assert.That(_contacts.Count, Is.EqualTo(2));

            Assert.That(_manager.TryContinueGradualCorrection(_character, 101, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.ProcessedFrames, Is.EqualTo(2));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Zero));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 0, 2)));
            Assert.That(_contacts, Has.All.Matches<CollisionContact>(contact => contact.Shapes.Contact.Depth == Fix64.Zero));
        }

        [Test]
        public void GradualCornerSharesOneBudgetAcrossDirections()
        {
            CreateWorld(CollisionPlane.XY, true);
            AddFixedBox("Top wall", Point(0, 3), new Vector2(1.75f, 1));
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 1.5f;

            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.125f, 1.125f), 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Vector3 position = _character.Body.transform.position;
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That(result.Iterations, Is.EqualTo(2));
            Assert.That(position.x + position.y, Is.EqualTo(2.0625f));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.FromRatio(1, 16)));

            Assert.That(_manager.TryContinueGradualCorrection(_character, 1, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 1)));
        }

        [Test]
        public void GradualUsesLatestContactsAndDoesNotRoundPastBudget()
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 5;
            Vector3 target = Point(1.5f, 0);

            Assert.That(_manager.TryMoveAndCorrect(_character, target, 0, 0.02, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Vector3 position = _character.Body.transform.position;
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That((double)target.x - position.x, Is.InRange(0.099999, 0.1));

            _wall.transform.position = Point(5, 0);
            Assert.That(_manager.TryContinueGradualCorrection(_character, 1, 0.02, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(_character.Body.transform.position, Is.EqualTo(position));
            Assert.That(_contacts, Is.Empty);
        }

        [Test]
        public void GradualClosingAndReopeningDoesNotRefillTheSameFrame()
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 2;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Vector3 firstPosition = _character.Body.transform.position;

            Assert.That(_manager.TrySetCorrectionAllowed(_character, false, _pairs, _contacts, out result, out error), Is.True, error);
            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, 0, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.ProcessedFrames, Is.Zero);
            Assert.That(_character.Body.transform.position, Is.EqualTo(firstPosition));
        }

        [Test]
        public void GradualNarrowGapStopsAfterFrameLimit()
        {
            CreateWorld(CollisionPlane.XY, true);
            CollisionBodyBehaviour leftWall = AddFixedBox("Left wall", Point(-1.75f, 0), new Vector2(1, 2));
            AddFixedBox("Right wall", Point(1.75f, 0), new Vector2(1, 2), leftWall.GetComponent<CollisionBodySettings>());
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 2;
            _manager.MaxGradualFrames = 2;

            Assert.That(_manager.TryMoveAndCorrect(_character, Point(0, 0), 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That(_manager.TryContinueGradualCorrection(_character, 1, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.FrameLimit));
            Assert.That(result.ProcessedFrames, Is.EqualTo(2));
            Assert.That(result.RemainingDepth, Is.GreaterThan(Fix64.Zero));
            Vector3 finalPosition = _character.Body.transform.position;

            Assert.That(_manager.TryContinueGradualCorrection(_character, 2, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.Body.transform.position, Is.EqualTo(finalPosition));
        }

        [Test]
        public void GradualStopsWhenTheBudgetCannotMoveTheTransform()
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 1;
            Vector3 target = Point(1.5f, 0);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, 0, 1.0 / Fix64.Scale, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NoProgress));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Half));
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
        }

        [Test]
        public void GradualOpeningFailureKeepsTheGateClosedAndCanRetry()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            _character.Mode = CorrectionMode.Gradual;
            _character.Timing = CorrectionTiming.Deferred;
            _character.CorrectionSpeed = 2;
            Vector3 target = Point(1.5f, 0);
            _character.Body.transform.position = target;
            index.FailOperation = "Query";
            index.FailureDelay = 3;

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(_character.CanCorrectPosition, Is.False);
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
            Assert.That(_contacts, Is.Empty);

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, 0, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            Assert.That(result.ProcessedFrames, Is.EqualTo(1));
            Assert.That(_character.CanCorrectPosition, Is.True);
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1.25f, 0)));
        }

        [Test]
        public void GradualFailureAfterPushRollsBackOnlyCurrentFrame()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 2;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            index.FailOperation = "Query";
            index.FailureDelay = 3;

            Assert.That(_manager.TryContinueGradualCorrection(_character, 1, 0.125, _pairs, _contacts,
                out result, out error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1.25f, 0)));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
            Assert.That(_manager.TryContinueGradualCorrection(_character, 2, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
        }

        [Test]
        public void GradualDoesNotResumeAfterBodyWasDisabledAndEnabledBetweenFrames()
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Mode = CorrectionMode.Gradual;
            _character.CorrectionSpeed = 2;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), 0, 0.125, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.InProgress));
            _character.Body.enabled = false;
            _character.Body.enabled = true;

            Assert.That(_manager.TryContinueGradualCorrection(_character, 1, 0.125, _pairs, _contacts,
                out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1.25f, 0)));
        }

        [TestCase(CollisionPlane.XY, true)]
        [TestCase(CollisionPlane.XZ, false)]
        public void DeferredMoveReportsPenetrationAndEnablingCorrectsBeforeReturning(CollisionPlane plane, bool characterFirst)
        {
            CreateWorld(plane, characterFirst);
            Assert.That(_character.CanCorrectPosition, Is.True);
            _character.Timing = CorrectionTiming.Deferred;
            Assert.That(_character.CanCorrectPosition, Is.False);
            Vector3 target = Point(1.5f, 0, 2);
            Vector3 wallPosition = _wall.transform.position;

            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Skipped));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Half));
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
            Assert.That(_pairs.Count, Is.EqualTo(1));
            Assert.That(_contacts.Count, Is.EqualTo(2));
            Assert.That(_contacts, Has.All.Matches<CollisionContact>(contact => contact.Shapes.Contact.Depth == Fix64.Half));

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts, out result, out error), Is.True, error);
            Assert.That(_character.CanCorrectPosition, Is.True);
            Assert.That(_character.Timing, Is.EqualTo(CorrectionTiming.Deferred));
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.EqualTo(1));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Zero));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 0, 2)));
            Assert.That(_wall.transform.position, Is.EqualTo(wallPosition));
            Assert.That(_pairs.Count, Is.EqualTo(1));
            Assert.That(_contacts.Count, Is.EqualTo(2));
            Assert.That(_contacts, Has.All.Matches<CollisionContact>(contact => contact.Shapes.Contact.Depth == Fix64.Zero));
        }

        [TestCase(0f, 0)]
        [TestCase(-2.5f, 1)]
        public void EnablingUsesCurrentPositionAfterLeavingWall(float currentX, int expectedPairCount)
        {
            CreateWorld(CollisionPlane.XY, true);
            _character.Timing = CorrectionTiming.Deferred;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), _pairs, _contacts,
                out string error), Is.True, error);
            Assert.That(_contacts.Count, Is.EqualTo(2));
            Vector3 currentPosition = Point(currentX, 0);
            _character.Body.transform.position = currentPosition;

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts,
                out PositionCorrectionResult result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Zero));
            Assert.That(_character.Body.transform.position, Is.EqualTo(currentPosition));
            Assert.That(_pairs.Count, Is.EqualTo(expectedPairCount));
            Assert.That(_contacts.Count, Is.EqualTo(expectedPairCount));
            if (expectedPairCount == 1)
            {
                Assert.That(_pairs[0], Is.EqualTo(new CollisionPair(Id(_character.Body), Id(_trigger))));
                Assert.That(_contacts[0].Shapes.Contact.Depth, Is.GreaterThan(Fix64.Zero));
            }
        }

        [Test]
        public void ClosingOrRepeatingPermissionDoesNotRunStep()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1, 0), _pairs, _contacts,
                out string error), Is.True, error);
            CollisionPair[] previousPairs = _pairs.ToArray();
            CollisionContact[] previousContacts = _contacts.ToArray();
            index.FailOperation = "Query";
            Vector3 overlapping = Point(1.5f, 0);
            _character.Body.transform.position = overlapping;

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts,
                out PositionCorrectionResult result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.CanCorrectPosition, Is.True);
            Assert.That(_manager.TrySetCorrectionAllowed(_character, false, _pairs, _contacts, out result, out error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.CanCorrectPosition, Is.False);
            Assert.That(_character.Body.transform.position, Is.EqualTo(overlapping));
            Assert.That(_pairs, Is.EqualTo(previousPairs));
            Assert.That(_contacts, Is.EqualTo(previousContacts));
            Assert.That(index.FailOperation, Is.EqualTo("Query"));
        }

        [Test]
        public void EnablingResolvesCornerWithFreshContactsAfterEachPush()
        {
            CreateWorld(CollisionPlane.XY, true);
            CollisionBodyBehaviour topWall = AddFixedBox("Top wall", Point(0, 3), new Vector2(1.75f, 1));
            _character.Timing = CorrectionTiming.Deferred;
            _character.Body.transform.position = Point(1.5f, 1.5f);

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.EqualTo(2));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Zero));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 1)));
            Assert.That(_wall.transform.position, Is.EqualTo(Point(3, 0)));
            Assert.That(topWall.transform.position, Is.EqualTo(Point(0, 3)));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void EnablingFailureKeepsPermissionClosedAndAllowsRetry(int successfulQueries)
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            _character.Timing = CorrectionTiming.Deferred;
            Vector3 target = Point(1.5f, 0);
            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts, out string error), Is.True, error);
            index.FailOperation = "Query";
            index.FailureDelay = successfulQueries;

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts,
                out PositionCorrectionResult result, out error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.CanCorrectPosition, Is.False);
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);

            Assert.That(_manager.TrySetCorrectionAllowed(_character, true, _pairs, _contacts, out result, out error), Is.True, error);
            Assert.That(_character.CanCorrectPosition, Is.True);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(1, 0)));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void FailedInitialOrVerificationStepRollsBackAndNextStepResynchronizes(int successfulQueries)
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger) });
            index.FailOperation = "Query";
            index.FailureDelay = successfulQueries;
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
            Assert.That(_world.Step(_pairs, _contacts, out error), Is.True, error);
            Assert.That(_pairs, Is.Empty);
        }

        [Test]
        public void ATargetOutsideWorldRollsBackEvenWhenStepSucceedsForOtherBodies()
        {
            CreateWorld(CollisionPlane.XY, true);
            Vector3 original = _character.Body.transform.position;
            Assert.That(_manager.TryMoveAndCorrect(_character, Point(12, 0), _pairs, _contacts, out string error), Is.False);
            Assert.That(error, Does.Contain("did not participate"));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
        }

        [TestCase(CollisionPlane.XY)]
        [TestCase(CollisionPlane.XZ)]
        public void CornerResolvesBothWallsFromFreshContacts(CollisionPlane plane)
        {
            CreateWorld(plane, true);
            CollisionBodyBehaviour topWall = AddFixedBox("Top wall", Point(0, 3), new Vector2(1.75f, 1));
            Vector3 target = Point(1.5f, 1.5f, 2);
            Vector3 expected = Point(1, 1, 2);

            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.EqualTo(2));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Zero));
            Assert.That(_character.Body.transform.position, Is.EqualTo(expected));
            Assert.That(_wall.transform.position, Is.EqualTo(Point(3, 0)));
            Assert.That(topWall.transform.position, Is.EqualTo(Point(0, 3)));
            Assert.That(_pairs.Count, Is.EqualTo(2));
            Assert.That(_contacts, Has.All.Matches<CollisionContact>(contact => contact.Shapes.Contact.Depth == Fix64.Zero));
        }

        [Test]
        public void NarrowGapStopsAtTheConfiguredLimitWithFinalPenetration()
        {
            CreateWorld(CollisionPlane.XY, true);
            CollisionBodyBehaviour leftWall = AddFixedBox("Left wall", Point(-1.75f, 0), new Vector2(1, 2));
            CollisionBodyBehaviour rightWall = AddFixedBox("Right wall", Point(1.75f, 0), new Vector2(1, 2),
                leftWall.GetComponent<CollisionBodySettings>());
            _manager.MaxIterations = 3;

            Assert.That(_manager.TryMoveAndCorrect(_character, Point(0, 0), _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.IterationLimit));
            Assert.That(result.Iterations, Is.EqualTo(3));
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.Half));
            Assert.That(_character.Body.transform.position, Is.EqualTo(Point(0.25f, 0)));
            Assert.That(leftWall.transform.position, Is.EqualTo(Point(-1.75f, 0)));
            Assert.That(rightWall.transform.position, Is.EqualTo(Point(1.75f, 0)));
            CollisionPair rightPair = new CollisionPair(Id(_character.Body), Id(rightWall));
            Assert.That(_contacts, Has.Some.Matches<CollisionContact>(contact => contact.Pair == rightPair
                && contact.Shapes.Contact.Depth == result.RemainingDepth));
        }

        [Test]
        public void ExistingPenetrationWithinToleranceDoesNotMoveTheCharacter()
        {
            CreateWorld(CollisionPlane.XY, true);
            _manager.PenetrationTolerance = 0.125f;
            Vector3 target = Point(1.125f, 0);

            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.Resolved));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.FromRatio(1, 8)));
            Assert.That(_character.Body.transform.position, Is.EqualTo(target));
        }

        [Test]
        public void CorrectionBelowOneRawUnitOnBothAxesReportsNoProgress()
        {
            CreateWorld(CollisionPlane.XY, true);
            _manager.PenetrationTolerance = 0;
            float rawUnit = (float)(1.0 / Fix64.Scale);
            _trigger.transform.position = Vector3.zero;
            _trigger.GetComponent<CollisionBodySettings>().Layer = CollisionLayer.WorldSolid;
            _trigger.GetComponent<CircleShapeBehaviour>().Radius = rawUnit;
            _character.GetComponent<CircleShapeBehaviour>().Radius = rawUnit;
            Vector3 target = Point(rawUnit, rawUnit);

            Assert.That(_manager.TryMoveAndCorrect(_character, target, _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.True, error);
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NoProgress));
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.RemainingDepth, Is.EqualTo(Fix64.FromRaw(1)));
            Assert.That(_character.Body.transform.position.Equals(target), Is.True);
            Assert.That(_contacts.Count, Is.EqualTo(1));
        }

        [Test]
        public void FailureAfterTwoCorrectionsRollsBackTheEntireMove()
        {
            StubBroadPhase2D index = new StubBroadPhase2D();
            CreateWorld(CollisionPlane.XY, true, index);
            CollisionBodyBehaviour topWall = AddFixedBox("Top wall", Point(0, 3), new Vector2(1.75f, 1));
            index.Candidates.AddRange(new[] { Id(_character.Body), Id(_wall), Id(_trigger), Id(topWall) });
            index.FailOperation = "Query";
            index.FailureDelay = 8;
            Vector3 original = _character.Body.transform.position;

            Assert.That(_manager.TryMoveAndCorrect(_character, Point(1.5f, 1.5f), _pairs, _contacts,
                out PositionCorrectionResult result, out string error), Is.False);
            Assert.That(error, Does.Contain("Injected Query"));
            Assert.That(result.Status, Is.EqualTo(PositionCorrectionStatus.NotCompleted));
            Assert.That(_character.Body.transform.position, Is.EqualTo(original));
            Assert.That(_pairs, Is.Empty);
            Assert.That(_contacts, Is.Empty);
        }

        private void CreateWorld(CollisionPlane plane, bool characterFirst, IBroadPhase2D index = null)
        {
            _root = new GameObject("Position correction test world");
            _world = _root.AddComponent<CollisionWorldBehaviour>();
            _world.Configure(plane, Vector2.zero, new Vector2(9, 4));
            _manager = _root.AddComponent<PositionCorrectionManager>();
            CollisionBodySettings actor = CreateBody("Character", CollisionLayer.Character, Point(0, 0));
            _character = actor.gameObject.AddComponent<CharacterMovement>();
            _character.Body = actor.Body;
            CollisionBodySettings wall = CreateBody("Wall", CollisionLayer.WorldSolid, Point(3, 0));
            _wall = wall.Body;
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject part = new GameObject("Wall part");
                part.transform.SetParent(_wall.transform, false);
                part.transform.localPosition = Point(0, i);
                part.AddComponent<BoxShapeBehaviour>().HalfExtents = Vector2.one;
            }

            CollisionBodySettings trigger = CreateBody("Trigger", CollisionLayer.Trigger, Point(-3, 0));
            _trigger = trigger.Body;
            _character.gameObject.AddComponent<CircleShapeBehaviour>().Radius = 1;
            _trigger.gameObject.AddComponent<CircleShapeBehaviour>().Radius = 1;
            _world.Initialize(index ?? new UniformGridBroadPhase2D(new Aabb2D(FixVector2.Zero, new FixVector2(9, 4)), 2));
            (characterFirst ? _character.Body : _wall).BindWorld(_world);
            (characterFirst ? _wall : _character.Body).BindWorld(_world);
            _trigger.BindWorld(_world);
            _manager.Configure(_world, new[] { actor, wall, trigger });
        }

        private CollisionBodySettings CreateBody(string name, CollisionLayer layer, Vector3 position)
        {
            GameObject body = new GameObject(name);
            body.transform.SetParent(_root.transform, false);
            body.transform.position = position;
            CollisionBodySettings settings = body.AddComponent<CollisionBodySettings>();
            settings.Body.Plane = _world.Plane;
            settings.Layer = layer;
            return settings;
        }

        private CollisionBodyBehaviour AddFixedBox(string name, Vector3 position, Vector2 halfExtents,
            CollisionBodySettings otherWall = null)
        {
            CollisionBodySettings box = CreateBody(name, CollisionLayer.WorldSolid, position);
            box.gameObject.AddComponent<BoxShapeBehaviour>().HalfExtents = halfExtents;
            box.Body.BindWorld(_world);
            _manager.Configure(_world, new[] { _wall.GetComponent<CollisionBodySettings>(), box, otherWall });
            return box.Body;
        }

        private Vector3 Point(float x, float y, float orthogonal = 0)
        {
            return _world.Plane == CollisionPlane.XY ? new Vector3(x, y, orthogonal) : new Vector3(x, orthogonal, y);
        }

        private CollisionBodyId Id(CollisionBodyBehaviour body)
        {
            Assert.That(_world.TryGetBodyId(body, out CollisionBodyId id), Is.True);
            return id;
        }
    }
}
