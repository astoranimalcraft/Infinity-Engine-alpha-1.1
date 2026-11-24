using System;
using System.Collections.Generic;

using Infinity.Graphite.Rendering;
using Infinity.Graphite.ShaderDef;
using Infinity.Graphite.ShaderDef.Compiler;
using Infinity.Runtime.Graphs;
using Infinity.Vector;

using Xunit;

namespace Infinity.Runtime.Test;

/// <summary>
/// Every material graph node must produce a shader Graphite accepts. The graph editor adds a node before the
/// user wires it, and a node that only works for some output pins is a trap that shows up as a compile error
/// the moment it is placed.
/// </summary>
public class MaterialGraphCompilerTests
{
    public static IEnumerable<object[]> NodeTypes()
    {
        foreach (string type in MaterialGraphNodes.All.Keys)
            if (type != MaterialGraphNodes.Output)
                yield return [type];
    }

    [Theory]
    [MemberData(nameof(NodeTypes))]
    public void EveryNodeCompilesIntoASurfaceShader(string type)
    {
        var graph = new MaterialGraph("Test");
        GraphNodeRecord output = graph.AddNode(MaterialGraphNodes.Output, new Float2(420f, 60f));
        GraphNodeRecord node = graph.AddNode(type, new Float2(40f, 40f));
        output.Inputs[0].Node = node.Id;

        AssertCompiles(graph);
    }

    public static IEnumerable<object[]> NodePins()
    {
        for (int pin = 0; pin < 7; pin++)
            foreach (string type in MaterialGraphNodes.All.Keys)
                if (type != MaterialGraphNodes.Output)
                    yield return [type, pin];
    }

    [Theory]
    [MemberData(nameof(NodePins))]
    public void EveryNodeCompilesOnEveryOutputPin(string type, int pin)
    {
        var graph = new MaterialGraph("Test");
        GraphNodeRecord output = graph.AddNode(MaterialGraphNodes.Output, new Float2(420f, 60f));
        GraphNodeRecord node = graph.AddNode(type, new Float2(40f, 40f));
        output.Inputs[pin].Node = node.Id;

        AssertCompiles(graph);
    }

    private static void AssertCompiles(MaterialGraph graph)
    {
        string source = graph.ToShaderSource();
        ShaderDefinition definition = ShaderParser.Parse(source);
        var compiler = new GlslShaderCompiler();
        var shader = new Shader(definition, compiler);

        foreach (ShaderPass pass in shader.Definition.Passes ?? [])
            compiler.CompileProgram(pass, ReadOnlySpan<Keyword>.Empty);
    }

    [Fact]
    public void PostProcessOutputCompiles()
    {
        var graph = new MaterialGraph("Test") { PostProcess = true };
        GraphNodeRecord output = graph.AddNode(MaterialGraphNodes.Output, new Float2(420f, 60f));
        GraphNodeRecord scene = graph.AddNode(MaterialGraphNodes.SceneColor, new Float2(40f, 40f));
        output.Inputs[0].Node = scene.Id;

        string source = MaterialGraphCompiler.CompilePostProcessFragment(graph);
        Assert.Contains("void main()", source);
        Assert.Contains("Graphite_SampleSource", source);
    }
}
