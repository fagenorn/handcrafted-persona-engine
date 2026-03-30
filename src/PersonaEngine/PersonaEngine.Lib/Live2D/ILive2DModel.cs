namespace PersonaEngine.Lib.Live2D;

public interface ILive2DModel : IDisposable
{
    string ModelId { get; }

    void SetParameter(string paramName, float value);

    void Update(float deltaTime);

    void Draw();

    void SetPosition(float x, float y);

    void SetScale(float scale);

    void SetRotation(float rotation);
}
