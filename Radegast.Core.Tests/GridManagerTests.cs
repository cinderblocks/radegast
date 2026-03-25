/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
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
using NUnit.Framework;
using OpenMetaverse.StructuredData;

namespace Radegast.Tests
{
    [TestFixture]
    public class GridManagerTests
    {
        private GridManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new GridManager();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.Dispose();
        }

        #region Grid

        [Test]
        public void Grid_ToString_ReturnsName()
        {
            var grid = new Grid("test", "Test Grid", "https://login.test.com");
            Assert.That(grid.ToString(), Is.EqualTo("Test Grid"));
        }

        [Test]
        public void Grid_Constructor_SetsIdNameAndLoginUri()
        {
            var grid = new Grid("myid", "My Grid", "https://login.example.com");
            Assert.Multiple(() =>
            {
                Assert.That(grid.ID, Is.EqualTo("myid"));
                Assert.That(grid.Name, Is.EqualTo("My Grid"));
                Assert.That(grid.LoginURI, Is.EqualTo("https://login.example.com"));
            });
        }

        [Test]
        public void Grid_DefaultConstructor_HasEmptyFields()
        {
            var grid = new Grid();
            Assert.Multiple(() =>
            {
                Assert.That(grid.ID, Is.EqualTo(string.Empty));
                Assert.That(grid.Name, Is.EqualTo(string.Empty));
                Assert.That(grid.LoginURI, Is.EqualTo(string.Empty));
            });
        }

        #endregion

        #region Grid.FromOSD

        [Test]
        public void Grid_FromOSD_NullInput_ReturnsNull()
        {
            Assert.That(Grid.FromOSD(null), Is.Null);
        }

        [Test]
        public void Grid_FromOSD_NonMapOsd_ReturnsNull()
        {
            Assert.That(Grid.FromOSD(OSD.FromString("not a map")), Is.Null);
        }

        [Test]
        public void Grid_FromOSD_ValidMap_PopulatesAllFields()
        {
            var map = new OSDMap
            {
                ["gridnick"] = OSD.FromString("agni"),
                ["gridname"] = OSD.FromString("Second Life"),
                ["platform"] = OSD.FromString("SL"),
                ["loginuri"] = OSD.FromString("https://login.agni.lindenlab.com/cgi-bin/login.cgi"),
                ["loginpage"] = OSD.FromString("https://secondlife.com/"),
                ["helperuri"] = OSD.FromString("https://secondlife.com/"),
                ["website"] = OSD.FromString("https://secondlife.com/"),
                ["support"] = OSD.FromString("https://secondlife.com/"),
                ["register"] = OSD.FromString("https://join.secondlife.com/"),
                ["password"] = OSD.FromString("https://secondlife.com/"),
                ["version"] = OSD.FromString("1")
            };

            Grid grid = Grid.FromOSD(map);
            Assert.Multiple(() =>
            {
                Assert.That(grid, Is.Not.Null);
                Assert.That(grid.ID, Is.EqualTo("agni"));
                Assert.That(grid.Name, Is.EqualTo("Second Life"));
                Assert.That(grid.LoginURI, Is.EqualTo("https://login.agni.lindenlab.com/cgi-bin/login.cgi"));
                Assert.That(grid.Version, Is.EqualTo("1"));
            });
        }

        [Test]
        public void Grid_FromOSD_EmptyMap_ReturnsGridWithEmptyFields()
        {
            Grid grid = Grid.FromOSD(new OSDMap());
            Assert.Multiple(() =>
            {
                Assert.That(grid, Is.Not.Null);
                Assert.That(grid.ID, Is.EqualTo(string.Empty));
                Assert.That(grid.Name, Is.EqualTo(string.Empty));
            });
        }

        #endregion

        #region GridManager initial state

        [Test]
        public void GridManager_InitialState_IsEmpty()
        {
            Assert.That(_manager.Count, Is.EqualTo(0));
        }

        #endregion

        #region RegisterGrid

        [Test]
        public void RegisterGrid_NewGrid_AppearsInListAndCount()
        {
            _manager.RegisterGrid(new Grid("test", "Test", "https://login.test.com"));
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Count, Is.EqualTo(1));
                Assert.That(_manager.KeyExists("test"), Is.True);
            });
        }

        [Test]
        public void RegisterGrid_TwoDistinctGrids_BothPresent()
        {
            _manager.RegisterGrid(new Grid("alpha", "Alpha", "https://alpha.com"));
            _manager.RegisterGrid(new Grid("beta", "Beta", "https://beta.com"));
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Count, Is.EqualTo(2));
                Assert.That(_manager.KeyExists("alpha"), Is.True);
                Assert.That(_manager.KeyExists("beta"), Is.True);
            });
        }

        [Test]
        public void RegisterGrid_DuplicateId_ReplacesExistingAndCountStaysSame()
        {
            _manager.RegisterGrid(new Grid("test", "Old Name", "https://old.com"));
            _manager.RegisterGrid(new Grid("test", "New Name", "https://new.com"));
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Count, Is.EqualTo(1));
                Assert.That(_manager["test"].Name, Is.EqualTo("New Name"));
            });
        }

        #endregion

        #region KeyExists

        [Test]
        public void KeyExists_RegisteredId_ReturnsTrue()
        {
            _manager.RegisterGrid(new Grid("sl", "Second Life", "https://login.sl.com"));
            Assert.That(_manager.KeyExists("sl"), Is.True);
        }

        [Test]
        public void KeyExists_UnknownId_ReturnsFalse()
        {
            Assert.That(_manager.KeyExists("ghost"), Is.False);
        }

        #endregion

        #region Indexers

        [Test]
        public void StringIndexer_KnownId_ReturnsCorrectGrid()
        {
            _manager.RegisterGrid(new Grid("osgrid", "OSgrid", "https://login.osgrid.org"));
            Assert.That(_manager["osgrid"].Name, Is.EqualTo("OSgrid"));
        }

        [Test]
        public void StringIndexer_UnknownId_ThrowsKeyNotFoundException()
        {
            Assert.Throws<KeyNotFoundException>(() => { var _ = _manager["unknown"]; });
        }

        [Test]
        public void IntIndexer_ValidIndex_ReturnsGrid()
        {
            _manager.RegisterGrid(new Grid("first", "First", "https://first.com"));
            Assert.That(_manager[0].ID, Is.EqualTo("first"));
        }

        #endregion
    }
}
