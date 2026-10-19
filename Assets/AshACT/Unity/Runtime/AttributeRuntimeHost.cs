using System.Collections.Generic;
using UnityEngine;

namespace AshACT.Unity
{
    [DisallowMultipleComponent]
    public sealed class AttributeRuntimeHost : MonoBehaviour
    {
        public AttributeSessionSetup Setup { get; private set; }
        public AttributeSession Session { get; private set; }

        public bool TryInitialize(out IReadOnlyList<ConfigurationIssue> issues)
        {
            return TryInitialize(Setup, out issues);
        }

        public bool TryInitialize(AttributeSessionSetup setup, out IReadOnlyList<ConfigurationIssue> issues)
        {
            if (!ConfigurationCompiler.TryCompileSession(setup, out CompiledSessionConfiguration compiled, out issues))
            {
                return false;
            }

            AttributeSession session = new AttributeSession(compiled);
            Setup = setup;
            Session = session;
            return true;
        }
    }
}
