using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ConditionCommandSender.Services;

/// <summary>
/// Sends text through the same native chat entry path used by the game client.
/// Must be called on the framework thread.
/// </summary>
public static class ChatCommandSender
{
    public static unsafe void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Utf8String* message = Utf8String.FromSequence(bytes);

        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(message);
        }
        finally
        {
            message->Dtor(true);
        }
    }
}
