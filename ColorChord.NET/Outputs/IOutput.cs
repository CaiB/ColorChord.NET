﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Outputs
{
    public interface IOutput : IConfigurable
    {
        void Start();
        void Stop();
        void Dispatch();
    }
}
