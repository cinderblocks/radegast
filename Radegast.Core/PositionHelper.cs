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

using OpenMetaverse;

namespace Radegast
{
    /// <summary>
    /// Helper class for position and location calculations
    /// </summary>
    /// <remarks>This may end up in LibreMetaverse</remarks>
    public static class PositionHelper
    {
        /// <summary>
        /// Convert simulator handle and local position to global position
        /// </summary>
        public static Vector3d ToGlobalPosition(ulong simHandle, Vector3 localPos)
        {
            Utils.LongToUInts(simHandle, out var globalX, out var globalY);

            return new Vector3d(
                globalX + localPos.X,
                globalY + localPos.Y,
                localPos.Z);
        }

        /// <summary>
        /// Convert global position to local position for a specific simulator
        /// </summary>
        public static Vector3 ToLocalPosition(ulong simHandle, Vector3d globalPos)
        {
            Utils.LongToUInts(simHandle, out var globalX, out var globalY);

            return new Vector3(
                (float)(globalPos.X - globalX),
                (float)(globalPos.Y - globalY),
                (float)globalPos.Z);
        }

        /// <summary>
        /// Get global position from a simulator and local position
        /// </summary>
        public static Vector3d GlobalPosition(Simulator sim, Vector3 localPos)
        {
            if (sim == null) return Vector3d.Zero;
            return ToGlobalPosition(sim.Handle, localPos);
        }

        /// <summary>
        /// Get global position from a primitive
        /// </summary>
        public static Vector3d GlobalPosition(Primitive prim, Simulator currentSim)
        {
            if (prim == null || currentSim == null) return Vector3d.Zero;
            return GlobalPosition(currentSim, prim.Position);
        }

        /// <summary>
        /// Calculate the position of an avatar accounting for parent prim (if sitting)
        /// </summary>
        public static Vector3 GetAvatarPosition(Simulator sim, Avatar avatar)
        {
            if (avatar == null) return Vector3.Zero;

            if (avatar.ParentID == 0)
            {
                return avatar.Position;
            }

            if (sim?.ObjectsPrimitives.TryGetValue(avatar.ParentID, out var prim) == true)
            {
                return prim.Position + avatar.Position * prim.Rotation;
            }

            return avatar.Position;
        }

        /// <summary>
        /// Calculate the position of a primitive accounting for parent prim
        /// </summary>
        public static Vector3 GetPrimPosition(Simulator sim, Primitive prim)
        {
            if (prim == null) return Vector3.Zero;

            if (prim.ParentID == 0)
            {
                return prim.Position;
            }

            if (sim?.ObjectsPrimitives.TryGetValue(prim.ParentID, out var parent) == true)
            {
                return parent.Position + prim.Position * parent.Rotation;
            }

            return prim.Position;
        }

        /// <summary>
        /// Format region coordinates as a display string
        /// </summary>
        public static string FormatRegionCoordinates(string regionName, int? x = null, int? y = null, int? z = null)
        {
            return regionName + Utilities.FormatCoordinates(x, y, z);
        }
    }
}
