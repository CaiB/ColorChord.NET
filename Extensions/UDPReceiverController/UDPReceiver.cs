using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Extensions.UDPReceiverController
{
    public class UDPReceiver : Controller
    {
        [ConfigInt("Port", 0, 65535, 2775)]
        private ushort Port = 2775;


        public UDPReceiver(string name, Dictionary<string, object> config, IControllerInterface controllerInterface) : base(name, config, controllerInterface)
        {
            
        }

        public override void Start()
        {
            ISetting? Setting = this.Interface.FindSetting("Visualizers.SampleLinear.Enable");
        }

        public override void Stop()
        {
            
        }
    }
}
