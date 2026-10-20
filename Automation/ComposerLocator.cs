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
        private IntPtr pendingRefreshWindow;
        private int cachedProcessId;
        private bool replyInput;
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

        internal bool IsRefreshing
        {
            get { return Interlocked.CompareExchange(ref refreshRunning, 0, 0) != 0; }
        }

        internal bool HasReplyInput(IntPtr window)
        {
            lock (sync)
            {
                return cachedWindow == window && composer != null && replyInput;
            }
        }

        internal bool IsReplyFocused(IntPtr window)
        {
            AutomationElement element;
            lock (sync)
            {
                element = cachedWindow == window && replyInput ? composer : null;
            }
            try
            {
                return element != null && element.Current.HasKeyboardFocus;
            }
            catch (Exception error)
            {
                if (!IsExpectedAccessibilityFailure(error)) throw;
                return false;
            }
        }

        internal bool HasFocusedInput(IntPtr window)
        {
            bool focusedInput;
            return TryGetFocusedInput(window, out focusedInput) && focusedInput;
        }

        internal bool TryGetFocusedInput(IntPtr window, out bool focusedInput)
        {
            focusedInput = false;
            int processId;
            lock (sync)
            {
                processId = cachedWindow == window ? cachedProcessId : 0;
            }
            if (processId == 0 || window == IntPtr.Zero
                || window != NativeInput.GetForegroundWindow()) return false;

            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null || !focused.Current.IsEnabled
                    || !BelongsToWindow(focused, window))
                {
                    return false;
                }

                bool isInput = focused.Current.ControlType == ControlType.Edit;
                // Accessibility queries can outlive a foreground-window switch.
                // Unknown focus must not be mistaken for confirmed transcript focus.
                if (window != NativeInput.GetForegroundWindow()) return false;
                focusedInput = isInput;
                return true;
            }
            catch (Exception error)
            {
                if (!IsExpectedAccessibilityFailure(error)) throw;
                return false;
            }
        }

        private static bool BelongsToWindow(AutomationElement element, IntPtr window)
        {
            // Chromium can expose the composer and other edits from different
            // renderer processes. Their containing HWND, not PID, defines scope.
            for (int depth = 0; element != null && depth < 48; depth++)
            {
                if (new IntPtr(element.Current.NativeWindowHandle) == window)
                {
                    return true;
                }
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
        }

        internal void RequestRefresh(IntPtr window, bool force = false)
        {
            if (disposed || window == IntPtr.Zero)
            {
                return;
            }

            int now = Environment.TickCount;
            lock (sync)
            {
                if (!force && cachedWindow == window && unchecked(now - lastRefreshTick) < 2000)
                {
                    return;
                }
                if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0)
                {
                    if (force || cachedWindow != window) pendingRefreshWindow = window;
                    return;
                }
                lastRefreshTick = now;
            }

            // Chromium accessibility traversal can block briefly. Keeping it off the UI
            // thread prevents missed keyboard hooks and a frozen tray menu.
            ThreadPool.QueueUserWorkItem(delegate
            {
                RunRefresh(window);
            });
        }

        private void RunRefresh(IntPtr window)
        {
            try { Refresh(window); }
            finally
            {
                IntPtr next;
                lock (sync)
                {
                    next = disposed ? IntPtr.Zero : pendingRefreshWindow;
                    pendingRefreshWindow = IntPtr.Zero;
                    if (next == IntPtr.Zero) Interlocked.Exchange(ref refreshRunning, 0);
                }
                // A hotkey refresh must not be lost behind an older traversal that
                // began before the question appeared or the foreground window changed.
                if (next != IntPtr.Zero)
                {
                    ThreadPool.QueueUserWorkItem(delegate { RunRefresh(next); });
                }
            }
        }

        internal bool TryFocus(IntPtr window)
        {
            if (window == IntPtr.Zero || window != NativeInput.GetForegroundWindow())
            {
                return false;
            }

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
                if (element.Current.IsOffscreen || !element.Current.IsEnabled)
                {
                    RequestRefresh(window, true);
                    return false;
                }
                if (!BelongsToWindow(element, window)
                    || window != NativeInput.GetForegroundWindow())
                {
                    return false;
                }
                element.SetFocus();
                Thread.Sleep(10);
                if (window == NativeInput.GetForegroundWindow()
                    && element.Current.HasKeyboardFocus)
                {
                    return true;
                }
            }
            catch
            {
            }

            // If UIA cannot focus an exposed edit, target that edit's own bounds.
            // The generic bottom-of-window fallback would miss a pending reply.
            try
            {
                NativeInput.Rect bounds;
                if (TryConvertBounds(element.Current.BoundingRectangle, out bounds))
                {
                    return NativeInput.FocusInput(window, bounds);
                }
            }
            catch (Exception error)
            {
                if (!IsExpectedAccessibilityFailure(error)) throw;
            }

            RequestRefresh(window, true);
            return false;
        }

        internal bool TryGetBounds(IntPtr window, out NativeInput.Rect bounds)
        {
            AutomationElement element;
            AutomationElement edit;
            bool isContainer;
            lock (sync)
            {
                edit = cachedWindow == window ? composer : null;
                isContainer = cachedWindow == window && composerContainer != null;
                element = isContainer
                    ? composerContainer
                    : edit;
            }

            if (element == null || edit == null)
            {
                bounds = new NativeInput.Rect();
                RequestRefresh(window);
                return false;
            }

            try
            {
                if (edit.Current.IsOffscreen || !edit.Current.IsEnabled
                    || element.Current.IsOffscreen || !element.Current.IsEnabled)
                {
                    bounds = new NativeInput.Rect();
                    RequestRefresh(window, true);
                    return false;
                }
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
            int processId = 0;
            bool isReply = false;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root != null)
                {
                    processId = root.Current.ProcessId;
                    AutomationElement conversationComposer;
                    found = FindBestComposer(root, NativeInput.GetChatApplication(window),
                        out conversationComposer, out isReply);
                    if (found != null) processId = found.Current.ProcessId;
                    container = FindComposerContainer(found, conversationComposer, isReply);
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
                cachedProcessId = processId;
                composer = found;
                replyInput = isReply;
                composerContainer = container;
                hasCompletedRefresh = true;
            }
        }

        private static AutomationElement FindComposerContainer(
            AutomationElement element,
            AutomationElement conversationComposer,
            bool isReply)
        {
            if (element == null)
            {
                return null;
            }

            try
            {
                System.Windows.Rect editRect = element.Current.BoundingRectangle;
                AutomationElement parent = element;
                AutomationElement container = null;
                System.Windows.Rect mainRect = conversationComposer == null
                    ? System.Windows.Rect.Empty
                    : conversationComposer.Current.BoundingRectangle;
                for (int depth = 0; depth < (isReply ? 6 : 1); depth++)
                {
                    parent = TreeWalker.RawViewWalker.GetParent(parent);
                    if (parent == null) break;
                    System.Windows.Rect parentRect = parent.Current.BoundingRectangle;
                    if (!isReply)
                    {
                        return IsComposerContainer(parentRect, editRect) ? parent : null;
                    }
                    // Exclude the whole question card, including its options, from
                    // reading mode, but never absorb the ordinary composer below it.
                    if (mainRect.IsEmpty || !parentRect.Contains(editRect)
                        || parentRect.Bottom > mainRect.Top + 2)
                    {
                        break;
                    }
                    container = parent;
                }
                return container;
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

        private static AutomationElement FindBestComposer(
            AutomationElement root,
            ChatApplication application,
            out AutomationElement conversationComposer,
            out bool isReply)
        {
            System.Windows.Rect rootRect = root.Current.BoundingRectangle;
            AutomationElementCollection edits = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            AutomationElement best = null;
            double bestScore = double.MinValue;
            conversationComposer = null;
            double mainScore = double.MinValue;
            isReply = false;
            foreach (AutomationElement element in edits)
            {
                try
                {
                    System.Windows.Rect rect = element.Current.BoundingRectangle;
                    double score;
                    string name = element.Current.Name;
                    if (!TryScoreCandidate(rect, rootRect, name,
                        element.Current.IsEnabled, element.Current.IsKeyboardFocusable,
                        element.Current.IsOffscreen, application, out score))
                    {
                        continue;
                    }

                    bool reply = application == ChatApplication.Codex && HasReplyHint(name);
                    if (!reply && score > mainScore)
                    {
                        conversationComposer = element;
                        mainScore = score;
                    }
                    if (score > bestScore)
                    {
                        best = element;
                        bestScore = score;
                        isReply = reply;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return best;
        }

        internal static bool TryScoreCandidate(
            System.Windows.Rect rect,
            System.Windows.Rect rootRect,
            string name,
            bool enabled,
            bool focusable,
            bool offscreen,
            out double score)
        {
            return TryScoreCandidate(rect, rootRect, name, enabled, focusable,
                offscreen, ChatApplication.Codex, out score);
        }

        internal static bool TryScoreCandidate(
            System.Windows.Rect rect,
            System.Windows.Rect rootRect,
            string name,
            bool enabled,
            bool focusable,
            bool offscreen,
            ChatApplication application,
            out double score)
        {
            bool claude = application == ChatApplication.Claude;
            bool reply = application == ChatApplication.Codex && HasReplyHint(name);
            score = double.MinValue;
            if (application == ChatApplication.None
                || (claude && !HasClaudeComposerHint(name))
                || rect.IsEmpty || rootRect.IsEmpty
                || !IsScreenCoordinate(rect.Left) || !IsScreenCoordinate(rect.Top)
                || !IsScreenCoordinate(rect.Right) || !IsScreenCoordinate(rect.Bottom)
                || !IsScreenCoordinate(rootRect.Left) || !IsScreenCoordinate(rootRect.Top)
                || !IsScreenCoordinate(rootRect.Right) || !IsScreenCoordinate(rootRect.Bottom)
                || rect.Width < 140 || rect.Height < 18
                || rect.Top < rootRect.Top + (reply ? 55
                    : claude ? Math.Max(55, rootRect.Height * 0.20) : rootRect.Height * 0.50)
                || rect.Bottom > rootRect.Bottom + 2 || rect.Left < rootRect.Left
                || rect.Right > rootRect.Right + 2 || !enabled || !focusable || offscreen)
            {
                return false;
            }

            score = rect.Top - rootRect.Top + Math.Min(rect.Width, 10000);
            if (claude || HasComposerHint(name)) score += 20000;
            if (reply) score += 100000;
            return true;
        }

        private static bool HasClaudeComposerHint(string value)
        {
            string name = (value ?? string.Empty).Trim()
                .TrimEnd('.', ':', '\u2026').TrimEnd().ToLowerInvariant();
            // Restrict Claude to recognizable composer labels. A generic edit or a
            // search/task/editor label must never become a typing-replay destination.
            return name == "reply to claude"
                || name == "message claude"
                || name == "message to claude"
                || name == "ask claude"
                || name == "prompt claude"
                || name == "write a message to claude"
                || name == "\u043d\u0430\u043f\u0438\u0448\u0438\u0442\u0435 \u0437\u0430\u043f\u0440\u043e\u0441 \u0434\u043b\u044f claude"
                || name == "\u0441\u043e\u043e\u0431\u0449\u0435\u043d\u0438\u0435 \u0434\u043b\u044f claude";
        }

        private static bool HasReplyHint(string value)
        {
            string name = (value ?? string.Empty).ToLowerInvariant();
            return name.Contains("answer")
                || name.Contains("response")
                || name.Contains("reply")
                || name.Contains("\u043e\u0442\u0432\u0435\u0442");
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
                pendingRefreshWindow = IntPtr.Zero;
                cachedProcessId = 0;
                replyInput = false;
                hasCompletedRefresh = false;
            }
        }
    }
}
