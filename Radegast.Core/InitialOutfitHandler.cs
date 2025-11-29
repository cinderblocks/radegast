/*
 * Radegast Metaverse Client
 * Copyright(c) 2025, Sjofn, LLC
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
using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// Handles applying an initial outfit when the login response indicates a first login.
    /// </summary>
    public class InitialOutfitHandler : IDisposable
    {
        private readonly IRadegastInstance instance;

        public InitialOutfitHandler(IRadegastInstance instance)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            // Subscribe to client connected which is raised after successful login
            try
            {
                this.instance.NetCom.ClientConnected += NetCom_ClientConnected;
            }
            catch
            {
                // best-effort subscribe; if NetCom isn't available we'll silently no-op
            }
        }

        private void NetCom_ClientConnected(object sender, EventArgs e)
        {
            // Run the check asynchronously to avoid blocking NetCom handlers
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var client = instance.Client;
                    var loginData = client?.Network?.LoginResponseData;

                    if (loginData?.FirstLogin == true && !string.IsNullOrEmpty(loginData.InitialOutfit))
                    {
                        // Unsubscribe so we only handle this once
                        try { instance.NetCom.ClientConnected -= NetCom_ClientConnected; } catch { }

                        try
                        {
                            client.Self.SetAgentAccess("A");
                            var initOutfit = new InitialOutfit(instance);
                            initOutfit.SetInitialOutfit(loginData.InitialOutfit);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("InitialOutfitHandler: failed to apply initial outfit: " + ex.Message, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("InitialOutfitHandler: unexpected error: " + ex.Message, ex);
                }
            });
        }

        public void Dispose()
        {
            try { instance.NetCom.ClientConnected -= NetCom_ClientConnected; } catch { }
        }
    }
}
