// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

using Infinity.Editor.Projects.Scripting;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Runtime.Graphs;
using Infinity.Vector;

using Xunit;

namespace Infinity.Editor.Test;

public class ScriptGraphTests
{
    [Fact]
    public void DefaultGraph_CompilesToLoadableAssembly()
    {
        var graph = new ScriptGraph("Smoke Graph");
        GraphNodeRecord start = graph.AddNode(ScriptGraphNodes.OnStart, Float2.Zero);
        GraphNodeRecord log = graph.AddNode(ScriptGraphNodes.Log, new Float2(240f, 0f));
        start.PropertyInputs["Next"] = log.Id;

        string source = graph.ToCSharp();
        Assert.Contains("public sealed class", source);
        Assert.Contains("Debug.Log", source);

        Assert.True(ScriptGraphCompiler.Compile(graph, out string message), message);
        Assert.True(File.Exists(graph.AssemblyPath), message);

        var host = new GameObject("Graph Host");
        var runner = host.AddComponent<ScriptGraphRunner>();
        runner.Graph = graph;
        runner.Reload();
        Assert.True(runner.IsRunning);
    }

    [Fact]
    public void MaterialGraph_GeneratesSurfaceAndPostProcessSource()
    {
        var graph = new MaterialGraph("Smoke Material");
        GraphNodeRecord output = graph.AddNode(MaterialGraphNodes.Output, Float2.Zero);
        GraphNodeRecord color = graph.AddNode(MaterialGraphNodes.ConstColor, new Float2(-240f, 0f));
        output.Inputs[0].Node = color.Id;

        string surface = graph.ToShaderSource();
        Assert.Contains("Surface \"Lit\"", surface);
        Assert.Contains("p.baseColor", surface);

        graph.PostProcess = true;
        string post = MaterialGraphCompiler.CompilePostProcessFragment(graph);
        Assert.Contains("outColor", post);
    }

    [Fact]
    public void QuaternionApi_IsAFirstClassPin()
    {
        Assert.Equal(ScriptPinKind.Quaternion, ScriptGraphApiCatalog.KindOf(typeof(Quaternion)));

        var graph = new ScriptGraph("Rotation Graph");
        graph.Variables.Add(new ScriptGraphVariable
        {
            Name = "rotation",
            Type = ScriptPinKind.Quaternion,
            Vector = new Float3(10f, 20f, 30f),
        });

        string source = graph.ToCSharp();
        Assert.Contains("Quaternion rotation = Quaternion.FromEuler", source);
        Assert.Contains("10", source);
        Assert.Contains("30", source);

        Quaternion value = ScriptGraphOps.ToQuat(new Float3(0f, 90f, 0f));
        Assert.Equal(90f, Quaternion.ToEuler(value).Y, 1);
    }
}
