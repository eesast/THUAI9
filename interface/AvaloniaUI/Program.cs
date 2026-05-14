using Avalonia;
using System;
using System.IO;

namespace THUAI9_Avalonia
{
    public class Program
    {
        private const string AvaloniaReadyFileEnvironmentVariable = "THUAI9_AVALONIA_READY_FILE";

        [STAThread]
        public static void Main(string[] args)
        {
            string[] appArgs = ApplySmokeReadyFileArgument(args);
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(appArgs);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static string[] ApplySmokeReadyFileArgument(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] appArgs = new string[args.Length];
            int appArgCount = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                string? readyFile = null;
                if (arg.StartsWith("--readyFile=", StringComparison.OrdinalIgnoreCase))
                {
                    readyFile = arg.Substring("--readyFile=".Length);
                }
                else if (arg.Equals("--readyFile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    readyFile = args[i + 1];
                    i++;
                }

                if (string.IsNullOrWhiteSpace(readyFile))
                {
                    appArgs[appArgCount++] = arg;
                    continue;
                }

                Environment.SetEnvironmentVariable(
                    AvaloniaReadyFileEnvironmentVariable,
                    Path.GetFullPath(readyFile.Trim().Trim('"')));
            }

            if (appArgCount == appArgs.Length)
            {
                return appArgs;
            }

            Array.Resize(ref appArgs, appArgCount);
            return appArgs;
        }
    }
}
