/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2021, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using CommandLine;
using CommandLine.Text;
using System.Threading;
using OpenMetaverse;

namespace Radegast
{
    public class CommandLineOptions
    {
        [Option('u', "username", HelpText = "Username, use quotes to supply \"First Last\"")]
        public string Username { get; set; } = string.Empty;

        [Option('p', "password", HelpText = "Account password")]
        public string Password { get; set; } = string.Empty;

        [Option('a', "autologin", HelpText = "Automatically login with provided user credentials")]
        public bool AutoLogin { get; set; } = false;

        [Option('g', "grid", HelpText = "Grid ID to login into, try --list-grids to see IDs used for this parameter")]
        public string Grid { get; set; } = string.Empty;

        [Option('l', "location", HelpText = "Login location: last, home or region name. Region name can also be in format regionname/x/y/z")]
        public string Location { get; set; } = string.Empty;

        [Option("list-grids", HelpText = "Lists grid IDs used for --grid option")]
        public bool ListGrids { get; set; } = false;

        [Option("loginuri", HelpText = "Use this URI to login (don't use with --grid)")]
        public string LoginUri { get; set; } = string.Empty;

        [Option("no-sound", HelpText = "Disable sound")]
        public bool DisableSound { get; set; } = false;


        public HelpText GetHeader()
        {
            HelpText header = new HelpText(Properties.Resources.RadegastTitle)
            {
                AdditionalNewLineAfterOption = true,
                Copyright = new CopyrightInfo("Radegast Development Team, Sjofn LLC", 2009, 2025)
            };
            header.AddPreOptionsLine("https://radegast.life/");
            return header;
        }
    }

    public static class MainProgram
    {
        /// <summary>
        /// Parsed command line options
        /// </summary>
        public static CommandLineOptions s_CommandLineOpts;

        public static BugSplatDotNetStandard.BugSplat s_BugSplat;

        static void RunRadegast(CommandLineOptions args)
        {
            s_CommandLineOpts = args;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Increase the number of IOCP threads available. Mono defaults to a tragically low number
            int workerThreads, iocpThreads;
            ThreadPool.GetMaxThreads(out workerThreads, out iocpThreads);

            if (workerThreads < 500 || iocpThreads < 1000)
            {
                if (workerThreads < 500) workerThreads = 500;
                if (iocpThreads < 1000) iocpThreads = 1000;
                ThreadPool.SetMaxThreads(workerThreads, iocpThreads);
            }

            // Change current working directory to Radegast install dir
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) 
                                          ?? throw new InvalidOperationException());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // See if we only wanted to display list of grids
            if (s_CommandLineOpts.ListGrids)
            {
                Console.WriteLine(s_CommandLineOpts.GetHeader());
                Console.WriteLine();
                GridManager grids = new GridManager();
                Console.WriteLine("Use Grid ID as the parameter for --grid");
                Console.WriteLine("{0,-25} - {1}", "Grid ID", "Grid Name");
                Console.WriteLine(@"========================================================");

                for (int i = 0; i < grids.Count; i++)
                {
                    Console.WriteLine(@"{0,-25} - {1}", grids[i].ID, grids[i].Name);
                }

                Environment.Exit(0);
            }

            // Create main Radegast instance
            RadegastInstance instance = RadegastInstance.GlobalInstance;

            if (!string.IsNullOrEmpty(Generated.BugsplatDatabase))
            {
                s_BugSplat = new BugSplatDotNetStandard.BugSplat(
                Generated.BugsplatDatabase, "Radegast",
                Assembly.GetExecutingAssembly().GetName().Version.ToString())
                {
                    User = string.IsNullOrWhiteSpace(instance.Client.Self.Name) ? "Unknown" : instance.Client.Self.Name,
                    ExceptionType = BugSplatDotNetStandard.BugSplat.ExceptionTypeId.DotNetStandard,
                };
                s_BugSplat.Attributes.Add("platform", Utils.GetRunningPlatform().ToString());
                s_BugSplat.Attributes.Add("runtime", Utils.GetRunningRuntime().ToString());
                s_BugSplat.Attributes.Add("run", (DateTime.Now - instance.StartupTimeUTC).ToString("c"));
                if (instance.GlobalLogFile != null)
                {
                    s_BugSplat.Attachments.Add(new FileInfo(instance.GlobalLogFile));
                }
            }

            Application.Run(instance.MainForm);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs exEventArgs)
        {
            RadegastInstance.GlobalInstance.Client.Network.Logout();
            Exception ex = (Exception)exEventArgs.ExceptionObject;
            s_BugSplat?.Post(ex);
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Core.NativeMethods.Init();
            try
            {
                var parser = new Parser(x => x.HelpWriter = null);
                var result = parser.ParseArguments<CommandLineOptions>(args);
                result.WithParsed(RunRadegast)
                    .WithNotParsed(errs =>
                    {
                        var helpText = HelpText.AutoBuild(result, h =>
                        {
                            h.AdditionalNewLineAfterOption = false;
                            return h;
                        }, e => e);
                        Console.WriteLine(helpText);
                        Console.WriteLine("Use Grid ID as the parameter for --grid");
                        Console.WriteLine("{0,-25} - {1}", "Grid ID", "Grid Name");
                        Console.WriteLine();
                    });
            }
            catch (Exception ex)
            {
                if (System.Diagnostics.Debugger.IsAttached){ throw; }

                string errMsg = "Unhandled " + ex + ": " +
                                ex.Message + Environment.NewLine +
                                ex.StackTrace + Environment.NewLine;

                OpenMetaverse.Logger.Log(errMsg, OpenMetaverse.Helpers.LogLevel.Error);
                
                s_BugSplat?.Post(ex);

                Environment.Exit(1);
            }
        }
    }
}
