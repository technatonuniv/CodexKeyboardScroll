using System;
using System.Threading;
using System.Windows.Automation;

namespace CodexKeyboardScroll
{
    internal sealed class ComposerLocator : IDisposable
    {
        private readonly object sync = new object();
        private AutomationElement composer;
        private IntPtr cachedWindow;
        private int lastRefreshTick;
        private int refreshRunning;
        private bool disposed;

        internal string StatusText
        {
            get
            {
                lock (sync)
                {
                    return composer == null
                        ? "UI Automation: coordinate fallback active"
                        : "UI Automation: composer found";
                }
            }
        }

        internal string CompactStatus
        {
            get
            {
                lock (sync)
                {
                    return composer == null ? "Fallback" : "UIA";
                }
            }
        }

        internal void RequestRefresh(IntPtr window)
        {
            if (disposed || window == IntPtr.Zero)
            {
                return;
            }

            int now = Environment.TickCount;
            lock (sync)
            {
                if (cachedWindow == window && unchecked(now - lastRefreshTick) < 2000)
                {
                    return;
                }
                lastRefreshTick = now;
            }

            if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0)
            {
                return;
            }

            // Chromium accessibility traversal can block briefly. Keeping it off the UI
            // thread prevents missed keyboard hooks and a frozen tray menu.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Refresh(window); }
                finally { Interlocked.Exchange(ref refreshRunning, 0); }
            });
        }

        internal bool TryFocus(IntPtr window)
        {
            AutomationElement element;
            lock (sync)
            {
                element = cachedWindow == window ? composer : null;
            }

            if (element == null)
            {
                RequestRefresh(window);
                return false;
            }

            try
            {
                element.SetFocus();
                Thread.Sleep(10);
                if (element.Current.HasKeyboardFocus)
                {
                    return true;
                }
            }
            catch
            {
            }

            RequestRefresh(window);
            return false;
        }

        private void Refresh(IntPtr window)
        {
            AutomationElement found = null;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root != null)
                {
                    found = FindBestComposer(root);
                }
            }
            catch
            {
            }

            if (disposed)
            {
                return;
            }
            lock (sync)
            {
                cachedWindow = window;
                composer = found;
            }
        }

        private static AutomationElement FindBestComposer(AutomationElement root)
        {
            System.Windows.Rect rootRect = root.Current.BoundingRectangle;
            AutomationElementCollection edits = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            AutomationElement best = null;
            double bestScore = double.MinValue;
            foreach (AutomationElement element in edits)
            {
                try
                {
                    System.Windows.Rect rect = element.Current.BoundingRectangle;
                    if (!IsComposerCandidate(element, rect, rootRect))
                    {
                        continue;
                    }

                    // Prefer a wide, focusable edit near the bottom of the window. Name
                    // hints are secondary so localization does not become a requirement.
                    double score = rect.Top + rect.Width;
                    if (element.Current.IsKeyboardFocusable) score += 10000;
                    if (HasComposerHint(element.Current.Name)) score += 20000;
                    if (score > bestScore)
                    {
                        best = element;
                        bestScore = score;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return best;
        }

        private static bool IsComposerCandidate(
            AutomationElement element,
            System.Windows.Rect rect,
            System.Windows.Rect rootRect)
        {
            return !rect.IsEmpty
                && rect.Width >= 140
                && rect.Height >= 18
                && rect.Top >= rootRect.Top + rootRect.Height * 0.50
                && rect.Bottom <= rootRect.Bottom + 2
                && element.Current.IsEnabled;
        }

        private static bool HasComposerHint(string value)
        {
            string name = (value ?? string.Empty).ToLowerInvariant();
            return name.Contains("message")
                || name.Contains("prompt")
                || name.Contains("ask")
                || name.Contains("\u0441\u043e\u043e\u0431\u0449")
                || name.Contains("\u0432\u043e\u043f\u0440\u043e\u0441");
        }

        public void Dispose()
        {
            disposed = true;
            lock (sync)
            {
                composer = null;
                cachedWindow = IntPtr.Zero;
            }
        }
    }
}
