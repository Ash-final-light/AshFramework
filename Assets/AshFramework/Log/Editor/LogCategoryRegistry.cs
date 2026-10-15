using System;
using System.Collections.Generic;
using System.Reflection;

namespace AshFramework.Logging.Editor
{
    internal static class LogCategoryRegistry
    {
        private static string[] s_Categories;

        internal static string[] GetCategories()
        {
            if (s_Categories != null)
            {
                return s_Categories;
            }
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var field in typeof(LogCategories).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                {
                    names.Add((string)field.GetRawConstantValue());
                }
            }
            var settings = LogSettingsEditor.FindSettings();
            if (settings != null && settings.CustomCategories != null)
            {
                foreach (var category in settings.CustomCategories)
                {
                    if (LogCategories.IsValid(category))
                    {
                        names.Add(category);
                    }
                }
            }
            s_Categories = new string[names.Count];
            names.CopyTo(s_Categories);
            return s_Categories;
        }

        internal static void Invalidate()
        {
            s_Categories = null;
        }
    }
}
