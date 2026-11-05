namespace Shellboat.Core.Connections;

/// <summary>A folder in the connection tree. <see cref="ParentId"/> null means a top-level folder.</summary>
public sealed record Folder(Guid Id, string Name, Guid? ParentId, int SortOrder = 0, NodeIcon? Icon = null);
