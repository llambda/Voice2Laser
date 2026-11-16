namespace Voice2Laser.Models;

public sealed record RecognitionEvent(DateTime Timestamp, string Text, string ActionName);
