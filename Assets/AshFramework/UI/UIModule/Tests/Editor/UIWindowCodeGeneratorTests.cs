using System;
using System.IO;
using AshFramework.UI.UIModule.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AshFramework.UI.UIModule.Tests
{
    public sealed class UIWindowCodeGeneratorTests
    {
        private UIWindowGenerationOptions _options;
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            string relative = "Assets/__AshUIModuleGeneratorTests_" + Guid.NewGuid().ToString("N");
            _directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), relative);
            _options = new UIWindowGenerationOptions
            {
                WindowName = "Inventory",
                Namespace = "GeneratorTest.Inventory",
                ScriptDirectory = relative,
                PrefabPath = "Assets/Resources/UI/Inventory.prefab"
            };
            AssetDatabase.DisallowAutoRefresh();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                // Only this test's newly allocated, unimported directory is removed.
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, true);
                }
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }
        }

        [Test]
        public void RegenerationPreservesAllThreeBusinessFilesAndUpdatesOnlyBase()
        {
            UIWindowGenerationResult first = UIWindowCodeGenerator.Generate(_options);
            Assert.That(first.Written.Count, Is.EqualTo(4));
            string window = Path.Combine(_directory, "InventoryWindow.cs");
            string args = Path.Combine(_directory, "InventoryArgs.cs");
            string view = Path.Combine(_directory, "InventoryView.cs");
            File.WriteAllText(window, "// user window\n");
            File.WriteAllText(args, "// user args\n");
            File.WriteAllText(view, "// user view\n");
            _options.Layer = UILayer.Overlay;
            UIWindowGenerationResult second = UIWindowCodeGenerator.Generate(_options);
            Assert.That(second.Written.Count, Is.EqualTo(1));
            Assert.That(second.Preserved.Count, Is.EqualTo(3));
            Assert.That(File.ReadAllText(window), Is.EqualTo("// user window\n"));
            Assert.That(File.ReadAllText(args), Is.EqualTo("// user args\n"));
            Assert.That(File.ReadAllText(view), Is.EqualTo("// user view\n"));
            Assert.That(File.ReadAllText(Path.Combine(_directory, "Generated/InventoryWindowBase.g.cs")),
                Does.Contain("UILayer.Overlay"));
        }

        [Test]
        public void UnmarkedBaseIsRejectedBeforeAnyOtherFilesAreCreated()
        {
            string generated = Path.Combine(_directory, "Generated");
            Directory.CreateDirectory(generated);
            string path = Path.Combine(generated, "InventoryWindowBase.g.cs");
            File.WriteAllText(path, "// user owned\n");
            Assert.Throws<IOException>(() => UIWindowCodeGenerator.Generate(_options));
            Assert.That(File.ReadAllText(path), Is.EqualTo("// user owned\n"));
            Assert.That(File.Exists(Path.Combine(_directory, "InventoryWindow.cs")), Is.False);
        }

        [Test]
        public void ChangedNamespaceCannotSplitGeneratedBaseFromPreservedBusinessClasses()
        {
            UIWindowCodeGenerator.Generate(_options);
            string basePath = Path.Combine(_directory, "Generated/InventoryWindowBase.g.cs");
            string original = File.ReadAllText(basePath);
            _options.Namespace = "Another.Namespace";
            Assert.Throws<IOException>(() => UIWindowCodeGenerator.Generate(_options));
            Assert.That(File.ReadAllText(basePath), Is.EqualTo(original));
        }

        [TestCase("Assets/../Outside")]
        [TestCase("Packages/Generated")]
        [TestCase("Assets/Bad\nName")]
        public void OutputCannotEscapeAssetsOrInjectSource(string path)
        {
            _options.ScriptDirectory = path;
            Assert.That(UIWindowCodeGenerator.GetValidationError(_options), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => UIWindowCodeGenerator.Generate(_options));
            Assert.That(Directory.Exists(_directory), Is.False);
        }

        [TestCase("class")]
        [TestCase("Bad-Name")]
        [TestCase("1Inventory")]
        public void InvalidClassNamesAreRejected(string name)
        {
            _options.WindowName = name;
            Assert.That(UIWindowCodeGenerator.GetValidationError(_options), Is.Not.Null);
        }

        [Test]
        public void ResourceAddressIsDerivedFromChosenResourcesFolder()
        {
            _options.PrefabPath = "Assets/Game/Resources/Screens/Inventory.prefab";
            Assert.That(UIWindowCodeGenerator.GetValidationError(_options), Is.Null);
            Assert.That(UIWindowCodeGenerator.GetResourceKey(_options.PrefabPath), Is.EqualTo("Screens/Inventory"));
            _options.PrefabPath = "Assets/UI/Inventory.prefab";
            Assert.That(UIWindowCodeGenerator.GetValidationError(_options), Is.Not.Null);
        }

        [Test]
        public void GeneratorWindowPreservesChosenOptionsThroughEditorSerialization()
        {
            var window = ScriptableObject.CreateInstance<UIWindowGeneratorWindow>();
            var restored = ScriptableObject.CreateInstance<UIWindowGeneratorWindow>();

            try
            {
                var serialized = new SerializedObject(window);
                SerializedProperty options = serialized.FindProperty("_options");
                Assert.That(options, Is.Not.Null);
                options.FindPropertyRelative("WindowName").stringValue = "SerializationProbe";
                options.FindPropertyRelative("Namespace").stringValue = "Game.SerializationProbe";
                options.FindPropertyRelative("ScriptDirectory").stringValue = "Assets/SerializationProbe";
                options.FindPropertyRelative("PrefabPath").stringValue = "Assets/Resources/SerializationProbe.prefab";
                options.FindPropertyRelative("Layer").intValue = (int)UILayer.Popup;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                string saved = EditorJsonUtility.ToJson(window);
                EditorJsonUtility.FromJsonOverwrite(saved, restored);
                var restoredSerialized = new SerializedObject(restored);
                SerializedProperty restoredOptions = restoredSerialized.FindProperty("_options");
                Assert.That(restoredOptions.FindPropertyRelative("WindowName").stringValue, Is.EqualTo("SerializationProbe"));
                Assert.That(restoredOptions.FindPropertyRelative("Namespace").stringValue, Is.EqualTo("Game.SerializationProbe"));
                Assert.That(restoredOptions.FindPropertyRelative("ScriptDirectory").stringValue, Is.EqualTo("Assets/SerializationProbe"));
                Assert.That(restoredOptions.FindPropertyRelative("PrefabPath").stringValue, Is.EqualTo("Assets/Resources/SerializationProbe.prefab"));
                Assert.That(restoredOptions.FindPropertyRelative("Layer").intValue, Is.EqualTo((int)UILayer.Popup));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(restored);
            }
        }
    }
}
