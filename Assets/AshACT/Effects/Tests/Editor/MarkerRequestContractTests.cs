using System;
using NUnit.Framework;

namespace AshACT.Effects.Tests
{
    public sealed class MarkerRequestContractTests
    {
        [Test]
        public void DefaultRequestAndResultCannotReportSuccess()
        {
            Assert.That(default(MarkerRequest).IsValid, Is.False);
            Assert.That(default(MarkerRequestResult).Succeeded, Is.False);
            Assert.That(default(MarkerRequestResult).Error, Is.EqualTo(MarkerRequestError.HandlerUnavailable));
        }

        [Test]
        public void RequestKeepsExactStableContextWithoutDependingOnAnEffectInstance()
        {
            MarkerRequest request = new MarkerRequest(MarkerRequestKind.Add, new MarkerId(7),
                new EntityId(ulong.MaxValue), new EntityId(2), long.MaxValue, 3,
                definitionId: new EffectDefinitionId(1001), instanceId: new EffectInstanceId(42), operationIndex: 4);
            Assert.That(request.IsValid, Is.True);
            Assert.That(request.SourceId.Value, Is.EqualTo(ulong.MaxValue));
            Assert.That(request.RecipientId.Value, Is.EqualTo(2));
            Assert.That(request.Frame, Is.EqualTo(long.MaxValue));
            Assert.That(request.Count, Is.EqualTo(3));
            Assert.That(request.MarkerId.Value, Is.EqualTo(7));
            Assert.That(request.DefinitionId.Value, Is.EqualTo(1001));
            Assert.That(request.InstanceId.Value, Is.EqualTo(42));
            Assert.That(request.OperationIndex, Is.EqualTo(4));
            MarkerRequest direct = new MarkerRequest(MarkerRequestKind.Add, new MarkerId(7), new EntityId(1), new EntityId(2), 0);
            Assert.That(direct.IsValid, Is.True);
            Assert.That(direct.DefinitionId.IsValid, Is.False);
            Assert.That(direct.InstanceId.IsValid, Is.False);
        }

        [Test]
        public void RemovalPreservesSourceFilterAndDistinguishesCountFromAll()
        {
            MarkerRequest partial = new MarkerRequest(MarkerRequestKind.Remove, new MarkerId(7), new EntityId(1),
                new EntityId(2), 10, 2, MarkerRequestRemovalScope.RequestSource);
            Assert.That(partial.RemovalScope, Is.EqualTo(MarkerRequestRemovalScope.RequestSource));
            Assert.That(partial.RemovalMode, Is.EqualTo(MarkerRequestRemovalMode.Count));
            Assert.That(partial.Count, Is.EqualTo(2));
            MarkerRequest all = new MarkerRequest(MarkerRequestKind.Remove, new MarkerId(7), new EntityId(1),
                new EntityId(2), 10, 0, removalMode: MarkerRequestRemovalMode.All);
            Assert.That(all.IsValid, Is.True);
            Assert.That(all.RemovalMode, Is.EqualTo(MarkerRequestRemovalMode.All));
            Assert.That(all.Count, Is.Zero);
        }

        [TestCase("marker")]
        [TestCase("source")]
        [TestCase("recipient")]
        [TestCase("frame")]
        [TestCase("count")]
        [TestCase("kind")]
        [TestCase("index")]
        [TestCase("instance")]
        public void InvalidRequestContextIsRejectedBeforeCallingAnyHandler(string problem)
        {
            Assert.Throws<ArgumentException>(() => new MarkerRequest(
                problem == "kind" ? (MarkerRequestKind)99 : MarkerRequestKind.Add,
                new MarkerId(problem == "marker" ? 0u : 7u),
                new EntityId(problem == "source" ? 0ul : 1ul),
                new EntityId(problem == "recipient" ? 0ul : 2ul),
                problem == "frame" ? -1 : 10,
                problem == "count" ? 0 : 1,
                instanceId: problem == "instance" ? new EffectInstanceId(1) : default,
                operationIndex: problem == "index" ? -1 : 0));
        }

        [TestCase(MarkerRequestKind.Add, 0, MarkerRequestRemovalScope.AllSources, MarkerRequestRemovalMode.Count)]
        [TestCase(MarkerRequestKind.Add, 1, MarkerRequestRemovalScope.RequestSource, MarkerRequestRemovalMode.Count)]
        [TestCase(MarkerRequestKind.Add, 1, MarkerRequestRemovalScope.AllSources, MarkerRequestRemovalMode.All)]
        [TestCase(MarkerRequestKind.Remove, 1, MarkerRequestRemovalScope.AllSources, MarkerRequestRemovalMode.All)]
        [TestCase(MarkerRequestKind.Remove, -1, MarkerRequestRemovalScope.AllSources, MarkerRequestRemovalMode.Count)]
        [TestCase(MarkerRequestKind.Remove, 1, (MarkerRequestRemovalScope)99, MarkerRequestRemovalMode.Count)]
        [TestCase(MarkerRequestKind.Remove, 1, MarkerRequestRemovalScope.AllSources, (MarkerRequestRemovalMode)99)]
        public void AmbiguousOperationFieldsAreRejected(MarkerRequestKind kind, int count,
            MarkerRequestRemovalScope scope, MarkerRequestRemovalMode mode)
        {
            Assert.Throws<ArgumentException>(() => new MarkerRequest(kind, new MarkerId(7), new EntityId(1),
                new EntityId(2), 0, count, scope, mode));
        }

        [Test]
        public void PreparationFailureRemainsDistinctFromSuccessfulNoChange()
        {
            MarkerRequestResult ready = new MarkerRequestResult(MarkerRequestError.None, "满层时忽略新增");
            MarkerRequestResult rejected = new MarkerRequestResult(MarkerRequestError.UnsupportedOperation, "尚未实现刷新规则");
            Assert.That(ready.Succeeded, Is.True);
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.Error, Is.EqualTo(MarkerRequestError.UnsupportedOperation));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MarkerRequestResult((MarkerRequestError)99));
        }
    }
}
