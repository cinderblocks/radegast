/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using BugSplatDotNetStandard;
using Microsoft.Extensions.Logging;
using OpenMetaverse;
using Radegast.Core;
using Radegast.Veles.Core;

namespace Radegast.Veles;

internal static class Program
{
    internal static BugSplat? BugSplat { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize native libraries (FMOD) before anything else
        NativeMethods.Init();

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RadegastVeles");
        Directory.CreateDirectory(logDir);
        var logFilePath = Path.Combine(logDir, "Veles.log");

        VelesLogProvider.EnableFileLogging(logFilePath);

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new VelesLogProvider());
        });
        Logger.SetLoggerFactory(loggerFactory, "RadegastVeles");

        // Initialize BugSplat if a database has been configured at build time
        if (!string.IsNullOrEmpty(Generated.BugsplatDatabase))
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            BugSplat = new BugSplat(Generated.BugsplatDatabase, "RadegastVeles", version)
            {
                ExceptionType = BugSplat.ExceptionTypeId.DotNetStandard,
            };
            BugSplat.Attachments.Add(new FileInfo(logFilePath));

            // Hook all unhandled exception sources
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException      += OnUnobservedTaskException;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
        {
            BugSplat?.Post(ex);
#if !DEBUG
            ShowCrashWindow(ex);
#endif
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException      -= OnUnobservedTaskException;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            BugSplat?.Post(ex);
#if !DEBUG
            ShowCrashWindow(ex);
#endif
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        BugSplat?.Post(e.Exception);
        e.SetObserved();
#if !DEBUG
        ShowCrashWindow(e.Exception);
#endif
    }

    private static void ShowCrashWindow(Exception ex)
    {
        try
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                DoShow(ex);
            else
                Avalonia.Threading.Dispatcher.UIThread.Invoke(() => DoShow(ex));
        }
        catch
        {
            // Last-resort: if the window itself fails, swallow silently so we don't recurse.
        }

        static void DoShow(Exception ex)
        {
            var win = new Views.CrashWindow(ex);
            win.Show();   // standalone; no owner required
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
