using System.Collections.Generic;

namespace AshFramework.UI.UIModule
{
    internal sealed class UILayerManager
    {
        private readonly Dictionary<UILayer, List<WindowEntry>> _visible =
            new Dictionary<UILayer, List<WindowEntry>>
            {
                { UILayer.Main, new List<WindowEntry>() },
                { UILayer.Popup, new List<WindowEntry>() },
                { UILayer.Overlay, new List<WindowEntry>() }
            };

        public void CommitShow(WindowEntry entry, long sequence)
        {
            List<WindowEntry> windows = _visible[entry.Registration.Definition.Layer];
            windows.Remove(entry);
            entry.ShowSequence = sequence;
            int index = windows.FindIndex(window => window.ShowSequence > sequence);

            if (index < 0)
            {
                windows.Add(entry);
            }
            else
            {
                windows.Insert(index, entry);
            }

            foreach (WindowEntry window in windows)
            {
                window.Lease.Instance.transform.SetAsLastSibling();
            }
        }

        public void Remove(WindowEntry entry)
        {
            _visible[entry.Registration.Definition.Layer].Remove(entry);
        }

        public IUIHandle GetTop(UILayer layer)
        {
            List<WindowEntry> windows = _visible[layer];
            return windows.Count == 0 ? null : windows[windows.Count - 1];
        }
    }
}
