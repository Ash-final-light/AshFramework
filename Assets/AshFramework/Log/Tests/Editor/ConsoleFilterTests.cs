using AshFramework.Logging.Editor;
using NUnit.Framework;

namespace AshFramework.Logging.Tests
{
    public sealed class ConsoleFilterTests
    {
        private sealed class Search : IConsoleSearch
        {
            internal string Text = "Boss";
            internal bool RejectWrites;

            public bool TryRead(out string text)
            {
                text = Text;
                return true;
            }

            public bool TryWrite(string text, out string error)
            {
                error = RejectWrites ? "unsupported" : null;
                if (RejectWrites)
                {
                    return false;
                }
                Text = text;
                return true;
            }
        }

        [Test]
        public void SwitchingCategoriesPreservesOriginalSearch()
        {
            var search = new Search();
            var controller = new ConsoleFilterController(search, new ConsoleFilterState());
            Assert.That(controller.ApplyCategory(LogCategories.UI), Is.True);
            Assert.That(search.Text, Is.EqualTo("[Ash][C:UI]"));
            Assert.That(controller.ApplyCategory(LogCategories.Network), Is.True);
            Assert.That(controller.RestorePrevious(), Is.True);
            Assert.That(search.Text, Is.EqualTo("Boss"));
        }

        [Test]
        public void ManualSearchIsNeverOverwrittenByRestore()
        {
            var search = new Search();
            var controller = new ConsoleFilterController(search, new ConsoleFilterState());
            controller.ApplyCategory(LogCategories.UI);
            search.Text = "Sword";
            Assert.That(controller.RestorePrevious(), Is.False);
            Assert.That(search.Text, Is.EqualTo("Sword"));
            Assert.That(controller.State.Mode, Is.EqualTo(ConsoleFilterMode.Manual));
            controller.ApplyCategory(LogCategories.Network);
            controller.RestorePrevious();
            Assert.That(search.Text, Is.EqualTo("Sword"));
        }

        [Test]
        public void RestoredStateDoesNotOverrideChangedNativeSearch()
        {
            var state = new ConsoleFilterState
            {
                Mode = ConsoleFilterMode.Category,
                OwnsSearch = true,
                LastAppliedText = "[Ash][C:UI]",
                PreviousSearchText = "Boss",
                SelectedCategory = LogCategories.UI
            };
            var search = new Search { Text = "Sword" };
            var controller = new ConsoleFilterController(search, state);
            controller.RefreshFromConsole();
            Assert.That(state.OwnsSearch, Is.False);
            Assert.That(controller.RestorePrevious(), Is.False);
            Assert.That(search.Text, Is.EqualTo("Sword"));
        }

        [Test]
        public void FailedWriteDoesNotTakeOwnership()
        {
            var search = new Search { RejectWrites = true };
            var state = new ConsoleFilterState();
            var controller = new ConsoleFilterController(search, state);
            Assert.That(controller.ApplyCategory(LogCategories.UI), Is.False);
            Assert.That(state.OwnsSearch, Is.False);
            Assert.That(search.Text, Is.EqualTo("Boss"));
        }

        [Test]
        public void ShowAllClearsSearchAndRecoveryState()
        {
            var search = new Search();
            var controller = new ConsoleFilterController(search, new ConsoleFilterState());
            controller.ShowFramework();
            controller.ShowAll();
            Assert.That(search.Text, Is.Empty);
            Assert.That(controller.State.OwnsSearch, Is.False);
            Assert.That(controller.State.PreviousSearchText, Is.Empty);
        }

        [Test]
        public void NativeConsoleSearchRoundTrip()
        {
            var required = ConsoleCapabilities.ReadSearch | ConsoleCapabilities.WriteSearch;
            Assert.That(ConsoleBridge.Capabilities & required, Is.EqualTo(required), ConsoleBridge.FailureReason);
            Assert.That(ConsoleBridge.TryGetSearchText(out string previous), Is.True);
            try
            {
                Assert.That(ConsoleBridge.TrySetSearchText("[Ash][C:UI]", out string error), Is.True, error);
                Assert.That(ConsoleBridge.TryGetSearchText(out string current), Is.True);
                Assert.That(current, Is.EqualTo("[Ash][C:UI]"));
            }
            finally
            {
                ConsoleBridge.TrySetSearchText(previous, out _);
            }
        }
    }
}
