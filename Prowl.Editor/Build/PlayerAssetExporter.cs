using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;


using Infinity.Echo;
using Infinity.Editor.Projects;
using Infinity.Editor.Projects.Settings;
using Infinity.Runtime;

namespace Infinity.Editor.Build;

/// <summary>
/// Writes the assets a player loads: the imported cache files, the manifest that names them, and either loose
/// files or .prowlpak archives. The player never reads the project's Assets/ folder or its import settings.
/// </summary>
public static class PlayerAssetExporter
{
    private sealed class ExportEntry
    {
        public Guid Guid;
        public string Path = "";
        public string TypeName = "";
        public string CachePath = "";
        public long Size;
        public Guid[] Hard = [];
        public Guid[] Soft = [];
    }

    public static int Export(Project project, BuildSettings settings, string contentDir, IReadOnlyList<Guid> scenes,
        Action<string>? report = null)
    {
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null)
            throw new InvalidOperationException("The asset database is not initialized.");

        var entries = new List<ExportEntry>();
        foreach (AssetEntry entry in backend.GetAllEntries())
        {
            Add(entries, project, backend, entry.Guid, entry.Path, entry.MainAssetTypeName ?? "",
                entry.Dependencies, entry.SoftDependencies);
            foreach (SubAssetEntry sub in entry.SubAssets)
                Add(entries, project, backend, sub.Guid, $"{entry.Path}#{sub.Name}", sub.TypeName,
                    sub.Dependencies, sub.SoftDependencies);
        }

        report?.Invoke($"Exporting {entries.Count} assets");
        Directory.CreateDirectory(contentDir);
        Directory.CreateDirectory(Path.Combine(contentDir, "Assets"));

        var shipped = new List<ExportEntry>(entries.Count);
        foreach (ExportEntry entry in entries)
        {
            if (!File.Exists(entry.CachePath)) continue;
            shipped.Add(entry);
        }

        if (settings.PackagingMode == AssetPackagingMode.ProwlPak)
            WritePaks(shipped, contentDir, Math.Max(1, settings.MaxPakSizeMB));
        else
            foreach (ExportEntry entry in shipped)
                File.Copy(entry.CachePath, Path.Combine(contentDir, "Assets", $"{entry.Guid}.asset"), overwrite: true);

        WriteManifest(contentDir, scenes, shipped, backend);
        return shipped.Count;
    }

    private static void Add(List<ExportEntry> entries, Project project, EditorAssetBackend backend, Guid guid,
        string path, string typeName, Guid[] hard, Guid[] soft)
    {
        string cache = Path.Combine(project.CachePath, $"{guid}.asset");
        if (!File.Exists(cache)) return;

        entries.Add(new ExportEntry
        {
            Guid = guid,
            Path = path,
            TypeName = typeName,
            CachePath = cache,
            Size = new FileInfo(cache).Length,
            Hard = hard,
            Soft = soft,
        });
    }

    private static void WritePaks(List<ExportEntry> entries, string contentDir, int maxSizeMb)
    {
        long maxBytes = (long)maxSizeMb * 1024 * 1024;
        int pakIndex = 0;
        long currentSize = 0;
        ZipArchive? pak = null;

        try
        {
            foreach (ExportEntry entry in entries)
            {
                if (pak == null || (currentSize > 0 && currentSize + entry.Size > maxBytes))
                {
                    pak?.Dispose();
                    string pakPath = Path.Combine(contentDir, $"content_{pakIndex++}.prowlpak");
                    pak = ZipFile.Open(pakPath, ZipArchiveMode.Create);
                    currentSize = 0;
                }

                ZipArchiveEntry zipEntry = pak.CreateEntry($"Assets/{entry.Guid}.asset", CompressionLevel.Optimal);
                using Stream destination = zipEntry.Open();
                using FileStream source = File.OpenRead(entry.CachePath);
                source.CopyTo(destination);
                currentSize += entry.Size;
            }
        }
        finally
        {
            pak?.Dispose();
        }
    }

    private static void WriteManifest(string contentDir, IReadOnlyList<Guid> scenes, IReadOnlyList<ExportEntry> entries,
        EditorAssetBackend backend)
    {
        var root = EchoObject.NewCompound();
        root["format"] = new EchoObject(PlayerAssetBackend.ManifestFormat);
        root["defaultScene"] = new EchoObject(scenes.Count > 0 ? scenes[0].ToString() : Guid.Empty.ToString());

        var assets = EchoObject.NewCompound();
        foreach (ExportEntry entry in entries)
        {
            var tag = EchoObject.NewCompound();
            tag["file"] = new EchoObject($"Assets/{entry.Guid}.asset");
            tag["path"] = new EchoObject(entry.Path);
            tag["type"] = new EchoObject(entry.TypeName);
            tag["size"] = new EchoObject(entry.Size);
            tag["hard"] = GuidList(entry.Hard);
            tag["soft"] = GuidList(entry.Soft);
            assets[entry.Guid.ToString()] = tag;
        }
        root["assets"] = assets;

        var resources = EchoObject.NewList();
        foreach (ResourceEntry resource in backend.Resources)
        {
            var item = EchoObject.NewCompound();
            item["path"] = new EchoObject(resource.LoadPath);
            item["guid"] = new EchoObject(resource.Guid.ToString());
            item["type"] = new EchoObject(resource.TypeName);
            resources.ListAdd(item);
        }
        root["resources"] = resources;

        root.WriteToBinary(new FileInfo(Path.Combine(contentDir, "asset_manifest.bin")));
    }

    private static EchoObject GuidList(IReadOnlyList<Guid> guids)
    {
        var list = EchoObject.NewList();
        foreach (Guid guid in guids)
            list.ListAdd(new EchoObject(guid.ToString()));
        return list;
    }
}
