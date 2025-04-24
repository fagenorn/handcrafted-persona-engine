using System.Drawing;

using FontStashSharp;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.Text.Rendering;

using Silk.NET.OpenGL;

using StbImageSharp;

using Texture = PersonaEngine.Lib.UI.Common.Texture;

namespace PersonaEngine.Lib.UI;

/// <summary>
///     Manages caching and loading of fonts and textures.
/// </summary>
public class FontProvider : IStartupTask
{
    private static readonly string _fontsPath = Path.Combine("Resources", "Fonts");

    private static readonly string _imagesPath = Path.Combine("Resources", "Imgs");

    private readonly Dictionary<string, FontSystem> _fontCache = new();

    private readonly ILogger<FontProvider> _logger;

    private readonly Dictionary<string, Texture> _textureCache = new();

    private Texture2DManager _texture2DManager = null!;

    // static FontProvider()
    // {
    //     FontSystemDefaults.FontLoader = new SixLaborsFontLoader();
    // }

    public FontProvider(ILogger<FontProvider> logger) { _logger = logger; }

    public void Execute(GL gl) { _texture2DManager = new Texture2DManager(gl); }

    public Task<IReadOnlyList<string>> GetAvailableFontsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if ( !Directory.Exists(_fontsPath) )
            {
                _logger.LogWarning("Fonts directory not found: {Path}", _fontsPath);

                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            var fontFiles = Directory.GetFiles(_fontsPath, "*.ttf");
            var fontNames = new List<string>(fontFiles.Length);
            foreach ( var file in fontFiles )
            {
                var voiceId = Path.GetFileName(file);
                fontNames.Add(voiceId);
            }

            _logger.LogInformation("Found {Count} available fonts", fontNames.Count);

            return Task.FromResult<IReadOnlyList<string>>(fontNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available fonts");

            throw;
        }
    }

    public FontSystem GetFontSystem(string fontName)
    {
        if ( !_fontCache.TryGetValue(fontName, out var fontSystem) )
        {
            fontSystem = new FontSystem();
            var fontData = File.ReadAllBytes(Path.Combine(_fontsPath, fontName));
            fontSystem.AddFont(fontData);
            _fontCache[fontName] = fontSystem;
        }

        return fontSystem;
    }

    public Texture GetTexture(string imageName)
    {
        if ( !_textureCache.TryGetValue(imageName, out var texture) )
        {
            using var stream      = File.OpenRead(Path.Combine(_imagesPath, imageName));
            var       imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Premultiply alpha
            unsafe
            {
                fixed (byte* b = imageResult.Data)
                {
                    var ptr = b;
                    for ( var i = 0; i < imageResult.Data.Length; i += 4, ptr += 4 )
                    {
                        var falpha = ptr[3] / 255.0f;
                        ptr[0] = (byte)(ptr[0] * falpha);
                        ptr[1] = (byte)(ptr[1] * falpha);
                        ptr[2] = (byte)(ptr[2] * falpha);
                    }
                }
            }

            texture = (Texture)_texture2DManager.CreateTexture(imageResult.Width, imageResult.Height);
            _texture2DManager.SetTextureData(texture, new Rectangle(0, 0, (int)texture.Width, (int)texture.Height), imageResult.Data);
            _textureCache[imageName] = texture;
        }

        return texture;
    }
}