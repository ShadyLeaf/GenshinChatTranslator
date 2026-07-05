using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation;

public sealed record LlmTranslationRequestStatus(
    long Sequence,
    DateTime SentAt,
    string Model,
    ChatLanguage TargetLanguage,
    int MessageCount,
    int CharacterCount,
    string Indices,
    string ThinkingType);

public static class LlmTranslationRequestStatusStore
{
    private static readonly object Gate = new();
    private static long _sequence;
    private static LlmTranslationRequestStatus? _latest;

    public static LlmTranslationRequestStatus? Latest
    {
        get
        {
            lock (Gate)
            {
                return _latest;
            }
        }
    }

    public static LlmTranslationRequestStatus RecordSent(
        string model,
        ChatLanguage targetLanguage,
        int messageCount,
        int characterCount,
        string indices,
        string thinkingType)
    {
        var status = new LlmTranslationRequestStatus(
            Interlocked.Increment(ref _sequence),
            DateTime.Now,
            model,
            targetLanguage,
            messageCount,
            characterCount,
            indices,
            thinkingType);

        lock (Gate)
        {
            _latest = status;
        }

        return status;
    }
}
