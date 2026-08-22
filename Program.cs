using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal static class Program
    {
        private const string MutexName = "Local\\CodexKeyboardScroll-7D35DB7D-3062-43BD-8D23-85802F58C820";

        [STAThread]
        private static int Main(string[] args)
        {
            // Let Windows select current secure protocols. Without an explicit
            // .NET 4.8 runtime contract, standalone csc builds default to TLS 1.0.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return SelfTests.Run();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var context = new ScrollApplicationContext())
                {
                    Application.Run(context);
                }
            }

            return 0;
        }
    }
}
