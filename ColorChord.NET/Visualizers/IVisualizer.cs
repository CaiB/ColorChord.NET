using ColorChord.NET.Outputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Visualizers
{
    public interface IVisualizer
    {
        string Name { get; set; }
        void Start();
        void Stop();
        void AttachOutput(IOutput output);
    }
}
