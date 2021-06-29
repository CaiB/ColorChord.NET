﻿using ColorChord.NET.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChordTests
{
    [TestClass]
    public class ConfigTests
    {
        public class ConfigTargetValid : IConfigurableAttr
        {
            [ConfigInt("IntTest", 0, 20, 10)]
            public int IntTest = -1;

            [ConfigInt("ByteTest", 10, 250, 127)]
            public readonly byte ByteTest = 30;

            [ConfigInt("ThisKeyDoesNotExist", 10, 200, 10)]
            public ushort DefaultTest = 50;

            [ConfigFloat("FloatTest", 0F, 1F, 0.5F)]
            private float FloatTestName = 0F;

            [ConfigString("StringTest", "DefaultStringValue")]
            public string StringTest = "Config Failed";

            [ConfigBool("BoolTest", true)]
            public bool ThisTestFailed = true;

            public ConfigTargetValid(Dictionary<string, object> config)
            {
                Configurer.Configure(this, config);
            }

            public bool CheckFloat() => this.FloatTestName == 0.2F;
        }

        [TestMethod]
        public void TestValidConfig()
        {
            Dictionary<string, object> ConfigValues = new()
            {
                { "IntTest", 99 }, // out of range, will be 10
                { "ByteTest", 25 }, // valid
                { "FloatTest", 0.2F }, // valid
                { "StringTest", "ConfiguredCorrectly" }, // valid
                { "BoolTest", false }, // valid
                { "ExtraKey", new object() } // nonexistant, ignored
            };

            ConfigTargetValid Target = new(ConfigValues);
            Assert.AreEqual(10, Target.IntTest, "Int not set to default when out of range");
            Assert.AreEqual(25, Target.ByteTest, "Byte not set correctly");
            Assert.IsTrue(Target.CheckFloat(), "Float not set correctly");
            Assert.AreEqual("ConfiguredCorrectly", Target.StringTest, "String was not set correctly");
            Assert.IsFalse(Target.ThisTestFailed, "Bool was not set correctly");
        }
    }
}
