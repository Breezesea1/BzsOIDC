namespace BzsOIDC.Contracts;

/// <summary>Opaque server-generated resource version. Its contents have no client semantics.</summary>
public readonly record struct OpaqueResourceVersion(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Opaque HTTP entity tag representation.</summary>
public readonly record struct ResourceETag(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Alias used when the HTTP header representation is named explicitly.</summary>
public readonly record struct OpaqueETag(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ETag(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Short alias for callers that refer to a version directly.</summary>
public readonly record struct ResourceVersion(string Value)
{
    public override string ToString() => Value;
}
