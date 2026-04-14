using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Voice;

public sealed class VoiceMetadataCatalogTests
{
    [Fact]
    public void Resolves_Known_Kokoro_Voice_From_Sidecar()
    {
        var catalog = new VoiceMetadataCatalog();

        var descriptor = catalog.Resolve(VoiceEngine.Kokoro, "af_heart");

        Assert.Equal("Heart", descriptor.DisplayName);
        Assert.Equal(VoiceGender.Female, descriptor.Gender);
    }

    [Fact]
    public void Unknown_Voice_Falls_Back_To_Id_As_DisplayName()
    {
        var catalog = new VoiceMetadataCatalog();

        var descriptor = catalog.Resolve(VoiceEngine.Kokoro, "zzz_not_real");

        Assert.Equal("zzz_not_real", descriptor.DisplayName);
        Assert.Null(descriptor.Gender);
    }

    [Fact]
    public void List_Returns_Only_Voices_For_Requested_Engine()
    {
        var catalog = new VoiceMetadataCatalog();

        var kokoro = catalog.List(VoiceEngine.Kokoro);

        Assert.All(kokoro, d => Assert.Equal(VoiceEngine.Kokoro, d.Engine));
        Assert.NotEmpty(kokoro);
    }
}
