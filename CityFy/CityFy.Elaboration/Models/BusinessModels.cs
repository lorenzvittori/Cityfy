namespace CityFy.Elaboration.Models;

public class Node
{
    public string? Tag { get; set; }
    public double Weight { get; set; }
}

public class Edge
{
    public string? From { get; set; }
    public string? To { get; set; }
    public double Weight { get; set; }
}

public class Graph
{
    public string? SeedTag { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<Node>? Nodes { get; set; }
    public List<Edge>? Edges { get; set; }
}
