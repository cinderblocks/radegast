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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using BugSplatDotNetStandard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LibreMetaverse;
using Radegast.Core;
using Radegast.Veles.Core;

namespace Radegast.Veles;

internal static class Program
{
    internal static BugSplat? BugSplat { get; private set; }

#if DEBUG
    private const LogLevel DefaultLogLevel = LogLevel.Debug;
#else
    private const LogLevel DefaultLogLevel = LogLevel.Information;
#endif

    /// <summary>
    /// The lowest level that is logged: Debug in debug builds, Information in release builds.
    /// VELES_LOG_LEVEL overrides it with any <see cref="LogLevel"/> name, e.g. "Debug" or "Warning".
    /// </summary>
    private static LogLevel ResolveLogLevel()
    {
        var requested = Environment.GetEnvironmentVariable("VELES_LOG_LEVEL");
        return Enum.TryParse<LogLevel>(requested, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : DefaultLogLevel;
    }

    // Baked in at build time via the BugsplatDatabase MSBuild property (see RadegastVeles.csproj).
    private static string? BugsplatDatabase =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BugsplatDatabase")?.Value;

    /// <summary>The application's logger factory, for components that take an <see cref="ILogger"/>.</summary>
    public static ILoggerFactory LogFactory { get; private set; } = NullLoggerFactory.Instance;

    /// <summary>
    /// A command from the command line that no running copy took, to be carried out by this
    /// copy once it has started.
    /// </summary>
    internal static AppCommand? StartupCommand { get; private set; }

    /// <summary>The options this copy was started with.</summary>
    internal static LaunchOptions LaunchOptions { get; private set; } = LaunchOptions.Parse([]);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    /// <summary>
    /// Handles the options that print something and exit. Returns true if the program is done.
    /// </summary>
    private static bool RunConsoleOptions(LaunchOptions options)
    {
        if (!options.ShowHelp && !options.ShowVersion && !options.ListGrids && options.Errors.Count == 0)
            return false;

        // A Windows GUI program has no console of its own; borrow the one it was started from.
        if (OperatingSystem.IsWindows() && AttachConsole(-1))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }

        foreach (var error in options.Errors)
            Console.Error.WriteLine(error);

        if (options.ShowHelp)
        {
            Console.WriteLine(LaunchOptions.HelpText);
            return true;
        }
        if (options.ShowVersion)
        {
            Console.WriteLine("Radegast Veles " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"));
            return true;
        }
        if (options.ListGrids)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var grids = new GridManager();
            grids.LoadGrids();
            Console.WriteLine($"{"Grid ID",-25} Grid Name");
            foreach (var grid in grids.Grids)
                Console.WriteLine($"{grid.ID,-25} {grid.Name}");
            return true;
        }

        Console.Error.WriteLine("Run with --help for the list of options.");
        return false;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);
        if (RunConsoleOptions(options)) return;
        if (options.PasswordFromStandardInput) options.SupplyPassword(Console.In.ReadLine());
        LaunchOptions = options;

        if (AppCommand.TryParse(args, out var command))
        {
            if (AppCommandChannel.TrySend(AppCommandChannel.DefaultEndpoint(), command)) return;
            // With no running copy there is nothing to exit or log out; the rest start the program.
            if (command.Kind is AppCommandKind.Exit or AppCommandKind.LogoutSession) return;
            StartupCommand = command;
        }

        // Legacy Radegast (Radegast/Program.cs) does the same for the same reason: several
        // pieces of shared code (LibreMetaverse.LslTools' LslSyntax, shader_data lookups, etc.)
        // resolve their own asset paths relative to the current working directory rather than
        // AppContext.BaseDirectory. On Windows that's usually a no-op since double-clicking an
        // exe already sets CWD to its own folder, but macOS/Linux launches (Terminal, LaunchServices,
        // launchd) make no such guarantee -- without this, those lookups silently fail whenever
        // the app isn't launched from inside its own install directory.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

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
            builder.SetMinimumLevel(ResolveLogLevel());
            builder.AddProvider(new VelesLogProvider());
            builder.AddProvider(new SeedCapsRetryGuard());
        });
        Logger.SetLoggerFactory(loggerFactory, "RadegastVeles");
        LogFactory = loggerFactory;

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
            $"(available RAM: {availableMb:F0} MB, reserved: {reservedMb:F0} MB, per-decode: {perDecodeMb:F1} MB, " +
            $"ProcessorCount={Environment.ProcessorCount}, reservedCores={GridTextureHelper.DefaultDecodeReservedCores})",
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
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException      += OnUnobservedTaskException;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
        {
            Logger.Critical("Unhandled exception on the UI thread; Veles is closing.", ex);
            VelesLogProvider.Flush();
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
            Logger.Critical($"Unhandled exception (terminating: {e.IsTerminating}).", ex);
            VelesLogProvider.Flush();
            BugSplat?.Post(ex);
#if !DEBUG
            ShowCrashWindow(ex);
#endif
        }
    }

    // A background task failed and nothing awaited it. The process keeps running, so this is
    // recorded and reported but is not presented to the user as a crash.
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Logger.Error("A background task failed and its error was never observed.", e.Exception);
        BugSplat?.Post(e.Exception);
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
