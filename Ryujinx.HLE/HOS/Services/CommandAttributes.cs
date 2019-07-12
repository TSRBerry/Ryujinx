﻿using System;

namespace Ryujinx.HLE.HOS.Services
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class CommandAttribute : Attribute
    {
        public readonly int Id;

        public CommandAttribute(int id) => Id = id;
    }
}