namespace SpaceCraft.Api.Common;

/// <summary>Why a delete was refused, so the caller can say something specific.</summary>
public sealed record DeletionRefusal(string Reason);
