﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Sources
{
    public interface IAudioSource : IConfigurable
    {
        void Start();
        void Stop();
    }
}
