/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Formats.Tar;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Tests;

[TestFixture]
public class PiperVoiceArchiveTests
{
    private string _root = string.Empty;
    private string _target = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "veles-tests-" + Guid.NewGuid().ToString("N"));
        _target = Path.Combine(_root, "voice");
        Directory.CreateDirectory(_target);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteTar(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_root, "archive.tar");
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, TarEntryFormat.Pax);
        foreach (var (name, content) in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
            };
            writer.WriteEntry(entry);
        }
        return path;
    }

    [TestCase("model.onnx")]
    [TestCase("espeak-ng-data/en_dict")]
    [TestCase("a/b/../c.txt")]
    public void EntryInsideTheTargetIsAccepted(string relative)
    {
        var resolved = PiperVoiceDownloaderViewModel.ResolveEntryPath(_target, relative);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved, Does.StartWith(Path.GetFullPath(_target) + Path.DirectorySeparatorChar));
    }

    [TestCase("../evil.txt")]
    [TestCase("a/../../evil.txt")]
    [TestCase("/etc/evil.txt")]
    [TestCase("")]
    public void EntryOutsideTheTargetIsRefused(string relative)
    {
        Assert.That(PiperVoiceDownloaderViewModel.ResolveEntryPath(_target, relative), Is.Null);
    }

    [Test]
    public void SiblingFolderWithTheSamePrefixIsRefused()
    {
        Assert.That(PiperVoiceDownloaderViewModel.ResolveEntryPath(_target, "../voice-other/x.txt"), Is.Null);
    }

    [Test]
    public void ExtractStripsTheTopLevelFolder()
    {
        string archive = WriteTar(("vits-piper-test/model.onnx", "model"),
                                  ("vits-piper-test/espeak-ng-data/en_dict", "dict"));

        PiperVoiceDownloaderViewModel.ExtractArchive(archive, _target, null, CancellationToken.None);

        Assert.That(File.ReadAllText(Path.Combine(_target, "model.onnx")), Is.EqualTo("model"));
        Assert.That(File.ReadAllText(Path.Combine(_target, "espeak-ng-data", "en_dict")), Is.EqualTo("dict"));
    }

    [Test]
    public void ExtractRefusesAnEntryThatClimbsOut()
    {
        string archive = WriteTar(("vits-piper-test/../../evil.txt", "evil"));

        Assert.Throws<InvalidDataException>(() =>
            PiperVoiceDownloaderViewModel.ExtractArchive(archive, _target, null, CancellationToken.None));
        Assert.That(File.Exists(Path.Combine(_root, "evil.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt")), Is.False);
    }

    [Test]
    public void ExtractStopsWhenCancelled()
    {
        string archive = WriteTar(("vits-piper-test/model.onnx", "model"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PiperVoiceDownloaderViewModel.ExtractArchive(archive, _target, null, cts.Token));
        Assert.That(File.Exists(Path.Combine(_target, "model.onnx")), Is.False);
    }
}
