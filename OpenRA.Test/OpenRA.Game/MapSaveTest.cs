#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	[NonParallelizable]
	sealed class MapSaveTest
	{
		sealed class MemoryPackage : IReadWritePackage
		{
			public readonly Dictionary<string, byte[]> Files = [];
			public readonly List<string> Updates = [];
			public string Name => "MapSaveTest";
			public IEnumerable<string> Contents => Files.Keys;
			public bool Contains(string filename) => Files.ContainsKey(filename);
			public Stream GetStream(string filename) => Files.TryGetValue(filename, out var data) ? new MemoryStream(data) : null;
			public IReadOnlyPackage OpenPackage(string filename, FileSystem.FileSystem context) => null;
			public void Delete(string filename) => Files.Remove(filename);
			public void Dispose() { }

			public void Update(string filename, byte[] contents)
			{
				Files[filename] = contents;
				Updates.Add(filename);
			}
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		public void SaveWritesStagedFilesAndMapData(int stagedFileCount)
		{
			Log.AddChannel("perf", null);
			Log.AddChannel("debug", null);
			using var modPackage = new MemoryPackage();
			modPackage.Update("mod.yaml", Encoding.UTF8.GetBytes(
@"Metadata:
	Title: Map save test
	Version: test
FileSystem: DefaultFileSystem
	Packages:
LoadScreen:
Assemblies: OpenRA.Mods.Common.dll
SpriteSequenceFormat: DefaultSpriteSequence
TerrainFormat: DefaultTerrain
TileSets:
	terrain.yaml:
MapGrid:
"));
			modPackage.Update("terrain.yaml", Encoding.UTF8.GetBytes(
@"General:
	Id: TEST
Terrain:
	TerrainType@Clear:
		Type: Clear
Templates:
	Template@0:
		Id: 0
		Size: 1,1
		Tiles:
			0: Clear
"));

			var previousSettings = Game.Settings;
			try
			{
				Game.Settings = new Settings(null, new Arguments([]));
				using var modData = new ModData(new Manifest("test", modPackage), new InstalledMods([], []));
				modData.ModFiles.Mount(modPackage);
				using var map = new Map(modData, modData.DefaultTerrainInfo["TEST"], new Size(2, 2)) { LockPreview = true };
				Assert.That(map.InvalidCustomRules, Is.False);
				using var package = new MemoryPackage();
				string[] filenames = ["rules.yaml", "map.png"];
				byte[][] payloads = [[1, 2, 3], [4, 5, 6]];
				for (var i = 0; i < stagedFileCount; i++)
					map.StagedMapFiles.Add(filenames[i], payloads[i]);

				map.Save(package);

				Assert.That(package.Contents, Is.EquivalentTo(filenames.Take(stagedFileCount).Concat(["map.yaml", "map.bin"])));
				Assert.That(package.Updates.Take(stagedFileCount), Is.EqualTo(filenames.Take(stagedFileCount).Order()));
				for (var i = 0; i < stagedFileCount; i++)
					Assert.That(package.Files[filenames[i]], Is.EqualTo(payloads[i]));
				Assert.That(package.Files["map.yaml"], Is.Not.Empty);
				Assert.That(package.Files["map.bin"], Is.EqualTo(map.SaveBinaryData()));
				Assert.That(map.Package, Is.SameAs(package));
				Assert.That(map.Uid, Is.EqualTo(Map.ComputeUID(package)));

				using var reloaded = new Map(modData, package);
				Assert.That(reloaded.MapSize, Is.EqualTo(map.MapSize));
				Assert.That(reloaded.SaveBinaryData(), Is.EqualTo(map.SaveBinaryData()));

				package.Update("existing.txt", [7, 8, 9]);
				map.Save(package);
				Assert.That(package.Files["existing.txt"], Is.EqualTo(new byte[] { 7, 8, 9 }));
				using var copy = new MemoryPackage();
				map.Save(copy);
				Assert.That(copy.Contents, Is.EquivalentTo(package.Contents));
				foreach (var filename in package.Contents)
					Assert.That(copy.Files[filename], Is.EqualTo(package.Files[filename]));
			}
			finally
			{
				Game.Settings = previousSettings;
			}
		}
	}
}
