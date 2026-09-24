using System.ComponentModel;
using System.Threading;

namespace Ymm4VoiceQualityAssist.Runtime;

/// <summary>In-memory diagnostics only; never uploads project text or engine data.</summary>
public sealed class AssistRuntimeStatus : INotifyPropertyChanged
{
    public static AssistRuntimeStatus Current { get; } = new();
    public string Code { get; private set; } = "STARTING";
    public string Message { get; private set; } = "発音補助の起動を確認しています。";
    long discardedResults;
    long generationAttempts;
    public long DiscardedResults => Interlocked.Read(ref discardedResults);
    public long GenerationAttempts => Interlocked.Read(ref generationAttempts);
    public void NoteGeneration() => Interlocked.Increment(ref generationAttempts);
    public void NoteDiscarded() => Interlocked.Increment(ref discardedResults);
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Report(string code, string message)
    {
        if (Code == code && Message == message) return;
        Code = code;
        Message = message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
    }
}
