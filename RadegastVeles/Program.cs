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
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using BugSplatDotNetStandard;
using Microsoft.Extensions.Logging;
using LibreMetaverse;
using Radegast.Core;
using Radegast.Veles.Core;

namespace Radegast.Veles;

internal static class Program
{
    internal static BugSplat? BugSplat { get; private set; }

    // Baked in at build time via the BugsplatDatabase MSBuild property (see RadegastVeles.csproj).
    private static string? BugsplatDatabase =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BugsplatDatabase")?.Value;

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

        // On .NET 8+ CoreJ2K no longer discovers image-creator plugins by reflection; each
        // plugin assembly (CoreJ2K.Skia, CoreJ2K.Avalonia) instead self-registers via a
        // [ModuleInitializer]. But module initializers only run when the CLR loads the
        // assembly, and nothing here references a CoreJ2K.Skia/CoreJ2K.Avalonia type at a
        // call site — decodes go through J2kImage...As<SKBitmap>()/DecodeToImage<T>() whose
        // target types live in SkiaSharp/Avalonia — so without this the plugin assemblies
        // never load, ImageFactory finds no creator, and every decode silently returns null
        // (callers treat a failed decode as "texture unavailable"). That is what made avatar
        // bakes render untextured. Force both module initializers to run up front.
        // (CoreJ2K >= 2.3.1.85 made Register idempotent per image type and made resolution
        // tolerate concurrent registration — earlier versions returned null for every
        // SKBitmap decode after a duplicate Register, and could transiently fail decodes
        // that raced a Register, e.g. LibreMetaverse's AssetTexture static ctor registering
        // ManagedImageCreator when the first texture asset arrived mid-decode.)
        RuntimeHelpers.RunModuleConstructor(typeof(CoreJ2K.Util.SKBitmapImageCreator).Module.ModuleHandle);
        RuntimeHelpers.RunModuleConstructor(typeof(CoreJ2K.Util.AvaloniaImageCreator).Module.ModuleHandle);

        // Tune the J2K decode concurrency gate based on GC-visible available RAM.
        // Must run after the logger is configured so the diagnostic line is captured.
        // Read user overrides from the persisted settings file if it already exists;
        // this mirrors the path RadegastInstance.InitializeAppData() uses at runtime.
        var settingsFile = Path.Combine(logDir, "settings.xml");
        double reservedMb  = GridTextureHelper.DefaultDecodeReservedMb;
        double perDecodeMb = GridTextureHelper.DefaultDecodePerDecodeMb;
        try
        {
            if (File.Exists(settingsFile))
            {
                var settingsXml = File.ReadAllText(settingsFile);
                if (LibreMetaverse.StructuredData.OSDParser.DeserializeLLSDXml(settingsXml)
                        is LibreMetaverse.StructuredData.OSDMap map)
                {
                    if (map["decode_reserved_ram_mb"].Type != LibreMetaverse.StructuredData.OSDType.Unknown)
                        reservedMb  = map["decode_reserved_ram_mb"].AsInteger();
                    if (map["decode_per_decode_mb"].Type != LibreMetaverse.StructuredData.OSDType.Unknown)
                        perDecodeMb = map["decode_per_decode_mb"].AsReal();
                }
            }
        }
        catch { /* corrupt or missing settings file; fall through to defaults */ }

        GridTextureHelper.TuneDecodeGateForAvailableRam(reservedMb, perDecodeMb);
        var availableMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        Logger.Log(
            $"J2K decode gate tuned: MaxConcurrentDecodes={GridTextureHelper.MaxConcurrentDecodes} " +
            $"(available RAM: {availableMb:F0} MB, reserved: {reservedMb:F0} MB, per-decode: {perDecodeMb:F1} MB)",
            LogLevel.Information);

        // Initialize BugSplat if a database has been configured at build time
        if (!string.IsNullOrEmpty(BugsplatDatabase))
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            BugSplat = new BugSplat(BugsplatDatabase, "RadegastVeles", version)
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

            // Application is exiting for good here (the Avalonia lifetime call has returned) -
            // flush and tear down the process-wide logger exactly once. This must not happen any
            // earlier (e.g. from GridClient.Dispose() during a reconnect), since Logger is a
            // shared static facility used for the lifetime of the whole process, not any one
            // GridClient.
            Logger.Shutdown();
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
