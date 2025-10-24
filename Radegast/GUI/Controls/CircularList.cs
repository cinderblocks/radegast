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

using System.Collections.Generic;

namespace Radegast
{
    /// <summary>
    /// A list which acts like the List class, but with more of a java
    /// influence. This allows you to set
    /// a looping variable to true, and creates
    /// a circular list. Also it utilizes the java iterator pattern of
    /// Next and HasNext.
    /// 
    /// Author: Wesley Tansey
    /// </summary>
    /// <typeparam name="T">The type of item that
    /// will be stored in the list</typeparam>

    public class CircularList<T> : List<T>
    {
        #region Member Variables

        private int index;
        #endregion

        #region Properties

        /// <summary>
        /// If true, the list will loop to the beginning when Next
        /// is called after the last element has been accessed.
        /// </summary>
        public bool Loop { get; set; } = true;

        /// <summary>
        /// The next element in the list. The user is responsible for
        /// making sure that HasNext is true
        /// before getting the next element.
        /// </summary>

        public T Next
        {
            get
            {
                if (index >= Count)
                {
                    if (!Loop)
                    {
                        return default(T);
                    }
                    index = 0;
                }
                return this[index++];
            }
        }

        /// <summary>
        /// Tells whether there is another element in the list
        /// </summary>
        public bool HasNext
        {
            get
            {
                if (Count == 0 || (!Loop && index >= Count))
                {
                    return false;
                }
                return true;
            }
        }
        #endregion
    }
}
