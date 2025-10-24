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
using System.Threading;
using System.Text;
using OpenMetaverse;

namespace Radegast.Commands
{
    public sealed class ParcelInfoCommand : RadegastCommand
    {
        private readonly ManualResetEvent ParcelsDownloaded = new ManualResetEvent(false);
        private readonly RadegastInstanceForms instance;

        public ParcelInfoCommand(IRadegastInstance instance)
            : base(instance)
        {
            Name = "parcelinfo";
            Description = "Prints out info about all the parcels in this simulator";
            Usage = Name;

            this.instance = (RadegastInstanceForms)instance;
            this.instance.NetCom.ClientDisconnected += Netcom_ClientDisconnected;
        }

        public override void Dispose()
        {
            base.Dispose();
            instance.NetCom.ClientDisconnected -= Netcom_ClientDisconnected;
        }

        public override void Execute(string name, string[] cmdArgs, ConsoleWriteLine WriteLine)
        {
            StringBuilder sb = new StringBuilder();
            string result;

            EventHandler<SimParcelsDownloadedEventArgs> del = delegate(object sender, SimParcelsDownloadedEventArgs e)
            {
                ParcelsDownloaded.Set();
            };

            instance.MainForm.PreventParcelUpdate = true;

            ParcelsDownloaded.Reset();
            Client.Parcels.SimParcelsDownloaded += del;
            Client.Parcels.RequestAllSimParcels(Client.Network.CurrentSim, true, TimeSpan.FromMilliseconds(750));

            if (Client.Network.CurrentSim.IsParcelMapFull())
                ParcelsDownloaded.Set();

            if (ParcelsDownloaded.WaitOne(30000, false) && Client.Network.Connected)
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
                result = "Failed to retrieve information on all the simulator parcels";

            Client.Parcels.SimParcelsDownloaded -= del;

            Thread.Sleep(2000);
            instance.MainForm.PreventParcelUpdate = false;

            WriteLine("Parcel Infro results:\n{0}", result);
        }

        private void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            ParcelsDownloaded.Set();
        }
    }
}
