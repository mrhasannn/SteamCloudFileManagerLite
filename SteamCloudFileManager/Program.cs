using System;
using System.Windows.Forms;

namespace SteamCloudFileManager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// Supported command line arguments:
        ///   /appid N    - connect to the given App ID on startup
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Catch unexpected exceptions instead of crashing with the default CLR dialog.
            Application.ThreadException += (s, e) => ShowFatal(e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowFatal(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(GetStartupAppId(args)));
        }

        static void ShowFatal(Exception ex)
        {
            try
            {
                MessageBox.Show("An unexpected error occurred." + Environment.NewLine + (ex == null ? "(unknown)" : ex.Message),
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* message boxes can be unavailable during shutdown */ }
        }

        static string GetStartupAppId(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                string arg = args[i];
                if ((arg == "/appid" || arg == "-appid" || arg == "--appid")
                    && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1].Trim();
                }
            }
            return null;
        }
    }
}
