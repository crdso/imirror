namespace iMirror.Core.Features;

public sealed record FeatureStatus(string Name, string Description)
{
    public string StateText => "Não implementado";
    public bool IsConnected => false;
}
