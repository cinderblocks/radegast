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
using System.Threading.Tasks;
using OpenMetaverse;

namespace Radegast
{
    public sealed class SitOnGroundAction : ContextAction
    {
        public SitOnGroundAction(RadegastInstanceForms inst)
            : base(inst)
        {
            Label = "Sit on ground";
            ContextType = typeof(Vector3);
        }
        public override string LabelFor(object target)
        {
            if (Client.Self.Movement.SitOnGround)
            {
                return "Stand up";
            }
            return "Sit on ground";
        }
        public override bool IsEnabled(object target)
        {
            return true;
        }
        public override void OnInvoke(object sender, EventArgs e, object target)
        {
            // Preserve synchronous behavior for callers that expect it by delegating to the async implementation
            TryCatch(() => OnInvokeAsync(sender, e, target).GetAwaiter().GetResult());
        }

        public override async Task OnInvokeAsync(object sender, EventArgs e, object target)
        {
            if (Client.Self.Movement.SitOnGround)
            {
                try { instance.ShowNotificationInChat("Standing up"); } catch { }
                try { Client.Self.Stand(); } catch { }
                return;
            }

            string pname = instance.Names.Get(ToUUID(target));
            if (pname == "(???) (???)") pname = "" + target;

            if (TryFindPos(target, out var sim, out var pos))
            {
                try { instance.ShowNotificationInChat($"Walking to {pname}"); } catch { }
                try { instance.State.MoveTo(sim, pos, false); } catch { }

                // Await arrival without blocking the thread
                double close = 0;
                try
                {
                    close = await instance.State.WaitUntilPositionAsync(StateManager.GlobalPosition(sim, pos), TimeSpan.FromSeconds(5), 1).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    try { Logger.Warn("WaitUntilPositionAsync failed", ex); } catch { }
                }

                if (close > 2)
                {
                    try
                    {
                        instance.ShowNotificationInChat($"Couldn't quite make it to {pname}, now sitting");
                    }
                    catch { }
                }

                try { Client.Self.SitOnGround(); } catch { }
            }
            else
            {
                try { instance.ShowNotificationInChat($"Could not locate {target}"); } catch { }
            }
        }
    }
}