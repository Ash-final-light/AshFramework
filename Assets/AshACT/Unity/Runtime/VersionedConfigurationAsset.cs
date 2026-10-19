using UnityEngine;

namespace AshACT.Unity
{
    public abstract class VersionedConfigurationAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 3;
        public int SchemaVersion = CurrentSchemaVersion;
        public int ReleaseVersion = 1;
    }
}
