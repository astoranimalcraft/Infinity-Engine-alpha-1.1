// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.Panels;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Editor.Projects;

/// <summary>
/// Finds the textures that belong to a mesh and assigns them to a Lit material in one step. The maps are recognised
/// from the file name, which is the convention every downloaded model and texture pack uses: BaseColor, Albedo,
/// Normal, Roughness, Metallic, AO, Height and Emission. A mesh without a material gets one created next to it.
/// </summary>
public static class MeshTextureAutoAssign
{
    private static readonly (string Property, string[] Tokens)[] s_mapKinds =
    [
        ("base_color_map", ["basecolor", "base_color", "albedo", "diffuse", "colour", "color", "bc"]),
        ("geometry_normal_map", ["normal", "norm", "nrm", "nor"]),
        ("specular_roughness_map", ["roughness", "rough", "rgh", "gloss"]),
        ("base_metalness_map", ["metallic", "metalness", "metal", "met"]),
        ("ambient_occlusion_map", ["ambientocclusion", "ambient_occlusion", "occlusion", "ao"]),
        ("geometry_height_map", ["height", "displacement", "disp", "bump"]),
        ("emission_color_map", ["emission", "emissive", "emit", "glow"]),
        ("geometry_opacity_map", ["opacity", "alpha", "transparency", "mask"]),
    ];

    /// <summary>
    /// Assigns every recognisable texture to <paramref name="renderer"/>'s material. Textures come from the current
    /// Project selection first, then from the mesh's own folder. Returns the number of texture slots assigned.
    /// </summary>
    public static int Assign(MeshRenderer renderer)
    {
        if (renderer.IsNotValid()) return 0;
        return AssignCore(renderer, renderer.Mesh, renderer.Material, material =>
        {
            Undo.RecordObject(renderer, "Assign Material");
            renderer.Material = material;
        });
    }

    /// <summary>
    /// The same one-click texture setup for a skinned mesh. It fills slot 0, creating a Lit material next to the
    /// mesh when the renderer has none, so an imported character with maps beside it becomes textured immediately.
    /// </summary>
    public static int Assign(SkinnedMeshRenderer renderer)
    {
        if (renderer.IsNotValid()) return 0;
        IMaterialRenderer slots = renderer;
        Material? current = slots.MaterialSlotCount > 0 ? slots.GetMaterial(0) : null;
        return AssignCore(renderer, renderer.SharedMesh, current, material =>
        {
            Undo.RecordObject(renderer, "Assign Material");
            slots.SetMaterial(0, material);
        });
    }

    private static int AssignCore(Component owner, Mesh? mesh, Material? material, Action<Material> setMaterial)
    {
        List<Texture2D> textures = TexturesFromSelection();
        string? folder = MeshFolder(mesh);
        if (textures.Count == 0 && folder != null)
            textures = TexturesInFolder(folder);

        if (textures.Count == 0)
        {
            Toasts.Show("No textures found", "Select the textures in the Project panel, or put them next to the mesh.", ToastType.Warning, 4f);
            return 0;
        }

        if (material.IsNotValid())
        {
            material = CreateMaterial(owner, mesh, folder);
            if (material.IsNotValid()) return 0;
            setMaterial(material!);
        }

        Undo.RecordObject(material!, "Auto Assign Textures");
        int assigned = 0;
        foreach ((string property, string[] tokens) in s_mapKinds)
        {
            Texture2D? texture = FindMap(textures, tokens);
            if (texture.IsNotValid()) continue;
            material!.SetTexture(property, texture);
            assigned++;
        }

        // Normal maps are tangent space and the Lit shader expects the map's green channel as authored.
        if (material!.HasProperty("normal_scale"))
            material.SetFloat("normal_scale", 1f);

        EditorUtility.SetDirty(material);
        Toasts.Show("Textures assigned", $"{assigned} map(s) assigned to {material.Name}.", ToastType.Info, 4f);
        return assigned;
    }

    private static Material? CreateMaterial(Component owner, Mesh? mesh, string? meshFolder)
    {
        string meshName = mesh.IsValid() ? mesh!.Name : owner.GameObject.Name;
        var material = new Material(Shader.LoadDefault(DefaultShader.Lit)) { Name = $"{meshName} Material" };

        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null || meshFolder == null) return material;

        string absolute = AssetCreateMenu.GetAbsoluteFolder(meshFolder);
        if (!Directory.Exists(absolute)) return material;

        string name = AssetCreateMenu.FindUniqueName(absolute, material.Name, ".mat");
        string relative = string.IsNullOrEmpty(meshFolder) ? name : $"{meshFolder}/{name}";
        backend.CreateAsset(material, relative);
        backend.ImportFile(relative);

        Guid guid = backend.PathToGuid(relative);
        Material? loaded = guid != Guid.Empty ? AssetDatabase.Get<Material>(guid) : null;
        return loaded.IsValid() ? loaded : material;
    }

    private static string? MeshFolder(Mesh? mesh)
    {
        if (mesh.IsNotValid()) return null;
        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(mesh!.AssetID);
        if (entry == null || string.IsNullOrEmpty(entry.Path)) return null;
        string? folder = Path.GetDirectoryName(entry.Path)?.Replace('\\', '/');
        return folder;
    }

    private static List<Texture2D> TexturesFromSelection()
    {
        var textures = new List<Texture2D>();
        var seen = new HashSet<Guid>();
        foreach (ContentItem item in Selection.GetSelected<ContentItem>())
        {
            if (item.IsFolder || item.Guid == Guid.Empty || !seen.Add(item.Guid)) continue;
            Texture2D? texture = AssetDatabase.Load<Texture2D>(item.Guid);
            if (texture.IsValid()) textures.Add(texture!);
        }
        return textures;
    }

    private static List<Texture2D> TexturesInFolder(string relativeFolder)
    {
        var textures = new List<Texture2D>();
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null) return textures;

        string absolute = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absolute)) return textures;

        foreach (string file in Directory.EnumerateFiles(absolute, "*.*", SearchOption.TopDirectoryOnly))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".hdr" or ".exr")) continue;
            Guid guid = backend.PathToGuid(Path.GetRelativePath(Project.Current!.AssetsPath, file).Replace('\\', '/'));
            if (guid == Guid.Empty) continue;
            Texture2D? texture = AssetDatabase.Load<Texture2D>(guid);
            if (texture.IsValid()) textures.Add(texture!);
        }
        return textures;
    }

    private static Texture2D? FindMap(List<Texture2D> textures, string[] tokens)
    {
        foreach (Texture2D texture in textures)
        {
            string name = texture.Name.ToLowerInvariant();
            if (tokens.Any(token => name.Contains(token, StringComparison.Ordinal)))
                return texture;
        }
        return null;
    }
}
