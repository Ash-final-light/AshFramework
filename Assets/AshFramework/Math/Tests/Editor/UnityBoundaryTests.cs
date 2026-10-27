using System;
using AshFramework.Math.Unity;
using NUnit.Framework;
using UnityEngine;

namespace AshFramework.Math.Tests
{
    public sealed class UnityBoundaryTests
    {
        [Test]
        public void CoreAssemblyHasNoUnityOrCollisionReferences()
        {
            foreach (System.Reflection.AssemblyName reference in typeof(Fix64).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("Unity", StringComparison.Ordinal), Is.False, reference.Name);
                Assert.That(reference.Name.IndexOf("Collision", StringComparison.OrdinalIgnoreCase), Is.EqualTo(-1));
            }
        }

        [Test]
        public void UnityConversionsAreExplicitAndPreserveExactlyRepresentableValues()
        {
            Vector3 input = new Vector3(1.5f, -2.25f, 4);
            Assert.That(input.ToFixVector().ToUnityVector(), Is.EqualTo(input));
            Vector2 input2 = new Vector2(-1.5f, 2);
            Assert.That(input2.ToFixVector().ToUnityVector(), Is.EqualTo(input2));
            Assert.That(Quaternion.identity.ToFixQuaternion(), Is.EqualTo(FixQuaternion.Identity));
            Assert.That(FixQuaternion.Identity.ToUnityQuaternion(), Is.EqualTo(Quaternion.identity));
            Assert.Throws<ArgumentOutOfRangeException>(() => float.NaN.ToFix64());
            Assert.Throws<ArgumentOutOfRangeException>(() => double.PositiveInfinity.ToFix64());
            Assert.Throws<OverflowException>(() => 2147483648.0.ToFix64());
        }
    }
}
