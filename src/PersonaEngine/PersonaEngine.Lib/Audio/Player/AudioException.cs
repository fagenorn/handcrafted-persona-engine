namespace PersonaEngine.Lib.Audio.Player;

public class AudioException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class AudioDeviceNotFoundException(string message) : Exception(message);

public class AudioPlayerInitializationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
