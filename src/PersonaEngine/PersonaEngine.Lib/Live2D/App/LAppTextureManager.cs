using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     画像読み込み、管理を行うクラス。
/// </summary>
public class LAppTextureManager(LAppDelegate lapp)
{
    private readonly List<TextureInfo> _textures = [];

    /// <summary>
    ///     画像読み込み
    /// </summary>
    /// <param name="fileName">読み込む画像ファイルパス名</param>
    /// <returns>画像情報。読み込み失敗時はNULLを返す</returns>
    public TextureInfo CreateTextureFromPngFile(string fileName)
    {
        //search loaded texture already.
        var item = _textures.FirstOrDefault(a => a.FileName == fileName);
        if (item != null)
        {
            return item;
        }

        // Using SixLabors.ImageSharp to load the image
        using var image = Image.Load<Rgba32>(fileName);
        var GL = lapp.GL;

        // OpenGL用のテクスチャを生成する
        var textureId = GL.GenTexture();
        GL.BindTexture(GL.GL_TEXTURE_2D, textureId);

        // Create a byte array to hold image data
        var pixelData = new byte[4 * image.Width * image.Height]; // 4 bytes per pixel (RGBA)

        // Access the pixel data
        image.ProcessPixelRows(accessor =>
        {
            // For each row
            for (var y = 0; y < accessor.Height; y++)
            {
                // Get the pixel row span
                var row = accessor.GetRowSpan(y);

                // For each pixel in the row
                for (var x = 0; x < row.Length; x++)
                {
                    // Calculate the position in our byte array
                    var arrayPos = (y * accessor.Width + x) * 4;

                    // Copy RGBA components
                    pixelData[arrayPos + 0] = row[x].R;
                    pixelData[arrayPos + 1] = row[x].G;
                    pixelData[arrayPos + 2] = row[x].B;
                    pixelData[arrayPos + 3] = row[x].A;
                }
            }
        });

        // Pin the byte array in memory so we can pass a pointer to OpenGL
        var pinnedArray = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
        try
        {
            // Get the address of the first byte in the array
            var pointer = pinnedArray.AddrOfPinnedObject();

            // Pass the pointer to OpenGL
            GL.TexImage2D(
                GL.GL_TEXTURE_2D,
                0,
                GL.GL_RGBA,
                image.Width,
                image.Height,
                0,
                GL.GL_RGBA,
                GL.GL_UNSIGNED_BYTE,
                pointer
            );
            GL.GenerateMipmap(GL.GL_TEXTURE_2D);
            GL.TexParameteri(
                GL.GL_TEXTURE_2D,
                GL.GL_TEXTURE_MIN_FILTER,
                GL.GL_LINEAR_MIPMAP_LINEAR
            );
            GL.TexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, GL.GL_LINEAR);
        }
        finally
        {
            // Free the pinned array
            pinnedArray.Free();
        }

        GL.BindTexture(GL.GL_TEXTURE_2D, 0);

        var info = new TextureInfo
        {
            FileName = fileName,
            Width = image.Width,
            Height = image.Height,
            ID = textureId,
        };

        _textures.Add(info);

        return info;
    }

    /// <summary>
    ///     指定したテクスチャIDの画像を解放する
    /// </summary>
    /// <param name="textureId">解放するテクスチャID</param>
    public void ReleaseTexture(int textureId)
    {
        foreach (var item in _textures)
        {
            if (item.ID == textureId)
            {
                _textures.Remove(item);

                break;
            }
        }
    }

    /// <summary>
    ///     テクスチャIDからテクスチャ情報を得る
    /// </summary>
    /// <param name="textureId">取得したいテクスチャID</param>
    /// <returns>テクスチャが存在していればTextureInfoが返る</returns>
    public TextureInfo? GetTextureInfoById(int textureId)
    {
        return _textures.FirstOrDefault(a => a.ID == textureId);
    }
}
