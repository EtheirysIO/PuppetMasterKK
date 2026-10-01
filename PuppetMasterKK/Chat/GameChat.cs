using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace PuppetMasterKK;

// Sends a line through the game's chat box, exactly as if typed: a "/command" runs, plain text is posted to the active
// channel. Based on XivCommon's Chat (MIT, Copyright (c) 2021 Anna Clemens). Framework thread only.
internal static unsafe class GameChat
{
    // The chat box takes at most this many UTF-8 bytes.
    public const int MaxBytes = 500;

    // Refuses (throws ArgumentException) anything the chat box wouldn't take as typed: empty, too long, or with
    // characters the game's own sanitizer would strip.
    public static void Send(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length == 0)
            throw new ArgumentException("The message is empty.", nameof(message));
        if (bytes.Length > MaxBytes)
            throw new ArgumentException($"The message is longer than {MaxBytes} bytes.", nameof(message));
        if (message.Length != Sanitize(message).Length)
            throw new ArgumentException("The message has characters the chat box doesn't allow.", nameof(message));

        var text = Utf8String.FromSequence(bytes);
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(text);
        }
        finally
        {
            text->Dtor(true);
        }
    }

    private static string Sanitize(string message)
    {
        var text = Utf8String.FromString(message);
        try
        {
            text->SanitizeString((AllowedEntities)0x27F);
            return text->ToString();
        }
        finally
        {
            text->Dtor(true);
        }
    }
}
