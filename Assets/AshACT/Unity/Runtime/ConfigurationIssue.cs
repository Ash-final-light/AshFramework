namespace AshACT.Unity
{
    public sealed class ConfigurationIssue
    {
        public string Code { get; }
        public VersionedConfigurationAsset Asset { get; }
        public string Path { get; }
        public string Message { get; }
        public int NodeId { get; }

        public ConfigurationIssue(string code, VersionedConfigurationAsset asset, string path,
            string message, int nodeId = 0)
        {
            Code = code;
            Asset = asset;
            Path = path;
            Message = message;
            NodeId = nodeId;
        }

        public override string ToString() => $"{Code} / {Path} / 节点 {NodeId}: {Message}";
    }
}
