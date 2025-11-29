/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using OpenMetaverse;
using Radegast.Core.Commands;

namespace Radegast.Commands
{
    public class CommandsManager : ICommandInterpreter
    {
        public static string CmdPrefix = "//";
        public readonly List<IRadegastCommand> CommandsLoaded = new List<IRadegastCommand>();
        public readonly Dictionary<string, IRadegastCommand> CommandsByName = new Dictionary<string, IRadegastCommand>();
        public readonly List<ICommandInterpreter> InterpretersLoaded = new List<ICommandInterpreter>();
        private IRadegastInstance instance;
        public Queue<KeyValuePair<string, ThreadStart>> CommandQueue = new Queue<KeyValuePair<string, ThreadStart>>();
        public AutoResetEvent CommandQueued = new AutoResetEvent(false);

        private readonly CancellationTokenSource commandWorkerCancelToken;

        public CommandsManager(IRadegastInstance inst)
        {
            instance = inst;
            AddCmd("help", "Shows help info", "help help",
                   (name, cmdargs, writeline) =>
                   {
                       string args = string.Join(" ", cmdargs);
                       Help(args, writeline);
                       lock (InterpretersLoaded) foreach (ICommandInterpreter manager in InterpretersLoaded)
                       {
                           manager.Help(args, writeline);
                       }
                   });
            commandWorkerCancelToken = new CancellationTokenSource();
            Task task = Task.Factory.StartNew(CommandWorker, 
                commandWorkerCancelToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void CommandWorker()
        {
            while (true)
            {
                if (commandWorkerCancelToken.Token.IsCancellationRequested) 
                {
                    break;
                }
                if (CommandQueue.Count == 0)
                {
                    CommandQueued.WaitOne();
                }
                if (commandWorkerCancelToken.Token.IsCancellationRequested)
                {
                    break;
                }
                KeyValuePair<string, ThreadStart> todo;
                lock (CommandQueue)
                {
                    if (CommandQueue.Count == 0)
                    {
                        continue;
                    }
                    todo = CommandQueue.Dequeue();
                }
                try
                {

                    todo.Value();
                }
                catch (Exception ex)
                {
                    instance.ShowNotificationInChat(
                        $"Command error: {todo.Key} \n{ex.Message} {ex.StackTrace} ");
                }
            }
            CommandQueue.Clear();
            CommandQueued?.Close();
        }

        public void EnqueueCommand(string name, ThreadStart ts)
        {
            lock (CommandQueue)
                CommandQueue.Enqueue(new KeyValuePair<string, ThreadStart>(name, ts));
            CommandQueued.Set();
        }

        public IRadegastCommand AddCmd(string name, string desc, string usage, CommandExecuteDelegate executeDelegate)
        {
            IRadegastCommand cmd = new GenericRadegastCommand(instance, executeDelegate)
                                       {
                                           Name = name,
                                           Description = name,
                                           Usage = usage
                                       };

            LoadCommand(cmd);
            return cmd;
        }

        public void RemoveCommand(string name)
        {
            lock (CommandsLoaded)
            {
                if (CommandsByName.ContainsKey(name))
                {
                    var cmd = CommandsByName[name];
                    CommandsLoaded.Remove(cmd);
                    CommandsByName.Remove(name);
                    cmd.Dispose();
                }
            }
        }

        public void Help(string args, ConsoleWriteLine WriteLine)
        {
            int found = 0;
            WriteLine("Result of Help : {0}", args);
            foreach (var cmd in instance.CommandsManager.GetCommands())
            {
                if (args != "" && !args.Contains(cmd.Name) && !cmd.Name.Contains(args)) continue;
                WriteLine(" {0}: {1} Usage: {2}{3}", cmd.Name, cmd.Description, CmdPrefix, cmd.Usage);
                found++;
            }
            if (found == 0)
                WriteLine("no help.");
            else
                WriteLine("Listed {0} command{1}.", found, found == 1 ? "" : "s");
        }

        /// <summary>
        /// Loads commands
        /// </summary>
        /// <param name="type">Type to try to load</param>
        /// <returns>True if useful types were found and loaded</returns>
        public bool LoadType(Type type)
        {
            bool loadedType = false;
            
            if (typeof(IRadegastCommand).IsAssignableFrom(type) && type != typeof(RadegastCommand))
            {
                try
                {
                    var c = type.GetConstructor(new Type[] { typeof(RadegastInstance) });
                    if (c != null)
                    {
                        IRadegastCommand plug = (IRadegastCommand)c.Invoke(new object[] { instance });
                        LoadCommand(plug);
                        return true;
                    }
                    c = type.GetConstructor(Type.EmptyTypes);
                    if (c != null)
                    {
                        IRadegastCommand plug = (IRadegastCommand)c.Invoke(Array.Empty<object>());
                        LoadCommand(plug);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"ERROR in Radegast Command: {type} because {ex.Message}", ex);
                    throw;
                }
                return false;
            }

            if (typeof(ICommandInterpreter).IsAssignableFrom(type))
            {
                if (GetType() == type) return false;
                foreach (var ci in type.GetConstructors())
                {
                    if (ci.GetParameters().Length > 0) continue;
                    try
                    {
                        ICommandInterpreter plug = (ICommandInterpreter)ci.Invoke(Array.Empty<object>());
                        LoadInterpreter(plug);
                        loadedType = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"ERROR in Radegast ICommandInterpreter: {type} because {ex.Message}", ex);
                        throw;
                    }
                }
            }

            return loadedType;
        }

        public void LoadInterpreter(ICommandInterpreter interpreter)
        {
            interpreter.StartInterpreter(instance);
            lock (InterpretersLoaded) InterpretersLoaded.Add(interpreter);
        }

        public void LoadCommand(IRadegastCommand command)
        {
            command.StartCommand(instance);
            CommandsByName[command.Name.ToLower()] = command;
            lock (CommandsLoaded) CommandsLoaded.Add(command);
        }

        private void WriteLine(string fmt, object[] args)
        {
            var str = args.Length == 0 ? fmt : string.Format(fmt, args);

            str = str.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).TrimEnd();
            instance.ShowNotificationInChat(str);
        }

        /// <summary>
        /// Queues command for execution
        /// </summary>
        /// <param name="cmdline"></param>
        public void ExecuteCommand(string cmdline)
        {
            ExecuteCommand(WriteLine, cmdline);
        }

        /// <summary>
        /// Queues command for execution
        /// </summary>
        /// <param name="WriteLine"></param>
        /// <param name="cmdline"></param>
        public void ExecuteCommand(ConsoleWriteLine WriteLine, string cmdline)
        {
            EnqueueCommand(cmdline, () => ExecuteCommandForeground(WriteLine, cmdline));
        }

        public void ExecuteCommandForeground(ConsoleWriteLine WriteLine, string cmdline)
        {
            if (cmdline == null) return;
            cmdline = cmdline.Trim();
            List<ICommandInterpreter> interpretersLoadedLocal = new List<ICommandInterpreter>();
            lock (InterpretersLoaded)
                interpretersLoadedLocal.AddRange(InterpretersLoaded);
            foreach (ICommandInterpreter manager in interpretersLoadedLocal)
            {
                if (!manager.IsValidCommand(cmdline)) continue;
                // the command is queued from outside
                manager.ExecuteCommand(WriteLine, cmdline);
                return;
            }

            // our local impl
            while (cmdline.StartsWith(CmdPrefix))
            {
                cmdline = cmdline.Substring(CmdPrefix.Length);
                cmdline = cmdline.Trim();
            }
            if (cmdline == "") return;
            string[] parsd = ParseArguments(cmdline);
            string cmd = parsd[0];
            IRadegastCommand cmdimpl;
            if (CommandsByName.TryGetValue(cmd.ToLower(), out cmdimpl))
            {
                try
                {
                    cmdimpl.Execute(cmd, SplitOff(parsd, 1), WriteLine);
                }
                catch (Exception ex)
                {
                    WriteLine("Command error: {0} \n{1} {2} ", cmdline, ex.Message, ex.StackTrace);
                }
                return;
            }
            WriteLine("Command not found {0}", cmd);
        }

        public void Dispose()
        {
            lock (CommandsLoaded)
            {
                CommandsLoaded.ForEach(plug =>
                {
                    try
                    {
                        plug.Dispose();
                    }
                    catch (Exception) { }
                });
                CommandsLoaded.Clear();
            }
            lock (InterpretersLoaded)
            {
                InterpretersLoaded.ForEach(plug =>
                {
                    try
                    {
                        plug.Dispose();
                    }
                    catch (Exception) { }
                });
                InterpretersLoaded.Clear();
            }
            CommandsByName.Clear();
            commandWorkerCancelToken.Cancel();
            commandWorkerCancelToken.Dispose();
            CommandQueued = null;
        }

        public void StartInterpreter(IRadegastInstance inst)
        {
            instance = inst;
        }

        public void StopInterpreter()
        {
            Dispose();
        }


        public static string[] SplitOff(string[] args, int p)
        {
            string[] newstring = new string[args.Length - p];
            int ci = 0;
            while (p < args.Length)
            {
                newstring[ci] = args[p];
                p++;
                ci++;
            }
            return newstring;
        }

        public static string[] ParseArguments(string str)
        {
            List<string> list = new List<string>();
            StringBuilder current = new StringBuilder();
            string trimmed = null;
            bool withinQuote = false;
            bool escaped = false;

            foreach (char c in str)
            {
                if (c == '"')
                {
                    if (escaped)
                    {
                        current.Append('"');
                        escaped = false;
                    }
                    else
                    {
                        current.Append('"');
                        withinQuote = !withinQuote;
                    }
                }
                else if (c == ' ' || c == '\t')
                {
                    if (escaped || withinQuote)
                    {
                        current.Append(c);
                        escaped = false;
                    }
                    else
                    {
                        trimmed = current.ToString().Trim();
                        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                        {
                            trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        }
                        if (trimmed.Length > 0)
                            list.Add(trimmed);
                        current.Clear();
                    }
                }
                else if (c == '\\')
                {
                    if (escaped)
                    {
                        current.Append('\\');
                        escaped = false;
                    }
                    else
                    {
                        escaped = true;
                    }
                }
                else
                {
                    if (escaped) escaped = false;
                    //throw new FormatException(c.ToString() + " is not an escapable character.");
                    current.Append(c);
                }
            }

            trimmed = current.ToString().Trim();

            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            if (trimmed.Length > 0)
                list.Add(trimmed);

            return list.ToArray();
        }

        public bool IsValidCommand(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            lock (InterpretersLoaded) foreach (ICommandInterpreter manager in InterpretersLoaded)
                    if (manager.IsValidCommand(msg)) return true;
            msg = msg.Trim();
            if (!msg.StartsWith(CmdPrefix)) return false;
            msg = msg.Substring(CmdPrefix.Length);
            return CommandExists(ParseArguments(msg)[0]);
        }

        public bool CommandExists(string cmd)
        {
            lock (CommandsByName) return CommandsByName.ContainsKey(cmd);
        }

        public IEnumerable<IRadegastCommand> GetCommands()
        {
            lock (CommandsLoaded) return new List<IRadegastCommand>(CommandsLoaded);
        }
    }
}