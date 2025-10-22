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
using System.IO;
using System.Reflection;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast
{
    public class Grid
    {
        public string ID = string.Empty;
        public string Name = string.Empty;
        public string Platform = string.Empty;
        public string LoginURI = string.Empty;
        public string LoginPage = string.Empty;
        public string HelperURI = string.Empty;
        public string Website = string.Empty;
        public string Support = string.Empty;
        public string Register = string.Empty;
        public string PasswordURL = string.Empty;
        public string Version = "1";

        public Grid() { }

        public Grid(string ID, string name, string loginURI)
        {
            this.ID = ID;
            Name = name;
            LoginURI = loginURI;
        }

        public override string ToString()
        {
            return Name;
        }

        public static Grid FromOSD(OSD data)
        {
            if (data == null || data.Type != OSDType.Map) return null;
            Grid grid = new Grid();
            OSDMap map = (OSDMap)data;

            grid.ID = map["gridnick"].AsString();
            grid.Name = map["gridname"].AsString();
            grid.Platform = map["platform"].AsString();
            grid.LoginURI = map["loginuri"].AsString();
            grid.LoginPage = map["loginpage"].AsString();
            grid.HelperURI = map["helperuri"].AsString();
            grid.Website = map["website"].AsString();
            grid.Support = map["support"].AsString();
            grid.Register = map["register"].AsString();
            grid.PasswordURL = map["password"].AsString();
            grid.Version = map["version"].AsString();

            return grid;
        }
    }

    public class GridManager : IDisposable
    {
        public List<Grid> Grids;

        public GridManager()
        {
            Grids = new List<Grid>();
        }

        public void Dispose()
        {
        }

        public void LoadGrids()
        {
            Grids.Clear();
            Grids.Add(new Grid("agni", "Second Life (agni)", "https://login.agni.lindenlab.com/cgi-bin/login.cgi"));
            Grids.Add(new Grid("aditi", "Second Life Beta (aditi)", "https://login.aditi.lindenlab.com/cgi-bin/login.cgi"));

            try
            {
                string sysGridsFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "grids.xml");
                OSDArray sysGrids = (OSDArray)OSDParser.DeserializeLLSDXml(File.ReadAllText(sysGridsFile));
                foreach (var grid in sysGrids)
                {
                    RegisterGrid(Grid.FromOSD(grid));
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading grid information: {ex.Message}", Helpers.LogLevel.Warning);
            }
        }

        public void RegisterGrid(Grid grid)
        {
            int ix = Grids.FindIndex(g => g.ID == grid.ID);
            if (ix < 0)
            {
                Grids.Add(grid);
            }
            else
            {
                Grids[ix] = grid;
            }
        }

        public Grid this[int ix] => Grids[ix];

        public Grid this[string gridID]
        {
            get
            {
                foreach (Grid grid in Grids)
                {
                    if (grid.ID == gridID) return grid;
                }
                throw new KeyNotFoundException();
            }
        }

        public bool KeyExists(string gridID)
        {
            foreach (Grid grid in Grids)
            {
                if (grid.ID == gridID) return true;
            }
            return false;
        }

        public int Count => Grids.Count;
    }
}
