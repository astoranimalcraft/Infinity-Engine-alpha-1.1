using Infinity.Runtime;

namespace Infinity.Editor.Importers;

/// <summary>
/// Imports .terraindata files - Echo-serialized TerrainData objects.
/// </summary>
[ImporterFor(".terraindata")]
public class TerrainDataImporter : AssetImporter
{
    public override int Version => 1;
    public override EchoSource Source => EchoSource.Text;

    public override bool Import(ImportContext ctx) => ImportHelper.ImportEcho<TerrainData>(ctx, "terrain data");
}
