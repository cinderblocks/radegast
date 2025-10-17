/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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

namespace Radegast.Commands
{
    public class ThreadCommand : IRadegastCommand
    {
        private List<Thread> _listed = null;
        List<Thread> _commandThreads = new List<Thread>();
        Queue<KeyValuePair<string, ThreadStart>> _commandQueue => instance.CommandsManager.CommandQueue;
        private RadegastInstance instance;
        public string Name => "thread";

        public string Description => "Runs a command in a thread";

        public string Usage => "thread <long running command>";

        public void StartCommand(RadegastInstance inst)
        {
            instance = inst;
            instance.CommandsManager.CommandsByName.Add("kill", this);
            instance.CommandsManager.CommandsByName.Add("threads", this);
        }

        public void Dispose()
        {
            foreach (var cmd in _commandThreads)
            {
                try
                {
                    cmd.Abort();
                }
                catch (Exception)
                {
                }
            }
            instance = null;
        }

        public void Execute(string threadCmd, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            string args = String.Join(" ", cmdArgs);
            if (threadCmd == "kill")
            {
                if (_listed == null)
                {
                    WriteLine("Must load the threadlist Using: thread");
                    return;
                }
                lock (_listed)
                {
                    if (_listed.Count == 0)
                    {
                        WriteLine("No threaded commands to kill.");
                    }
                    else
                    {
                        WriteLine("Killing all threaded commands");
                        int found = 0;
                        foreach (var cmd in _listed)
                        {
                            try
                            {
                                if (cmd.IsAlive)
                                {
                                    found++;
                                    cmd.Abort();
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                        WriteLine("Killed {0} thread{1}.", found, found == 1 ? "" : "s");
                        _listed = null;

                    }
                }
                lock (_commandQueue)
                {
                    int commandQueueCount = _commandQueue.Count;
                    if (commandQueueCount == 0)
                    {
                        WriteLine("No queued commands.");
                    }
                    else
                    {
                        _commandQueue.Clear();
                        WriteLine("Clear queued commands: " + commandQueueCount);
                    }
                }
                return;
            }

            //cleared each time becasue the list will change 
            _listed = null;
            if (args == string.Empty)
            {
                lock (_commandThreads) _listed = new List<Thread>(_commandThreads);
                if (_listed.Count == 0)
                {
                    WriteLine("No threaded commands.");
                }
                else
                {
                    int found = _listed.Count;
                    WriteLine("Listing {0} thread{1}.", found, found == 1 ? "" : "s");
                    for (int i = 0; i < _listed.Count; i++)
                    {
                        WriteLine(" * {0}: {1}", i, _listed[i].Name);
                    }
                }
                lock (_commandQueue)
                {
                    if (_commandQueue.Count == 0)
                    {
                        WriteLine("No queued commands.");
                    }
                    else
                    {
                        WriteLine("Queued commands: {0}", _commandQueue.Count);
                        foreach (var c in _commandQueue)
                        {
                            WriteLine(" Q: {0}", c.Key);
                        }
                    }
                }
                return;
            }
            string name = string.Format("ThreadCommand {0}: {1}", DateTime.Now, args);
            Thread thread = new Thread(() =>
                                           {
                                               try
                                               {
                                                   WriteLine("Started command: {0}", args);
                                                   instance.CommandsManager.ExecuteCommandForeground(WriteLine, args);
                                               }
                                               catch (Exception e)
                                               {
                                                   WriteLine("Exception: " + name + "\n" + e);
                                               }
                                               finally
                                               {
                                                   WriteLine("Done with {0}", args);
                                                   _commandThreads.Remove(Thread.CurrentThread);
                                               }
                                           }) {Name = name};
            lock (_commandThreads) _commandThreads.Add(thread);
            thread.Start();
        }
    }

    public sealed class PauseCommand : RadegastCommand
    {
        public PauseCommand(RadegastInstance inst)
            : base(inst)
        {
            Name = "pause";
            Description = "pauses command script execution";
            Usage = "pause [seconds]";
        }
        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            float time;
            if (cmdArgs.Length == 0 || !float.TryParse(cmdArgs[0], out time))
                WriteLine(Usage);
            else
            {
                WriteLine("pausing for " + time);
                Thread.Sleep((int)(time * 1000));
                WriteLine("paused for " + time);
            }
        }
    }
}