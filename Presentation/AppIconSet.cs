using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CodexKeyboardScroll
{
    internal sealed class AppIconSet : IDisposable
    {
        private const string ActiveResource = "CodexKeyboardScroll.Assets.Active.ico";
        private const string WaitingResource = "CodexKeyboardScroll.Assets.Waiting.ico";

        internal AppIconSet()
        {
            Active = Load(ActiveResource);
            Waiting = Load(WaitingResource);
        }

        internal Icon Active { get; private set; }
        internal Icon Waiting { get; private set; }

        public void Dispose()
        {
            Active.Dispose();
            Waiting.Dispose();
        }

        private static Icon Load(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Missing icon resource: " + resourceName);
                }
                using (var icon = new Icon(stream))
                {
                    return (Icon)icon.Clone();
                }
            }
        }
    }
}
