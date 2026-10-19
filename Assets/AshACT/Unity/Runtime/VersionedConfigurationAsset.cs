using UnityEngine;

namespace AshACT.Unity
{
    public abstract class VersionedConfigurationAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 2;
        public int SchemaVersion = CurrentSchemaVersion;
        public int ReleaseVersion = 1;
    }
}
