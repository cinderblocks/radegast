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
using System.Text;
using OpenMetaverse;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Radegast.Commands
{
    public sealed class ParcelInfoCommand : RadegastCommand
    {
        private readonly RadegastInstanceForms instance;

        public ParcelInfoCommand(IRadegastInstance instance)
            : base(instance)
        {
            Name = "parcelinfo";
            Description = "Prints out info about all the parcels in this simulator";
            Usage = Name;

            this.instance = (RadegastInstanceForms)instance;
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            // Run the long-running network request asynchronously so we don't block the UI thread
            _ = Task.Run(async () =>
            {
                StringBuilder sb = new StringBuilder();

                EventHandler<SimParcelsDownloadedEventArgs> del = null;
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    instance.MainForm.PreventParcelUpdate = true;

                    del = (object sender, SimParcelsDownloadedEventArgs e) =>
                    {
                        // signal completion
                        tcs.TrySetResult(true);
                    };

                    Client.Parcels.SimParcelsDownloaded += del;

                    await Client.Parcels.RequestAllSimParcelsAsync(Client.Network.CurrentSim, true, TimeSpan.FromMilliseconds(750));

                    if (Client.Network.CurrentSim.IsParcelMapFull())
                        tcs.TrySetResult(true);

                    // Wait up to 30 seconds for the parcels to download
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);

                    string result;
                    if (completed == tcs.Task && Client.Network.Connected)
                    {
                        sb.AppendFormat("Downloaded {0} Parcels in {1} " + Environment.NewLine,
                            Client.Network.CurrentSim.Parcels.Count, Client.Network.CurrentSim.Name);

                        Client.Network.CurrentSim.Parcels.ForEach(delegate(Parcel parcel)
                        {
                            sb.AppendFormat("Parcel[{0}]: Name: \"{1}\", Description: \"{2}\" ACLBlacklist Count: {3}, ACLWhiteList Count: {5} Traffic: {4}" + Environment.NewLine,
                                parcel.LocalID, parcel.Name, parcel.Desc, parcel.AccessBlackList.Count, parcel.Dwell, parcel.AccessWhiteList.Count);
                        });

                        result = sb.ToString();
                    }
                    else
                    {
                        result = "Failed to retrieve information on all the simulator parcels";
                    }

                    // Output results (WriteLine may be UI-bound; call it on threadpool to be safe)
                    try
                    {
                        WriteLine("Parcel Info results:\n{0}", result);
                    }
                    catch
                    {
                        // swallow to avoid throwing from background task
                    }
                }
                catch (Exception ex)
                {
                    try { WriteLine("Parcel Info error: {0}", ex.ToString()); } catch { }
                }
                finally
                {
                    if (del != null) Client.Parcels.SimParcelsDownloaded -= del;
                    // Ensure we unset PreventParcelUpdate on the UI thread
                    try
                    {
                        if (instance.MainForm != null && instance.MainForm.IsHandleCreated)
                        {
                            instance.MainForm.BeginInvoke(new MethodInvoker(() => instance.MainForm.PreventParcelUpdate = false));
                        }
                        else
                        {
                            instance.MainForm.PreventParcelUpdate = false;
                        }
                    }
                    catch
                    {
                        try { instance.MainForm.PreventParcelUpdate = false; } catch { }
                    }
                }
            });
        }
    }
}
