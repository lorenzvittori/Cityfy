using CityFy.Elaboration.Models;
using CityFy.Elaboration.SubServices;

namespace CityFy.Elaboration.Tests;

public sealed class GenreHierarchyTests
{
    // rock -> alternative rock -> grunge ; electronic -> house ; "influenced by" must be ignored
    internal static GenreGraph SampleGraph() => new(
        [
            new(1, "g1", "rock"),
            new(2, "g2", "alternative rock"),
            new(3, "g3", "grunge"),
            new(4, "g4", "electronic"),
            new(5, "g5", "house"),
            new(6, "g6", "electronic rock"),
        ],
        [
            new(1, 2, "subgenre"),
            new(2, 3, "subgenre"),
            new(4, 5, "subgenre"),
            new(1, 6, "subgenre"),
            new(4, 6, "subgenre"),
            new(5, 1, "influenced by"),
        ]);

    [Fact]
    public void Roots_FollowSubgenreChainToTheTop()
    {
        var h = new GenreHierarchy(SampleGraph());

        Assert.Equal([1], h.GetRoots(3));
        Assert.Equal([4], h.GetRoots(5));
    }

    [Fact]
    public void Roots_OfTopLevelGenre_IsItself()
    {
        var h = new GenreHierarchy(SampleGraph());

        Assert.Equal([1], h.GetRoots(1));
    }

    [Fact]
    public void Roots_WithTwoParents_ReturnsBoth()
    {
        var h = new GenreHierarchy(SampleGraph());

        Assert.Equal([1, 4], h.GetRoots(6));
    }

    [Fact]
    public void Roots_IgnoreNonSubgenreRelations()
    {
        // house "influenced by" rock must not make rock an ancestor of house
        var h = new GenreHierarchy(SampleGraph());

        Assert.DoesNotContain(1, h.GetRoots(5));
    }

    [Fact]
    public void Roots_WithCycle_DoNotLoopAndFallBackToTheGenre()
    {
        var graph = new GenreGraph(
            [new(1, "a", "a"), new(2, "b", "b")],
            [new(1, 2, "subgenre"), new(2, 1, "subgenre")]);

        var roots = new GenreHierarchy(graph).GetRoots(1);

        Assert.Equal([1], roots);
    }
}
