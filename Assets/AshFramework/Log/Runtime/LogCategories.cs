namespace AshFramework.Logging
{
    public static class LogCategories
    {
        public const string Default = "Default";
        public const string Framework = "Framework";
        public const string UI = "UI";
        public const string Network = "Network";
        public const string Resource = "Resource";
        public const string Battle = "Battle";
        public const string Event = "Event";

        public static bool IsValid(string category)
        {
            if (string.IsNullOrEmpty(category) || category.Length > 64)
            {
                return false;
            }

            foreach (char character in category)
            {
                if (!(character >= 'A' && character <= 'Z') &&
                    !(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '_' && character != '.' && character != '-')
                {
                    return false;
                }
            }
            return true;
        }

        internal static string Normalize(string category)
        {
            if (string.IsNullOrEmpty(category))
            {
                return Default;
            }
            if (IsValid(category))
            {
                return category;
            }
            LogDiagnostics.Report("非法日志分类已回退为 Default。分类仅允许 1～64 个英文字母、数字、_、.、-。");
            return Default;
        }
    }
}
