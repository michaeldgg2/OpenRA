using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using OpenRA.FileSystem;

namespace OpenRA.GameRules
{
	public class RulesetWatcher : IDisposable
	{
		readonly World world;
		readonly ModData modData;
		readonly Dictionary<string, string> watchFiles = new();
		readonly Timer timer;
		readonly HashSet<string> fileQueue = new();
		readonly FileSystemWatcher watcher;

		volatile bool isEnabled;
		bool isDisposed;

		public bool IsEnabled => isEnabled;

		public RulesetWatcher(World world, ModData modData)
		{
			this.world = world;
			this.modData = modData;

			var files = modData.Manifest.Rules
				.Concat(modData.Manifest.Weapons)
				.Concat(modData.Manifest.Sequences);
			foreach (var file in files)
			{
				string filename;
				using (var fs = modData.DefaultFileSystem.Open(file) as FileStream)
					filename = fs?.Name;

				if (string.IsNullOrEmpty(filename))
					continue;

				var fullPath = Path.GetFullPath(filename);
				watchFiles[fullPath] = file;
			}

			timer = new Timer(10) { AutoReset = false };
			timer.Elapsed += RefreshTimer;

			watcher = new FileSystemWatcher(modData.Manifest.Package.Name)
			{
				IncludeSubdirectories = true
			};
			watcher.Changed += FileChanged;

			foreach (var file in watchFiles.Keys.Distinct())
			{
				watcher.Filters.Add(Path.GetFileName(file));
			}
		}

		public void StartWatching()
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(RulesetWatcher));

			watcher.EnableRaisingEvents = true;
			isEnabled = true;
			RestartTimerIfEnabled();
		}

		public void StopWatching()
		{
			if (isDisposed)
				throw new ObjectDisposedException(nameof(RulesetWatcher));

			isEnabled = false;
			timer.Stop();
			watcher.EnableRaisingEvents = false;

			lock (fileQueue)
				fileQueue.Clear();
		}

		void RefreshTimer(object sender, ElapsedEventArgs e)
		{
			if (fileQueue.Count == 0)
			{
				RestartTimerIfEnabled();
				return;
			}

			List<string> copy;
			lock (fileQueue)
			{
				if (fileQueue.Count == 0)
					return;

				copy = fileQueue.ToList();
				fileQueue.Clear();
			}

			if (isDisposed || !isEnabled)
				return;

			Game.RunAfterTick(() =>
			{
				if (isDisposed || !isEnabled)
					return;

				var modFsFilenames = copy.Select(f => watchFiles[f]).ToArray();

				var defaultRules = world.Map.Rules;
				var rulesFiles = FindRulesetFiles(modData.Manifest.Rules, modFsFilenames).ToArray();
				var weaponFiles = FindRulesetFiles(modData.Manifest.Weapons, modFsFilenames).ToArray();
				var sequenceFile = FindRulesetFiles(modData.Manifest.Sequences, modFsFilenames).FirstOrDefault();

				if (rulesFiles.Length > 0)
					defaultRules.LoadActorTraitsFromRuleFile(world, modData, rulesFiles);
				else if (weaponFiles.Length > 0)
					defaultRules.LoadWeaponsFromFile(world, modData, rulesFiles);
				else if (sequenceFile != null)
					world.Map.Sequences.ReloadSequenceSetFromFiles(modData.DefaultFileSystem, sequenceFile);

				RestartTimerIfEnabled();

				IEnumerable<string> FindRulesetFiles(IEnumerable<string> allFiles, IEnumerable<string> findFiles)
				{
					return allFiles.Where(f => findFiles.Any(c => f.Equals(c, StringComparison.OrdinalIgnoreCase))
						&& modData.DefaultFileSystem.Exists(f));
				}
			});
		}

		void RestartTimerIfEnabled()
		{
			if (isEnabled)
				timer.Start();
		}

		void FileChanged(object sender, FileSystemEventArgs e)
		{
			if (!watchFiles.ContainsKey(e.FullPath))
				return;

			lock (fileQueue)
				fileQueue.Add(e.FullPath);
		}

		public void Dispose()
		{
			if (isDisposed)
				return;

			isDisposed = true;

			watcher.Changed -= FileChanged;
			watcher.Dispose();

			timer.Stop();
			timer.Dispose();
		}
	}
}
