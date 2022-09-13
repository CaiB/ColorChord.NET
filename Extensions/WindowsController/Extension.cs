using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.WindowsController
{
    public class Extension : IExtension
    {
        public string Name => "Windows Controller";
        public string Description => "Adds Windows-specific controller features";
        public uint APIVersion => ColorChordAPI.APIVersion;

        internal static MessageHandler? WindowsInterface;

        public void Initialize()
        {
            WindowsInterface = new();
        }

        public void PostInitialize()
        {
            
        }

        public void Shutdown()
        {
            
        }


    }
}