using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.UDPReceiverController;

public class UDPReceiverController : IExtension
{
    public string Name => "UDP Receiver Controller";
    public string Description => "Allows you to control ColorChord.NET components via plain UDP packets";
    public uint APIVersion => ColorChordAPI.APIVersion;

    public UDPReceiverController()
    {
        Console.WriteLine("EXT UDPRC: Ctor!");
    }

    public void Initialize()
    {
        Console.WriteLine("EXT UDPRC: Init!");
    }

    public void PostInitialize()
    {
        Console.WriteLine("EXT UDPRC: PostInit!");
    }

    public void Shutdown()
    {
        Console.WriteLine("EXT UDPRC: Shutdown!");
    }
}