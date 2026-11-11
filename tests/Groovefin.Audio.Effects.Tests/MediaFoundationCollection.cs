namespace Groovefin.Audio.Effects.Tests;

/// <summary>
/// The tests that use Media Foundation run one class after another: the equalizer effect's parameters are the
/// process's, so a player of one class must not play while another class measures the effect.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MediaFoundationCollection
{
    public const string Name = "Media Foundation";
}
