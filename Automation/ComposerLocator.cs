using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace CodexKeyboardScroll
{
    internal sealed class ComposerLocator : IDisposable
    {
        private readonly object sync = new object();
        private AutomationElement composer;
        private AutomationElement composerContainer;
        private IntPtr cachedWindow;
        private int lastRefreshTick;
        private int refreshRunning;
        private bool hasCompletedRefresh;
        private bool disposed;

        internal ComposerStatus Status
        {
            get
            {
                lock (sync)
                {
                    return !hasCompletedRefresh
                        ? ComposerStatus.Locating
                        : composer == null
                            ? ComposerStatus.CoordinateFallback
                            : ComposerStatus.Found;
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

        internal bool TryGetBounds(IntPtr window, out NativeInput.Rect bounds)
        {
            AutomationElement element;
            bool isContainer;
            lock (sync)
            {
                isContainer = cachedWindow == window && composerContainer != null;
                element = isContainer
                    ? composerContainer
                    : cachedWindow == window ? composer : null;
            }

            if (element == null)
            {
                bounds = new NativeInput.Rect();
                RequestRefresh(window);
                return false;
            }

            try
            {
                System.Windows.Rect rect = element.Current.BoundingRectangle;
                if (!isContainer && !rect.IsEmpty)
                {
                    // The editable element starts inside the visual composer card.
                    // Include its small upper inset when no suitable parent is exposed.
                    rect.Y -= 24;
                    rect.Height += 24;
                }

                if (TryConvertBounds(rect, out bounds))
                {
                    return true;
                }
            }
            catch (Exception error)
            {
                if (!IsExpectedAccessibilityFailure(error))
                {
                    throw;
                }
            }

            bounds = new NativeInput.Rect();
            RequestRefresh(window);
            return false;
        }

        private void Refresh(IntPtr window)
        {
            AutomationElement found = null;
            AutomationElement container = null;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root != null)
                {
                    found = FindBestComposer(root);
                    container = FindComposerContainer(found);
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
                composerContainer = container;
                hasCompletedRefresh = true;
            }
        }

        private static AutomationElement FindComposerContainer(AutomationElement element)
        {
            if (element == null)
            {
                return null;
            }

            try
            {
                System.Windows.Rect editRect = element.Current.BoundingRectangle;
                AutomationElement parent = TreeWalker.RawViewWalker.GetParent(element);
                if (parent == null)
                {
                    return null;
                }

                System.Windows.Rect parentRect = parent.Current.BoundingRectangle;
                return IsComposerContainer(parentRect, editRect) ? parent : null;
            }
            catch (Exception error)
            {
                if (!IsExpectedAccessibilityFailure(error))
                {
                    throw;
                }
                return null;
            }
        }

        private static bool IsComposerContainer(
            System.Windows.Rect candidate,
            System.Windows.Rect edit)
        {
            return !candidate.IsEmpty
                && !edit.IsEmpty
                && candidate.Left <= edit.Left + 2
                && candidate.Top <= edit.Top + 2
                && candidate.Right >= edit.Right - 2
                && candidate.Bottom >= edit.Bottom - 2
                && edit.Top - candidate.Top <= 80
                && candidate.Bottom - edit.Bottom <= 100
                && candidate.Width - edit.Width <= 200
                && candidate.Height <= 220;
        }

        private static bool TryConvertBounds(
            System.Windows.Rect rect,
            out NativeInput.Rect bounds)
        {
            if (rect.IsEmpty
                || !IsScreenCoordinate(rect.Left)
                || !IsScreenCoordinate(rect.Top)
                || !IsScreenCoordinate(rect.Right)
                || !IsScreenCoordinate(rect.Bottom))
            {
                bounds = new NativeInput.Rect();
                return false;
            }

            bounds = new NativeInput.Rect(
                (int)Math.Floor(rect.Left),
                (int)Math.Floor(rect.Top),
                (int)Math.Ceiling(rect.Right),
                (int)Math.Ceiling(rect.Bottom));
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }

        private static bool IsScreenCoordinate(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value >= int.MinValue
                && value <= int.MaxValue;
        }

        private static bool IsExpectedAccessibilityFailure(Exception error)
        {
            return error is ElementNotAvailableException
                || error is InvalidOperationException
                || error is COMException;
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
                composerContainer = null;
                cachedWindow = IntPtr.Zero;
                hasCompletedRefresh = false;
            }
        }
    }
}
